using UnityEngine;
using Logger = Workflow.Utils.Logger;

public class WorkflowObjectPlacer : MonoBehaviour
{
    [Header("Asset Catalog (optional)")]
    [SerializeField] WorkflowAssetCatalog _catalog;

    [Header("Prefab Key")]
    [SerializeField] string _prefabKey = "Cover";

    [Header("Fallback — used when catalog has no match")]
    [SerializeField] Color _fallbackColor = Color.green;
    [SerializeField] float _fallbackScale = 0.1f;

    [Header("Smoothing")]
    [SerializeField, Range(0f, 1f)] float _smoothingAlpha = 0.2f;

    GameObject _currentObject;
    Vector3    _smoothedPosition;
    bool       _hasAnchor;

    private readonly Logger _logger = new(true, nameof(WorkflowObjectPlacer));

    void OnEnable()
    {
        WorkflowEvents.OnTargetPoseUpdated += OnTargetPoseUpdated;
    }

    void OnDisable()
    {
        WorkflowEvents.OnTargetPoseUpdated -= OnTargetPoseUpdated;
        DestroyCurrentObject();
    }

    void OnTargetPoseUpdated(Pose pose)
    {
        if (!_hasAnchor)
        {
            _smoothedPosition = pose.position;
            _hasAnchor        = true;
            SpawnObject();
        }
        else
        {
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, pose.position, _smoothingAlpha);
        }

        if (_currentObject != null)
            _currentObject.transform.position = _smoothedPosition;
    }

    void SpawnObject()
    {
        var prefab = _catalog != null ? _catalog.GetPrefab(_prefabKey) : null;
        _currentObject = prefab != null
            ? Instantiate(prefab, _smoothedPosition, Quaternion.identity)
            : CreateFallbackSphere();

        _logger.Log($"Spawned '{(_currentObject != null ? _currentObject.name : "NULL")}' at {_smoothedPosition}");
    }

    GameObject CreateFallbackSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position   = _smoothedPosition;
        go.transform.localScale = Vector3.one * _fallbackScale;
        Destroy(go.GetComponent<Collider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = _fallbackColor;
        go.GetComponent<Renderer>().material = mat;
        return go;
    }

    void DestroyCurrentObject()
    {
        if (_currentObject != null)
        {
            Destroy(_currentObject);
            _currentObject = null;
        }
    }
}
