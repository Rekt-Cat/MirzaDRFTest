#if USING_SNAPDRAGON_SPACES_SDK
using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Qualcomm.Snapdragon.Spaces;
using Logger = Workflow.Utils.Logger;

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
    private readonly Logger _logger = new(true, nameof(WorkflowARImageTrackingController));

    void OnEnable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;

        _lifecycleEvents = FindObjectOfType<SpacesLifecycleEvents>();
        if (_lifecycleEvents != null)
            _lifecycleEvents.OnOpenXRStarted.AddListener(OnOpenXRStarted);

        ApplyTrackingMode();
    }

    void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;

        if (_lifecycleEvents != null)
            _lifecycleEvents.OnOpenXRStarted.RemoveListener(OnOpenXRStarted);
    }

    void OnOpenXRStarted() => ApplyTrackingMode();

    void ApplyTrackingMode()
    {
        if (_imageConfigurator == null || string.IsNullOrEmpty(_targetImageName)) return;
        if (!_imageConfigurator.HasReferenceImageTrackingMode(_targetImageName)) return;

        try
        {
            _imageConfigurator.SetTrackingModeForReferenceImage(_targetImageName, _trackingMode);
            _logger.Log($"Tracking mode set — image='{_targetImageName}' mode={_trackingMode}");
        }
        catch (Exception e)
        {
            _logger.Log($"ApplyTrackingMode FAILED (will retry on OpenXR start) — {e.Message}");
        }
    }

    void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        foreach (var image in args.added)   TryRaisePose(image);
        foreach (var image in args.updated) TryRaisePose(image);
    }

    void TryRaisePose(ARTrackedImage image)
    {
        if (image.trackingState != TrackingState.Tracking) return;
        if (!string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase)) return;

        WorkflowEvents.RaiseTargetPoseUpdated(new Pose(image.transform.position, image.transform.rotation));
    }
}
#endif
