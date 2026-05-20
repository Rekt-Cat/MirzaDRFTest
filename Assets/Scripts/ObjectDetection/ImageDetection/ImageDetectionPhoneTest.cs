using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.UI;
using Logger = Workflow.Utils.Logger;

public class ImageDetectionPhoneTest : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] RawImage cameraDisplay;
    [SerializeField] bool useFrontCamera = false;

    [Header("Model")]
    [SerializeField] ModelAsset modelAsset;
    [SerializeField, Range(0f, 1f)] float scoreThreshold = 0.65f;
    [SerializeField, Range(0f, 1f)] float nmsIoUThreshold = 0.45f;
    [SerializeField, Range(0f, 1f)] float smoothAlpha = 0.2f;

    [Header("Bounding Boxes")]
    [SerializeField] Canvas overlayCanvas;
    [SerializeField] GameObject boundingBoxPrefab;
    [SerializeField] int maxBoxes = 20;

    const string InputName  = "pixel_values";
    const string BoxesName  = "sigmoid";
    const string LogitsName = "outputs";

    WebCamTexture _webCam;
    IWorker _worker;
    bool _isInferencing;
    bool _shapeLogged;
    readonly List<BoundingBoxOverlay> _boxPool = new();
    List<DetectionResult> _latestResults = new();  // updated by inference coroutine
    List<Rect> _smoothedRects = new();             // EMA-smoothed positions, updated every frame
    readonly Logger _logger = new(true, nameof(ImageDetectionPhoneTest));

    static readonly string[] Labels =
    {
        "Nothing", "Cover", "Housing", "Plunger", "Screw",
        "Tool 1", "Tool 2", "spring", "v-lock-plate"
    };

    static readonly Color[] ClassColors =
    {
        Color.gray,
        Color.cyan,
        Color.green,
        Color.yellow,
        Color.red,
        new Color(1f, 0.5f, 0f),
        Color.magenta,
        Color.white,
        new Color(0.4f, 0.6f, 1f)
    };

    public event Action<IReadOnlyList<DetectionResult>> OnDetectionsUpdated;

    void Start()
    {
        InitCamera();
        InitModel();
        InitBoxPool();
    }

    void InitCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            _logger.LogError("No camera found on device.");
            return;
        }

        string deviceName = devices[0].name;
        foreach (var d in devices)
        {
            if (d.isFrontFacing == useFrontCamera)
            {
                deviceName = d.name;
                break;
            }
        }

        _webCam = new WebCamTexture(deviceName, 1280, 720, 30);
        _webCam.Play();

        if (cameraDisplay != null)
        {
            cameraDisplay.texture = _webCam;
            // Fix portrait rotation on Android
            cameraDisplay.rectTransform.localEulerAngles = new Vector3(0, 0, -_webCam.videoRotationAngle);
            if (_webCam.videoVerticallyMirrored)
                cameraDisplay.rectTransform.localScale = new Vector3(1, -1, 1);
        }
    }

    void InitModel()
    {
        Model model = ModelLoader.Load(modelAsset);
        Debug.Log("[ImageDetectionPhoneTest] Declared outputs: " + string.Join(", ", model.outputs.ConvertAll(o => o.name)));
        _worker = WorkerFactory.CreateWorker(BackendType.CPU, model);
    }

    void InitBoxPool()
    {
        if (overlayCanvas == null)
        {
            var canvasGo = new GameObject("DetectionCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            overlayCanvas = canvas;
        }

        for (int i = 0; i < maxBoxes; i++)
        {
            if (boundingBoxPrefab == null) break;
            var go = Instantiate(boundingBoxPrefab, overlayCanvas.transform, false);
            var box = go.GetComponent<BoundingBoxOverlay>();
            if (box != null) _boxPool.Add(box);
            go.SetActive(false);
        }
    }

    void Update()
    {
        if (_webCam == null || !_webCam.isPlaying) return;

        // EMA-smooth displayed boxes toward latest inference results every frame
        ApplyEMASmoothing();

        if (!_isInferencing)
            StartCoroutine(InferenceCoroutine());
    }

    IEnumerator InferenceCoroutine()
    {
        _isInferencing = true;

        // Named input "pixel_values" — matches Android team's session.run() call
        TensorFloat input = TextureConverter.ToTensor(_webCam, new TextureTransform().SetDimensions(640, 640, 3));
        var inputs = new Dictionary<string, Tensor> { { InputName, input } };

        var enumerator = _worker.ExecuteLayerByLayer(inputs);
        int layer = 0;
        while (enumerator.MoveNext())
        {
            if (++layer % 10 == 0)
                yield return null;
        }
        input.Dispose();

        _latestResults = ParseDetections();
        OnDetectionsUpdated?.Invoke(_latestResults);

        _isInferencing = false;
    }

    // Applies EMA every frame so boxes glide smoothly (SMOOTH_ALPHA = 0.2 matches Android)
    void ApplyEMASmoothing()
    {
        int count = Mathf.Min(_latestResults.Count, _boxPool.Count);

        // Grow smoothed rect list to match current detection count
        while (_smoothedRects.Count < count)
            _smoothedRects.Add(_latestResults[_smoothedRects.Count].normalizedRect);
        while (_smoothedRects.Count > count)
            _smoothedRects.RemoveAt(_smoothedRects.Count - 1);

        // Deactivate excess pool slots
        for (int i = count; i < _boxPool.Count; i++)
            _boxPool[i].gameObject.SetActive(false);

        for (int i = 0; i < count; i++)
        {
            Rect target = _latestResults[i].normalizedRect;
            Rect current = _smoothedRects[i];

            // EMA: smoothed = (1-alpha)*current + alpha*target
            _smoothedRects[i] = new Rect(
                Mathf.Lerp(current.x,      target.x,      smoothAlpha),
                Mathf.Lerp(current.y,      target.y,      smoothAlpha),
                Mathf.Lerp(current.width,  target.width,  smoothAlpha),
                Mathf.Lerp(current.height, target.height, smoothAlpha)
            );

            var smoothedDet = new DetectionResult
            {
                normalizedRect = _smoothedRects[i],
                labelId        = _latestResults[i].labelId,
                className      = _latestResults[i].className,
                score          = _latestResults[i].score
            };

            _boxPool[i].gameObject.SetActive(true);
            _boxPool[i].SetBox(smoothedDet, GetClassColor(_latestResults[i].labelId));
        }
    }

    void OnDestroy()
    {
        _webCam?.Stop();
        _worker?.Dispose();
        foreach (var box in _boxPool)
            if (box != null) Destroy(box.gameObject);
        _boxPool.Clear();
    }

    List<DetectionResult> ParseDetections()
    {
        // boxes  = "sigmoid" → [1, 300, 4]  cx,cy,w,h already in [0,1]
        // logits = "outputs" → [1, 300, 9]  raw class logits, apply sigmoid per class
        TensorFloat rawBoxes  = SafePeekFloat(BoxesName);
        TensorFloat rawLogits = SafePeekFloat(LogitsName);

        if (rawBoxes == null || rawLogits == null)
        {
            _logger.LogError($"Outputs not found — expected '{BoxesName}' and '{LogitsName}'. Check Logcat for declared output names.");
            return new List<DetectionResult>();
        }

        using TensorFloat cpuBoxes  = rawBoxes.ReadbackAndClone();
        using TensorFloat cpuLogits = rawLogits.ReadbackAndClone();

        if (!_shapeLogged)
        {
            Debug.Log($"[ImageDetectionPhoneTest] boxes={cpuBoxes.shape}  logits={cpuLogits.shape}");
            _shapeLogged = true;
        }

        int numDetections = cpuBoxes.shape[1];
        int numClasses    = cpuLogits.shape[2];
        var results       = new List<DetectionResult>();

        for (int i = 0; i < numDetections; i++)
        {
            // Find best class — mirrors Android team's logic exactly
            float bestScore = 0f;
            int   bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float s = 1f / (1f + Mathf.Exp(-cpuLogits[0, i, c]));
                if (s > bestScore) { bestScore = s; bestClass = c; }
            }

            if (bestScore < scoreThreshold) continue;
            if (bestClass <= 0 || bestClass >= Labels.Length) continue;

            // boxes are already sigmoid-activated — cx,cy,w,h in [0,1]
            float cx = cpuBoxes[0, i, 0];
            float cy = cpuBoxes[0, i, 1];
            float bw = cpuBoxes[0, i, 2];
            float bh = cpuBoxes[0, i, 3];
            float x  = Mathf.Clamp01(cx - bw * 0.5f);
            float y  = Mathf.Clamp01(1f - (cy + bh * 0.5f));

            results.Add(new DetectionResult
            {
                normalizedRect = new Rect(x, y, Mathf.Clamp01(bw), Mathf.Clamp01(bh)),
                labelId        = bestClass,
                className      = Labels[bestClass],
                score          = bestScore
            });
        }

        return NMS(results, nmsIoUThreshold);
    }

    List<DetectionResult> NMS(List<DetectionResult> dets, float iouThreshold)
    {
        dets.Sort((a, b) => b.score.CompareTo(a.score));
        var keep       = new List<DetectionResult>();
        var suppressed = new bool[dets.Count];

        for (int i = 0; i < dets.Count; i++)
        {
            if (suppressed[i]) continue;
            keep.Add(dets[i]);
            for (int j = i + 1; j < dets.Count; j++)
            {
                if (!suppressed[j] && IoU(dets[i].normalizedRect, dets[j].normalizedRect) >= iouThreshold)
                    suppressed[j] = true;
            }
        }
        return keep;
    }

    static float IoU(Rect a, Rect b)
    {
        float ix = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
        float iy = Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
        float intersection = ix * iy;
        float union = a.width * a.height + b.width * b.height - intersection;
        return union <= 0f ? 0f : intersection / union;
    }

    TensorFloat SafePeekFloat(string name)
    {
        try { return _worker.PeekOutput(name) as TensorFloat; }
        catch { return null; }
    }

    Color GetClassColor(int labelId) =>
        (labelId >= 0 && labelId < ClassColors.Length) ? ClassColors[labelId] : Color.white;
}
