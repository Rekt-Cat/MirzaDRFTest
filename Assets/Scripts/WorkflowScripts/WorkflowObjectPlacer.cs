using UnityEngine;
using Logger = Workflow.Utils.Logger;

public class WorkflowObjectPlacer : MonoBehaviour
{
    [Header("Asset Catalog (optional)")]
    [SerializeField] WorkflowAssetCatalog _catalog;

    [Header("Fallback — used when catalog has no match")]
    [SerializeField] Color  _fallbackColor = Color.green;
    [SerializeField] float  _fallbackScale = 0.1f;

    [Header("Smoothing")]
    [SerializeField, Range(0f, 1f)] float _smoothingAlpha = 0.2f;

    GameObject _currentObject;
    Vector3    _smoothedPosition;
    bool       _hasAnchor;
    string     _currentPrefabKey;

    private readonly Logger _logger = new(true, nameof(WorkflowObjectPlacer));

    void OnEnable()
    {
        _logger.Log($"BUG: OnEnable — catalog={(_catalog == null ? "NULL" : "assigned")}");
        WorkflowEvents.OnStepChanged       += OnStepChanged;
        WorkflowEvents.OnTargetPoseUpdated += OnTargetPoseUpdated;
    }

    void OnDisable()
    {
        _logger.Log("BUG: OnDisable");
        WorkflowEvents.OnStepChanged       -= OnStepChanged;
        WorkflowEvents.OnTargetPoseUpdated -= OnTargetPoseUpdated;
        DestroyCurrentObject();
    }

    void OnStepChanged(int index, WorkflowStep step)
    {
        _currentPrefabKey = step.PrefabKey;
        _hasAnchor        = false;
        _logger.Log($"BUG: OnStepChanged — index={index} prefabKey='{_currentPrefabKey}' targetClass='{step.TargetClass}'");
        DestroyCurrentObject();
    }

    void OnTargetPoseUpdated(Pose pose)
    {
        if (!_hasAnchor)
        {
            _smoothedPosition = pose.position;
            _hasAnchor        = true;
            _logger.Log($"BUG: OnTargetPoseUpdated FIRST HIT — pos={pose.position} spawning object");
            SpawnObject();
        }
        else
        {
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, pose.position, _smoothingAlpha);
            _logger.Log($"BUG: OnTargetPoseUpdated UPDATE — smoothedPos={_smoothedPosition}");
        }

        if (_currentObject != null)
            _currentObject.transform.position = _smoothedPosition;
        else
            _logger.Log("BUG: OnTargetPoseUpdated — _currentObject is NULL (not spawned yet or destroyed)");
    }

    void SpawnObject()
    {
        _logger.Log($"BUG: SpawnObject — prefabKey='{_currentPrefabKey}' catalog={(_catalog == null ? "NULL" : "assigned")}");

        var prefab = _catalog != null ? _catalog.GetPrefab(_currentPrefabKey) : null;
        _logger.Log($"BUG: SpawnObject — prefab from catalog={(prefab == null ? "NULL — using fallback sphere" : prefab.name)}");

        _currentObject = prefab != null
            ? Instantiate(prefab, _smoothedPosition, Quaternion.identity)
            : CreateFallbackSphere();

        _logger.Log($"BUG: SpawnObject — spawned '{(_currentObject != null ? _currentObject.name : "NULL")}' at {_smoothedPosition}");
    }

    GameObject CreateFallbackSphere()
    {
        _logger.Log("BUG: CreateFallbackSphere — creating primitive sphere");
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
            _logger.Log($"BUG: DestroyCurrentObject — destroying '{_currentObject.name}'");
            Destroy(_currentObject);
            _currentObject = null;
        }
    }
}
