using UnityEngine;

[CreateAssetMenu(fileName = "WorkflowStep", menuName = "Workflow/Step")]
public class WorkflowStepSO : ScriptableObject
{
    public string Number;
    public string Name;
    public string Description;
    public string TargetClass;
    public string PrefabKey;

    public WorkflowStep ToWorkflowStep() => new WorkflowStep
    {
        Number      = Number,
        Name        = Name,
        Description = Description,
        TargetClass = TargetClass,
        PrefabKey   = PrefabKey,
    };
}
