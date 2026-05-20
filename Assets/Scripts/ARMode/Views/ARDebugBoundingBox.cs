using TMPro;
using UnityEngine;

public class ARDebugBoundingBox : MonoBehaviour
{
    [SerializeField] LineRenderer lineRenderer;
    [SerializeField] TextMeshPro label;

    public void Init(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, string text, Color color)
    {
        if (lineRenderer == null) lineRenderer = GetComponentInChildren<LineRenderer>();
        if (label == null)        label        = GetComponentInChildren<TextMeshPro>();

        // Rectangle: TL → TR → BR → BL → TL (closed loop)
        lineRenderer.positionCount = 5;
        lineRenderer.SetPositions(new[] { tl, tr, br, bl, tl });
        lineRenderer.startWidth  = 0.004f;
        lineRenderer.endWidth    = 0.004f;
        lineRenderer.startColor  = color;
        lineRenderer.endColor    = color;
        lineRenderer.useWorldSpace = true;

        // Assign default material so it renders without errors
        if (lineRenderer.material == null || lineRenderer.material.shader == null)
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        if (label != null)
        {
            label.text      = text;
            label.color     = color;
            label.transform.position = tl;
        }
    }
}
