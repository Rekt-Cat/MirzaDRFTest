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

    void OnEnable()
    {
        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Starting);

        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;

        _lifecycleEvents = FindObjectOfType<SpacesLifecycleEvents>();
        if (_lifecycleEvents != null)
        {
            _lifecycleEvents.OnOpenXRStarted.AddListener(OnOpenXRStarted);
            _lifecycleEvents.OnOpenXRStopped.AddListener(OnOpenXRStopped);
        }

        ApplyTrackingMode();
    }

    void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;

        if (_lifecycleEvents != null)
        {
            _lifecycleEvents.OnOpenXRStarted.RemoveListener(OnOpenXRStarted);
            _lifecycleEvents.OnOpenXRStopped.RemoveListener(OnOpenXRStopped);
        }
    }

    void OnOpenXRStarted()
    {
        _isDetected = false;
        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
        ApplyTrackingMode();
    }

    void OnOpenXRStopped()
    {
        _isDetected = false;
        WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Starting);
    }

    void ApplyTrackingMode()
    {
        if (_imageConfigurator == null || string.IsNullOrEmpty(_targetImageName)) return;
        if (!_imageConfigurator.HasReferenceImageTrackingMode(_targetImageName)) return;

        try
        {
            _imageConfigurator.SetTrackingModeForReferenceImage(_targetImageName, _trackingMode);
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
        if (!string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase)) return;

        if (image.trackingState == TrackingState.Tracking && !_isDetected)
        {
            _isDetected = true;
            WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detected);
            _logger.Log($"Detected '{_targetImageName}' at {image.transform.position}");
        }
        else if (image.trackingState != TrackingState.Tracking && _isDetected)
        {
            _isDetected = false;
            WorkflowTrackingEvents.Raise(WorkflowTrackingStatus.Detecting);
        }
    }
}
#endif
