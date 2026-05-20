using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WorkflowAssetCatalog", menuName = "Workflow/Asset Catalog")]
public class WorkflowAssetCatalog : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string Key;
        public GameObject Prefab;
    }

    [SerializeField] Entry[] _entries;

    public GameObject GetPrefab(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var entry in _entries)
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return entry.Prefab;

        Debug.LogWarning($"[WorkflowAssetCatalog] No prefab found for key: '{key}'");
        return null;
    }
}
