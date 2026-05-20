using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Collections;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Logger = Workflow.Utils.Logger;

public class ImageDetection : MonoBehaviour
{
    [Header("AR — auto-discovered if left null")]
    [SerializeField] ARCameraManager arCameraManager;
    [SerializeField] Camera arCamera;
    [SerializeField] ARRaycastManager arRaycastManager;

    [Header("Model")]
    [SerializeField] ModelAsset modelAsset;
    [SerializeField, Range(0f, 1f)] float scoreThreshold = 0.65f;
    [SerializeField] int inferenceEveryNFrames = 3;

    [Header("Bounding Boxes")]
    [SerializeField] Canvas overlayCanvas;
    [SerializeField] GameObject boundingBoxPrefab;
    [SerializeField] int maxBoxes = 20;

    [Header("3D Markers (optional)")]
    [SerializeField] bool enable3DMarkers = true;
    [SerializeField] GameObject markerPrefab;
    [SerializeField] float markerDepth = 1.5f;

    [Header("Glasses Bounding Boxes (world-space, AR Glasses)")]
    [SerializeField] GameObject glassesBoxPrefab;
    [SerializeField] float glassesBoxDepth = 1.5f;

    [Header("Debug")]
    [SerializeField] bool saveNextFrame;

    // RT-DETR model I/O names
    const string InputName  = "pixel_values";
    const string BoxesName  = "sigmoid";
    const string LogitsName = "outputs";

    IWorker _worker;
    bool _detectionEnabled = true;
    bool _inferenceRunning;
    int _frameCount;
    Texture2D _lastFrame;
    readonly List<BoundingBoxOverlay> _boxPool = new();
    readonly List<GameObject> _markers3D = new();
    readonly List<GameObject> _glassesBoxes = new();
    static readonly List<ARRaycastHit> _raycastHits = new();
    readonly Logger _logger = new(true, nameof(ImageDetection));

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

    public event Action<Texture2D> OnFrameCaptured;
    public event Action<IReadOnlyList<DetectionResult>> OnDetectionsUpdated;

    void Awake()
    {
        if (arCameraManager == null)
        {
            // includeInactive=true — XR Origin (and its ARCameraManager) starts disabled in DRF mode.
            arCameraManager = FindObjectOfType<ARCameraManager>(true);
            if (arCameraManager == null)
                _logger.LogError("ARCameraManager not found. Assign it in the Inspector or add it to the scene.");
        }

        // Always derive the AR camera from ARCameraManager when available.
        // The inspector field may be set to the Spaces Host View (phone camera) which is wrong for DRF mode.
        if (arCameraManager != null)
            arCamera = arCameraManager.GetComponent<Camera>();
        else if (arCamera == null)
            arCamera = Camera.main;

        if (arRaycastManager == null)
        {
            // includeInactive=true — ARRaycastManager lives inside XR Origin which starts disabled in DRF mode.
            arRaycastManager = FindObjectOfType<ARRaycastManager>(true);
            if (arRaycastManager == null)
                _logger.Log("ARRaycastManager not found. 3D markers will use ray-depth fallback.");
        }

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

        Model model = ModelLoader.Load(modelAsset);
        foreach (var output in model.outputs)
            _logger.Log($"Model output: name={output.name}  index={output.index}");

        // GPUCompute uses AsyncGPUReadback which requires Adreno syncobj fences — broken on this device.
        // GPUCommandBuffer uses a different tensor data path that avoids ComputeTensorData.Download.
        _worker = WorkerFactory.CreateWorker(BackendType.GPUCommandBuffer, model);
        _logger.Log($"Worker ready. Camera={arCamera?.name ?? "null"}  CameraManager={arCameraManager?.name ?? "null"}  Raycast={arRaycastManager?.name ?? "null"}");

        for (int i = 0; i < maxBoxes; i++)
        {
            if (boundingBoxPrefab == null) break;
            var go = Instantiate(boundingBoxPrefab, overlayCanvas.transform, false);
            var box = go.GetComponent<BoundingBoxOverlay>();
            if (box != null) _boxPool.Add(box);
            go.SetActive(false);
        }
    }

    void OnDestroy()
    {
        _worker?.Dispose();
        ClearBoxes();
        ClearMarkers();
        ClearGlassesBoxes();
        if (_lastFrame != null) Destroy(_lastFrame);
    }

