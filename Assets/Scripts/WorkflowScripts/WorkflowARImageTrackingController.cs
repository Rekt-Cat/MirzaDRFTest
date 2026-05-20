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

    [Header("Tracking Mode")]
    [SerializeField] SpacesImageTrackingMode _trackingMode = SpacesImageTrackingMode.DYNAMIC;

    string _targetImageName;
    bool _trackingModeApplied;
    SpacesLifecycleEvents _lifecycleEvents;
    ARCameraManager _arCameraManager;
    int _frameCount;
    private readonly Logger _logger = new(true, nameof(WorkflowARImageTrackingController));

    void OnEnable()
    {
        _logger.Log($"BUG: OnEnable — _trackedImageManager={(_trackedImageManager == null ? "NULL" : "assigned")} _imageConfigurator={(_imageConfigurator == null ? "NULL" : "assigned")}");

        WorkflowEvents.OnStepChanged += OnStepChanged;

        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
            _logger.Log("BUG: Subscribed to trackedImagesChanged");
        }
        else
        {
            _logger.Log("BUG: _trackedImageManager is NULL — image tracking will not work!");
        }

        _lifecycleEvents = FindObjectOfType<SpacesLifecycleEvents>();
        if (_lifecycleEvents != null)
        {
            _lifecycleEvents.OnOpenXRStarted.AddListener(OnOpenXRStarted);
            _logger.Log("BUG: Subscribed to SpacesLifecycleEvents.OnOpenXRStarted");
        }
        else
        {
            _logger.Log("BUG: SpacesLifecycleEvents not found — tracking mode cannot be deferred to OpenXR start");
        }

        _arCameraManager = FindObjectOfType<ARCameraManager>(true);
        _logger.Log($"BUG: ARCameraManager found={_arCameraManager != null}");
    }

    void OnDisable()
    {
        _logger.Log("BUG: OnDisable");
        WorkflowEvents.OnStepChanged -= OnStepChanged;

        if (_trackedImageManager != null)
            _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;

        if (_lifecycleEvents != null)
            _lifecycleEvents.OnOpenXRStarted.RemoveListener(OnOpenXRStarted);
    }

    void OnOpenXRStarted()
    {
        _logger.Log($"BUG: OnOpenXRStarted — applying deferred tracking mode for '{_targetImageName}'");
        _logger.Log($"BUG: ARTrackedImageManager subsystem running={_trackedImageManager?.subsystem?.running ?? false} enabled={_trackedImageManager?.enabled ?? false}");
        ApplyTrackingMode();
    }

    void OnStepChanged(int index, WorkflowStep step)
    {
        _targetImageName    = step.TargetClass;
        _trackingModeApplied = false;
        _logger.Log($"BUG: OnStepChanged — index={index} targetImageName='{_targetImageName}'");
        ApplyTrackingMode();
    }

    void ApplyTrackingMode()
    {
        if (_imageConfigurator == null || string.IsNullOrEmpty(_targetImageName)) return;

        bool hasImage = _imageConfigurator.HasReferenceImageTrackingMode(_targetImageName);
        _logger.Log($"BUG: ApplyTrackingMode — HasReferenceImageTrackingMode('{_targetImageName}') = {hasImage}");

        if (!hasImage)
        {
            _logger.Log($"BUG: Image '{_targetImageName}' NOT in reference library — check name matches TargetClass exactly");
            return;
        }

        try
        {
            _imageConfigurator.SetTrackingModeForReferenceImage(_targetImageName, _trackingMode);
            _trackingModeApplied = true;
            _logger.Log($"BUG: ApplyTrackingMode SUCCESS — mode={_trackingMode} image='{_targetImageName}'");
        }
        catch (Exception e)
        {
            _logger.Log($"BUG: ApplyTrackingMode FAILED (will retry on OpenXR start) — {e.Message}");
        }
    }

    void Update()
    {
        if (_arCameraManager == null || !_arCameraManager.enabled) return;
        if (++_frameCount % 90 != 0) return; // log every ~3 seconds at 30fps

        bool gotFrame = _arCameraManager.TryAcquireLatestCpuImage(out var image);
        if (gotFrame) image.Dispose();
        _logger.Log($"BUG: [FrameCheck] TryAcquireLatestCpuImage={gotFrame} cameraManager.enabled={_arCameraManager.enabled} subsystem.running={_arCameraManager.subsystem?.running ?? false}");
    }

    void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        _logger.Log($"BUG: OnTrackedImagesChanged — added={args.added.Count} updated={args.updated.Count} removed={args.removed.Count} target='{_targetImageName}'");

        foreach (var image in args.added)
        {
            _logger.Log($"BUG: added image='{image.referenceImage.name}' state={image.trackingState}");
            TryRaisePose(image);
        }

        foreach (var image in args.updated)
        {
            _logger.Log($"BUG: updated image='{image.referenceImage.name}' state={image.trackingState}");
            TryRaisePose(image);
        }
    }

    void TryRaisePose(ARTrackedImage image)
    {
        if (image.trackingState != TrackingState.Tracking)
        {
            _logger.Log($"BUG: TryRaisePose SKIP — '{image.referenceImage.name}' state={image.trackingState}");
            return;
        }

        bool nameMatch = string.Equals(image.referenceImage.name, _targetImageName, StringComparison.OrdinalIgnoreCase);
        _logger.Log($"BUG: TryRaisePose — image='{image.referenceImage.name}' target='{_targetImageName}' nameMatch={nameMatch}");

        if (!nameMatch) return;

        var pose = new Pose(image.transform.position, image.transform.rotation);
        _logger.Log($"BUG: Raising OnTargetPoseUpdated — pos={pose.position}");
        WorkflowEvents.RaiseTargetPoseUpdated(pose);
    }
}
#endif
