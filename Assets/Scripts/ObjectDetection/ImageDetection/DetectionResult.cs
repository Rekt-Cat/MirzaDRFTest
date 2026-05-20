using UnityEngine;

public struct DetectionResult
{
    public Rect normalizedRect;
    public int labelId;
    public string className;
    public float score;
}
