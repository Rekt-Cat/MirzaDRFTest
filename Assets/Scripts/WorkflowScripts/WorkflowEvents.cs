using System;
using UnityEngine;

public static class WorkflowEvents
{
    public static event Action<Pose> OnTargetPoseUpdated;

    internal static void RaiseTargetPoseUpdated(Pose pose)
    {
        OnTargetPoseUpdated?.Invoke(pose);
    }
}