    public void EnableDetection()
    {
        _detectionEnabled = true;
    }

    public void DisableDetection()
    {
        _detectionEnabled = false;
        ClearBoxes();
        ClearMarkers();
        ClearGlassesBoxes();
    }

    // Poll the camera directly in Update instead of relying on frameReceived events.
    // On Snapdragon Spaces / Adreno, the ~250 ms GPU stall in ReadbackAndClone disrupts
    // the XR frame event pipeline, causing frameReceived to permanently stop firing after
    // the first inference. TryAcquireLatestCpuImage is unaffected by this.
    void Update()
    {
        if (!_detectionEnabled || _inferenceRunning) return;
        if (++_frameCount % inferenceEveryNFrames != 0) return;
        if (arCameraManager == null) return;
        if (arCameraManager.subsystem == null || !arCameraManager.subsystem.running) return;
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image)) return;
        if (!image.valid) { image.Dispose(); return; }

        StartCoroutine(InferenceCoroutine(image));
    }

    // Adreno devices fail AsyncGPUReadback fences when Execute and ReadbackAndClone run in
    // the same frame. Yielding one frame lets the GPU flush before we read the output tensors.
    IEnumerator InferenceCoroutine(XRCpuImage image)
    {
        _inferenceRunning = true;

        Texture2D tex;
        using (image)
            tex = ConvertToTexture2D(image);

        _logger.Log($"Inference start — image {tex.width}x{tex.height}");

        if (saveNextFrame)
        {
            saveNextFrame = false;
            string path = Path.Combine(Application.persistentDataPath, "debug_frame.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            _logger.Log($"Debug frame saved to: {path}");
        }

        var oldFrame = _lastFrame;
        _lastFrame = tex;

        var input = TextureConverter.ToTensor(tex, new TextureTransform().SetDimensions(640, 640, 3));
        _worker.Execute(new Dictionary<string, Tensor> { { InputName, input } });
        input.Dispose();

        // yield is allowed inside try-finally (no catch clause).
        // Wrapping ensures _inferenceRunning is always cleared even if a handler throws.
        try
        {
            yield return null; // wait one frame for GPU compute to finish

            var results = ParseRTDetr();
            _logger.Log($"Inference done — {results.Count} detection(s): [{string.Join(", ", results.ConvertAll(r => $"{r.className} {r.score:P0}"))}]");

            UpdateBoundingBoxes(results);
            if (enable3DMarkers) UpdateMarkers(results);
            UpdateGlassesBoxes(results);
            OnFrameCaptured?.Invoke(_lastFrame);
            OnDetectionsUpdated?.Invoke(results);
        }
        finally
        {
            if (oldFrame != null) Destroy(oldFrame);
            _inferenceRunning = false;
        }
    }

    Texture2D ConvertToTexture2D(XRCpuImage image)
    {
        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect        = new RectInt(0, 0, image.width, image.height),
            outputDimensions = new Vector2Int(image.width, image.height),
            outputFormat     = TextureFormat.RGBA32,
            transformation   = XRCpuImage.Transformation.MirrorX
        };

        int size = image.GetConvertedDataSize(conversionParams);
        var buffer = new NativeArray<byte>(size, Allocator.Temp);
        image.Convert(conversionParams, buffer);

        var tex = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(buffer);
        tex.Apply();
        buffer.Dispose();

        if (tex.width != 640 || tex.height != 640)
        {
            Texture2D resized = ResizeTexture(tex, 640, 640);
            Destroy(tex);
            return resized;
        }

        return tex;
    }

    static Texture2D ResizeTexture(Texture2D source, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        var resized = new Texture2D(w, h, TextureFormat.RGBA32, false);
        resized.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        resized.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return resized;
    }

    // RT-DETR format: "sigmoid" [1,300,4] boxes already in [0,1], "outputs" [1,300,9] raw class logits
    List<DetectionResult> ParseRTDetr()
    {
        if (_worker.PeekOutput(BoxesName)  is not TensorFloat rawBoxes ||
            _worker.PeekOutput(LogitsName) is not TensorFloat rawLogits)
        {
            _logger.LogError($"RT-DETR outputs not found — expected '{BoxesName}' and '{LogitsName}'.");
            return new List<DetectionResult>();
        }

        using TensorFloat cpuBoxes  = rawBoxes.ReadbackAndClone();
        using TensorFloat cpuLogits = rawLogits.ReadbackAndClone();

        int numDetections = cpuBoxes.shape[1];   // 300
        int numClasses    = cpuLogits.shape[2];  // 9
        var results       = new List<DetectionResult>();

        for (int i = 0; i < numDetections; i++)
        {
            // Per-class sigmoid — mirrors Android team's logic
            float bestScore = 0f;
            int   bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float s = 1f / (1f + Mathf.Exp(-cpuLogits[0, i, c]));
                if (s > bestScore) { bestScore = s; bestClass = c; }
            }

            if (bestScore < scoreThreshold) continue;
            if (bestClass <= 0 || bestClass >= Labels.Length) continue; // skip Nothing (0)

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

        return NMS(results);
    }

    static List<DetectionResult> NMS(List<DetectionResult> dets, float iouThreshold = 0.45f)
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

    void UpdateBoundingBoxes(List<DetectionResult> results)
    {
        ClearBoxes();
        int count = Mathf.Min(results.Count, _boxPool.Count);
        for (int i = 0; i < count; i++)
        {
            _boxPool[i].gameObject.SetActive(true);
            _boxPool[i].SetBox(results[i], GetClassColor(results[i].labelId));
        }
    }

    void UpdateMarkers(List<DetectionResult> results)
    {
        ClearMarkers();
        foreach (var result in results)
        {
            float cx = result.normalizedRect.x + result.normalizedRect.width  * 0.5f;
            float cy = result.normalizedRect.y + result.normalizedRect.height * 0.5f;
            var screenPt = new Vector2(cx * Screen.width, cy * Screen.height);

            Vector3 worldPos;
            if (arRaycastManager != null && arRaycastManager.Raycast(screenPt, _raycastHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
                worldPos = _raycastHits[0].pose.position;
            else
                worldPos = arCamera.ScreenPointToRay(screenPt).GetPoint(markerDepth);

            if (markerPrefab == null) continue;

            GameObject go = Instantiate(markerPrefab, worldPos, Quaternion.identity);
            var tmp = go.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) tmp.text = result.className;
            _markers3D.Add(go);
        }
    }

    void ClearBoxes()
    {
        foreach (var box in _boxPool)
            box.gameObject.SetActive(false);
    }

    void ClearMarkers()
    {
        foreach (var go in _markers3D)
            if (go != null) Destroy(go);
        _markers3D.Clear();
        _raycastHits.Clear();
    }

    void UpdateGlassesBoxes(List<DetectionResult> results)
    {
        ClearGlassesBoxes();
        if (glassesBoxPrefab == null || arCamera == null) return;

        foreach (var det in results)
        {
            float x1 = det.normalizedRect.xMin * Screen.width;
            float x2 = det.normalizedRect.xMax * Screen.width;
            float y1 = det.normalizedRect.yMin * Screen.height;
            float y2 = det.normalizedRect.yMax * Screen.height;

            Vector3 tl = arCamera.ScreenPointToRay(new Vector3(x1, y2)).GetPoint(glassesBoxDepth);
            Vector3 tr = arCamera.ScreenPointToRay(new Vector3(x2, y2)).GetPoint(glassesBoxDepth);
            Vector3 br = arCamera.ScreenPointToRay(new Vector3(x2, y1)).GetPoint(glassesBoxDepth);
            Vector3 bl = arCamera.ScreenPointToRay(new Vector3(x1, y1)).GetPoint(glassesBoxDepth);

            var go = Instantiate(glassesBoxPrefab);
            go.GetComponent<ARDebugBoundingBox>()?.Init(tl, tr, br, bl,
                $"{det.className}  {det.score:P0}", GetClassColor(det.labelId));
            _glassesBoxes.Add(go);
        }
    }

    void ClearGlassesBoxes()
    {
        foreach (var go in _glassesBoxes)
            if (go != null) Destroy(go);
        _glassesBoxes.Clear();
    }

    Color GetClassColor(int labelId)
    {
        return (labelId >= 0 && labelId < ClassColors.Length) ? ClassColors[labelId] : Color.white;
    }
}
