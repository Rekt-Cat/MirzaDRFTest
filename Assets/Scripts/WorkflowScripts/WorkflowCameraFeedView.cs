using UnityEngine;
using UnityEngine.UI;
using Logger = Workflow.Utils.Logger;

/// <summary>
/// Displays the RT-DETR detection frame as a live UGUI overlay on the phone screen.
/// UIToolkit's style.backgroundImage and IMGUIContainer both fail to update dynamically
/// on the Adreno GPU in Unity 2022.3. A UGUI RawImage reads the RenderTexture from the
/// GPU directly on every render frame, so no explicit repaint call is needed.
/// </summary>
public class WorkflowCameraFeedView : MonoBehaviour
{
    [SerializeField] ImageDetection _imageDetection;

    RenderTexture _feedRT;
    readonly Logger _logger = new(true, nameof(WorkflowCameraFeedView));

    void Awake()
    {
        _feedRT = new RenderTexture(640, 640, 0, RenderTextureFormat.ARGB32);
        _feedRT.Create();

        var canvasGO = new GameObject("CameraFeedCanvas");
        canvasGO.transform.SetParent(transform);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = 0; // phone screen (Display 1)
        canvas.sortingOrder  = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode       = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode   = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f; // anchor to height so vertical position is consistent

        canvasGO.AddComponent<GraphicRaycaster>();

        var rawImageGO = new GameObject("CameraFeed");
        rawImageGO.transform.SetParent(canvasGO.transform, false);

        var rawImage = rawImageGO.AddComponent<RawImage>();
        rawImage.texture = _feedRT;

        // Anchor bottom-centre, above the nav bar (~100 px) in reference-resolution units.
        var rt = rawImage.rectTransform;
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 100f);
        rt.sizeDelta        = new Vector2(240f, 240f); // square matches the 640×640 inference frame
    }

    void OnDestroy()
    {
        if (_feedRT != null) { _feedRT.Release(); Destroy(_feedRT); }
    }

    void OnEnable()
    {
        if (_imageDetection != null)
            _imageDetection.OnFrameCaptured += OnFrameCaptured;
        else
            _logger.LogError("ImageDetection reference not assigned");
    }

    void OnDisable()
    {
        if (_imageDetection != null)
            _imageDetection.OnFrameCaptured -= OnFrameCaptured;
    }

    void OnFrameCaptured(Texture2D frame)
    {
        if (frame == null || _feedRT == null) return;
        Graphics.Blit(frame, _feedRT);
        // RawImage samples _feedRT every render frame — no MarkDirtyRepaint needed.
    }
}
