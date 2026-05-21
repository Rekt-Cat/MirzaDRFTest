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

    SpacesLifecycleEvents _lifecycleEvents;
    bool _isDetected;
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
        _logger.Log("OnOpenXRStarted — status=Detecting");
        _isDetected = false;
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
                WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
            }
        }
    }

    void CheckImage(ARTrackedImage image)
    {
        bool isTarget = string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase);
        _logger.Log($"CheckImage — name='{image.referenceImage.name}' isTarget={isTarget} state={image.trackingState}");

        if (!isTarget) return;

        if (image.trackingState == TrackingState.Tracking && !_isDetected)
        {
            _isDetected = true;
            _logger.Log($"status=Detected pos={image.transform.position}");
            WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detected);
        }
        else if (image.trackingState != TrackingState.Tracking && _isDetected)
        {
            _isDetected = false;
            _logger.Log($"Image lost — status=Detecting");
            WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
        }
    }
}
#endif
