using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class WorkflowDetectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] ImageDetection _imageDetection;
    [SerializeField] Camera _arCamera;
    [SerializeField] ARRaycastManager _arRaycastManager;

    [Header("Detection Config")]
    [SerializeField, Range(0f, 1f)] float _minScore = 0.65f;
    [SerializeField] float _boxDepth = 1.5f;

    [Header("Bounding Boxes")]
    [SerializeField] bool _showBoundingBoxes = true;
    [SerializeField] GameObject _debugBoxPrefab;

    static readonly Color TargetColor = Color.green;
    static readonly Color OtherColor  = new(0.7f, 0.7f, 0.7f, 0.8f);
    static readonly List<ARRaycastHit> _raycastHits = new();

    string _targetClass;
    bool _isRunning;
    readonly List<GameObject> _activeBoxes = new();

    void Awake()
    {
        if (_arRaycastManager == null)
            _arRaycastManager = FindObjectOfType<ARRaycastManager>(true);

        if (_arCamera == null)
        {
            var arCamMgr = FindObjectOfType<ARCameraManager>(true);
            _arCamera = arCamMgr != null ? arCamMgr.GetComponent<Camera>() : Camera.main;
        }
    }

    void OnEnable()  => WorkflowEvents.OnStepChanged += OnStepChanged;
    void OnDisable()
    {
        WorkflowEvents.OnStepChanged -= OnStepChanged;
        StopDetection();
    }

    void OnDestroy() => StopDetection();

    void OnStepChanged(int index, WorkflowStep step) => StartDetection(step.TargetClass);

    void StartDetection(string targetClass)
    {
        if (_imageDetection == null)
        {
            Debug.LogError("[WorkflowDetection] ImageDetection reference not assigned.");
            return;
        }

        StopDetection();
        _targetClass = targetClass;
        _isRunning   = true;

        _imageDetection.OnDetectionsUpdated += OnDetectionsUpdated;
        _imageDetection.EnableDetection();

        Debug.Log($"[WorkflowDetection] Started for class: '{_targetClass}'");
    }

    void StopDetection()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_imageDetection != null)
        {
            _imageDetection.OnDetectionsUpdated -= OnDetectionsUpdated;
            _imageDetection.DisableDetection();
        }

        ClearBoxes();
        Debug.Log("[WorkflowDetection] Stopped.");
    }

    void OnDetectionsUpdated(IReadOnlyList<DetectionResult> results)
    {
        ClearBoxes();

        DetectionResult best      = default;
        float           bestScore = 0f;
        bool            found     = false;

        foreach (var det in results)
        {
            if (det.score < _minScore) continue;

            bool isTarget = string.Equals(det.className, _targetClass, StringComparison.OrdinalIgnoreCase);
            Color color   = isTarget ? TargetColor : OtherColor;

            if (_showBoundingBoxes && _debugBoxPrefab != null && _arCamera != null)
            {
                float x1 = det.normalizedRect.xMin * Screen.width;
                float x2 = det.normalizedRect.xMax * Screen.width;
                float y1 = det.normalizedRect.yMin * Screen.height;
                float y2 = det.normalizedRect.yMax * Screen.height;

                Vector3 wTL = _arCamera.ScreenToWorldPoint(new Vector3(x1, y2, _boxDepth));
                Vector3 wTR = _arCamera.ScreenToWorldPoint(new Vector3(x2, y2, _boxDepth));
                Vector3 wBR = _arCamera.ScreenToWorldPoint(new Vector3(x2, y1, _boxDepth));
                Vector3 wBL = _arCamera.ScreenToWorldPoint(new Vector3(x1, y1, _boxDepth));

                var go = Instantiate(_debugBoxPrefab);
                go.GetComponent<ARDebugBoundingBox>()?.Init(wTL, wTR, wBR, wBL, $"{det.className} {det.score:P0}", color);
                _activeBoxes.Add(go);
            }

            if (isTarget && det.score > bestScore)
            {
                best      = det;
                bestScore = det.score;
                found     = true;
            }
        }

        if (!found || _arCamera == null) return;

        float cx = (best.normalizedRect.x + best.normalizedRect.width  * 0.5f) * Screen.width;
        float cy = (best.normalizedRect.y + best.normalizedRect.height * 0.5f) * Screen.height;

        Vector3 worldPos;
        if (_arRaycastManager != null
            && _arRaycastManager.Raycast(new Vector2(cx, cy), _raycastHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            worldPos = _raycastHits[0].pose.position;
        else
            worldPos = _arCamera.ScreenPointToRay(new Vector3(cx, cy)).GetPoint(_boxDepth);

        WorkflowEvents.RaiseTargetPoseUpdated(new Pose(worldPos, Quaternion.identity));
    }

    void ClearBoxes()
    {
        foreach (var go in _activeBoxes)
            if (go != null) Destroy(go);
        _activeBoxes.Clear();
    }
}
