using System;
using UnityEngine;

/// <summary>
/// Central static event hub for the Workflow scene.
/// All inter-script communication routes through here.
/// </summary>
public static class WorkflowEvents
{
    public static event Action<int, WorkflowStep> OnStepChanged;
    public static event Action<Pose> OnTargetPoseUpdated;

    internal static void RaiseStepChanged(int index, WorkflowStep step)
    {
        Debug.Log($"BUG: WorkflowEvents.RaiseStepChanged — index={index} targetClass={step.TargetClass} listeners={OnStepChanged?.GetInvocationList().Length ?? 0}");
        if (OnStepChanged == null) return;
        foreach (var listener in OnStepChanged.GetInvocationList())
        {
            try { listener.DynamicInvoke(index, step); }
            catch (System.Exception e) { Debug.LogError($"BUG: WorkflowEvents.RaiseStepChanged — listener {listener.Method.Name} threw: {e.Message}"); }
        }
    }

    internal static void RaiseTargetPoseUpdated(Pose pose)
    {
        Debug.Log($"BUG: WorkflowEvents.RaiseTargetPoseUpdated — pos={pose.position} listeners={OnTargetPoseUpdated?.GetInvocationList().Length ?? 0}");
        if (OnTargetPoseUpdated == null) return;
        foreach (var listener in OnTargetPoseUpdated.GetInvocationList())
        {
            try { listener.DynamicInvoke(pose); }
            catch (System.Exception e) { Debug.LogError($"BUG: WorkflowEvents.RaiseTargetPoseUpdated — listener {listener.Method.Name} threw: {e.Message}"); }
        }
    }
}
