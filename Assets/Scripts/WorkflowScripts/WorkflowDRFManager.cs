#if USING_SNAPDRAGON_SPACES_SDK
using UnityEngine;
using UnityEngine.XR.ARFoundation;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Qualcomm.Snapdragon.Spaces;

/// <summary>
/// Manages DRF (Dual Render Fusion) lifecycle for WorkflowScene.
///
/// Phone screen  (Display 1) → WorkflowUI UIDocument — always active.
/// Glasses screen (Display 2) → ARGlassesUI World-Space Canvas — enabled when OpenXR starts.
///
/// Required scene objects (already present):
///   Dynamic OpenXR Loader, Spaces Host View, Spaces Lifecycle Events,
///   XR Origin (start disabled), AR Session (start disabled),
///   XR Camera (targetDisplay 1), ARGlassesUI (start disabled).
/// </summary>
public class WorkflowDRFManager : MonoBehaviour
{
    [Tooltip("ARGlassesUI root — the World-Space Canvas targeting the XR Camera. " +
             "Start it disabled; this script enables it when OpenXR starts.")]
    [SerializeField] private GameObject _glassesContentRoot;

    [Tooltip("XR Origin GameObject. On device, DynamicOpenXRLoader manages this via " +
             "AutoManageXRCamera. In the Editor, simulation shortcuts drive it here.")]
    [SerializeField] private GameObject _xrOrigin;

    [Tooltip("ARCameraManager inside XR Origin. Explicitly disabled until OpenXR starts to " +
             "prevent XRCameraSubsystem:TryGetLatestFrame error spam during the window between " +
             "DynamicOpenXRLoader enabling XR Origin and the camera subsystem being ready.")]
    [SerializeField] private ARCameraManager _arCameraManager;

    private SpacesLifecycleEvents _lifecycleEvents;

    private void Awake()
    {
        // Auto-find if not wired in Inspector (scene has ARCameraManager disabled by default).
        if (_arCameraManager == null)
            _arCameraManager = FindObjectOfType<ARCameraManager>(true);
    }

    private void Start()
    {
        _lifecycleEvents = FindFirstObjectByType<SpacesLifecycleEvents>();

        if (_lifecycleEvents == null)
        {
            Debug.LogWarning("[WorkflowDRFManager] SpacesLifecycleEvents not found in scene.");
            return;
        }

        _lifecycleEvents.OnOpenXRStarted.AddListener(OnOpenXRStarted);
        _lifecycleEvents.OnOpenXRStopped.AddListener(OnOpenXRStopped);

        SetGlassesContentActive(false);

        // Glasses can connect before Start() runs on wireless Mirza — catch up.
        if (DynamicOpenXRLoader.Instance != null && DynamicOpenXRLoader.Instance.AreSubsystemsRunning)
        {
            Debug.Log("[WorkflowDRFManager] OpenXR already running on Start — catching up.");
            OnOpenXRStarted();
        }
    }

    private void OnDestroy()
    {
        if (_lifecycleEvents == null) return;
        _lifecycleEvents.OnOpenXRStarted.RemoveListener(OnOpenXRStarted);
        _lifecycleEvents.OnOpenXRStopped.RemoveListener(OnOpenXRStopped);
    }

    private void OnOpenXRStarted()
    {
        Debug.Log("[WorkflowDRFManager] OpenXR started — showing ARGlassesUI on glasses.");
        SetGlassesContentActive(true);
    }

    private void OnOpenXRStopped()
    {
        Debug.Log("[WorkflowDRFManager] OpenXR stopped — hiding ARGlassesUI.");
        SetGlassesContentActive(false);
    }

    private void SetGlassesContentActive(bool active)
    {
        if (_glassesContentRoot != null)
            _glassesContentRoot.SetActive(active);

        // Keep ARCameraManager disabled until the subsystem is confirmed running.
        // DynamicOpenXRLoader enables XR Origin (and thus ARCameraManager) the moment
        // glasses connect, but the native camera provider isn't ready for frames yet —
        // this causes a flood of TryGetLatestFrame errors until we explicitly gate it here.
        if (_arCameraManager != null)
            _arCameraManager.enabled = active;

#if UNITY_EDITOR
        // DynamicOpenXRLoader handles XR Origin on device via AutoManageXRCamera.
        // Mirror that here so the XR Camera renders to Display 2 during Editor simulation.
        if (_xrOrigin != null)
            _xrOrigin.SetActive(active);
#endif
    }

#if UNITY_EDITOR
    [MenuItem("Window/XR/Snapdragon Spaces/Workflow DRF/Simulate DRF Start #&3")]
    public static void SimulateDRFStart()
    {
        if (!Application.isPlaying) return;
        var mgr = FindFirstObjectByType<WorkflowDRFManager>();
        if (mgr != null) mgr.OnOpenXRStarted();
        else Debug.LogWarning("[WorkflowDRFManager] Not found in scene.");
    }

    [MenuItem("Window/XR/Snapdragon Spaces/Workflow DRF/Simulate DRF Stop #&4")]
    public static void SimulateDRFStop()
    {
        if (!Application.isPlaying) return;
        var mgr = FindFirstObjectByType<WorkflowDRFManager>();
        if (mgr != null) mgr.OnOpenXRStopped();
        else Debug.LogWarning("[WorkflowDRFManager] Not found in scene.");
    }
#endif
}
#endif
