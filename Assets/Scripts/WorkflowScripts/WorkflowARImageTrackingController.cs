using System;
using UnityEngine;
#if USING_SNAPDRAGON_SPACES_SDK
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Qualcomm.Snapdragon.Spaces;
using Logger = Workflow.Utils.Logger;
#endif

public enum WorkflowTrackingStatus { Starting, Detecting, Detected }

public static class WorkflowTrackingEvents
{
    public static event Action<WorkflowTrackingStatus> OnStatusChanged;
    internal static void Raise(WorkflowTrackingStatus s) => OnStatusChanged?.Invoke(s);
}

#if USING_SNAPDRAGON_SPACES_SDK
public class WorkflowARImageTrackingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] ARTrackedImageManager _trackedImageManager;
    [SerializeField] SpacesReferenceImageConfigurator _imageConfigurator;

    [Header("Target")]
    [SerializeField] string _targetImageName = "Cover";

    [Header("Tracking Mode")]
    [SerializeField] SpacesImageTrackingMode _trackingMode = SpacesImageTrackingMode.DYNAMIC;

    [Header("Bounding Box")]
    [SerializeField] Color _boxColor = Color.green;
    [SerializeField] float _lineWidth = 0.005f;

    SpacesLifecycleEvents _lifecycleEvents;
    bool _isDetected;
    LineRenderer _boxLine;
    private readonly Logger _logger = new(true, nameof(WorkflowARImageTrackingController));

    void Awake()
    {
        // Register lifecycle listeners in Awake so they survive OnEnable/OnDisable cycles.
        // SpacesLifecycleEvents.OnSceneLoaded fires OnOpenXRStopped during scene load which
        // triggers OnDisable — if listeners were registered in OnEnable they'd be removed
        // before OpenXR actually starts, causing the status to stay on Starting forever.
        _lifecycleEvents = FindObjectOfType<SpacesLifecycleEvents>();
        _logger.Log($"[Awake] SpacesLifecycleEvents found={_lifecycleEvents != null}");

        if (_lifecycleEvents != null)
        {
            _lifecycleEvents.OnOpenXRStarted.AddListener(OnOpenXRStarted);
            _lifecycleEvents.OnOpenXRStopped.AddListener(OnOpenXRStopped);
        }

        CreateBoundingBox();
    }

    void OnDestroy()
    {
        if (_lifecycleEvents != null)
        {
            _lifecycleEvents.OnOpenXRStarted.RemoveListener(OnOpenXRStarted);
            _lifecycleEvents.OnOpenXRStopped.RemoveListener(OnOpenXRStopped);
        }
    }

    void OnEnable()
    {
        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Starting);

        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;

        if (DynamicOpenXRLoader.Instance != null && DynamicOpenXRLoader.Instance.AreSubsystemsRunning)
        {
            _logger.Log("OpenXR already running on OnEnable — catching up to Detecting");
            OnOpenXRStarted();
        }
    }

    void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    void OnOpenXRStarted()
    {
        bool subsystemRunning = _trackedImageManager != null && _trackedImageManager.subsystem != null && _trackedImageManager.subsystem.running;
        _logger.Log($"OnOpenXRStarted — subsystem.running={subsystemRunning}");
        _isDetected = false;

        // Re-subscribe here because OnDisable (fired by SpacesLifecycleEvents.OnSceneLoaded
        // during scene load) removes the subscription before OnOpenXRStarted runs.
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
            _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
            _logger.Log("OnOpenXRStarted — re-subscribed to trackedImagesChanged");
        }

        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
        ApplyTrackingMode();
    }

    void OnOpenXRStopped()
    {
        _logger.Log("OnOpenXRStopped — status=Starting");
        _isDetected = false;
        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Starting);
    }

    void ApplyTrackingMode()
    {
        if (_imageConfigurator == null)
        {
            _logger.Log("ApplyTrackingMode — imageConfigurator is NULL, skipping");
            return;
        }
        if (string.IsNullOrEmpty(_targetImageName))
        {
            _logger.Log("ApplyTrackingMode — targetImageName is empty, skipping");
            return;
        }

        bool hasImage = _imageConfigurator.HasReferenceImageTrackingMode(_targetImageName);
        _logger.Log($"ApplyTrackingMode — HasReferenceImageTrackingMode('{_targetImageName}')={hasImage}");
        if (!hasImage) return;

        try
        {
            _imageConfigurator.SetTrackingModeForReferenceImage(_targetImageName, _trackingMode);
            _logger.Log($"ApplyTrackingMode — SUCCESS mode={_trackingMode}");
        }
        catch (Exception e)
        {
            _logger.Log($"ApplyTrackingMode FAILED (will retry on OpenXR start) — {e.Message}");
        }
    }

    void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        foreach (var image in args.added)   CheckImage(image);
        foreach (var image in args.updated) CheckImage(image);

        foreach (var image in args.removed)
        {
            if (string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase))
            {
                _isDetected = false;
                _logger.Log("Target image removed — status=Detecting");
                WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
            }
        }
    }

    void CheckImage(ARTrackedImage image)
    {
        if (!string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase)) return;

        if (image.trackingState == TrackingState.Tracking)
        {
            UpdateBoundingBox(image);
            if (!_isDetected)
            {
                _isDetected = true;
                _logger.Log($"Detected '{_targetImageName}' at {image.transform.position}");
                WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detected);
            }
        }
        else if (_isDetected)
        {
            _isDetected = false;
            HideBoundingBox();
            _logger.Log($"Lost '{_targetImageName}' — status=Detecting");
            WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
        }
    }

    void CreateBoundingBox()
    {
        var go = new GameObject("ImageTrackingBoundingBox");
        go.transform.SetParent(transform, false);

        _boxLine = go.AddComponent<LineRenderer>();
        _boxLine.positionCount = 5;
        _boxLine.loop = false;
        _boxLine.useWorldSpace = true;
        _boxLine.startWidth = _lineWidth;
        _boxLine.endWidth   = _lineWidth;
        _boxLine.startColor = _boxColor;
        _boxLine.endColor   = _boxColor;
        _boxLine.material   = new Material(Shader.Find("Sprites/Default"));
        _boxLine.enabled    = false;
    }

    void UpdateBoundingBox(ARTrackedImage image)
    {
        if (_boxLine == null) return;

        Vector2 size = image.size * 0.5f;
        Transform t  = image.transform;

        // Four corners in image local space, then transform to world space.
        Vector3 tl = t.TransformPoint(new Vector3(-size.x,  0f,  size.y));
        Vector3 tr = t.TransformPoint(new Vector3( size.x,  0f,  size.y));
        Vector3 br = t.TransformPoint(new Vector3( size.x,  0f, -size.y));
        Vector3 bl = t.TransformPoint(new Vector3(-size.x,  0f, -size.y));

        _boxLine.SetPositions(new[] { tl, tr, br, bl, tl });
        _boxLine.enabled = true;
    }

    void HideBoundingBox()
    {
        if (_boxLine != null)
            _boxLine.enabled = false;
    }
}
#endif
