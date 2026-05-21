using UnityEngine;
using UnityEngine.UIElements;

public class WorkflowTrackingStatusView : MonoBehaviour
{
    [SerializeField] UIDocument _uiDocument;

    Label _statusLabel;
    Label _statusDetail;
    VisualElement _indicator;

    static readonly Color ColorStarting  = new(0.39f, 0.39f, 0.47f);
    static readonly Color ColorDetecting = new(0.95f, 0.76f, 0.19f);
    static readonly Color ColorDetected  = new(0.22f, 0.78f, 0.44f);

    void OnEnable()
    {
        WorkflowTrackingEvents.OnStatusChanged += OnStatusChanged;

        var root  = _uiDocument.rootVisualElement;
        _statusLabel  = root.Q<Label>("status-label");
        _statusDetail = root.Q<Label>("status-detail");
        _indicator    = root.Q<VisualElement>("status-indicator");

        OnStatusChanged(WorkflowTrackingStatus.Starting);
    }

    void OnDisable()
    {
        WorkflowTrackingEvents.OnStatusChanged -= OnStatusChanged;
    }

    void OnStatusChanged(WorkflowTrackingStatus status)
    {
        switch (status)
        {
            case WorkflowTrackingStatus.Starting:
                Set("Starting", "Initialising XR session…", ColorStarting);
                break;
            case WorkflowTrackingStatus.Detecting:
                Set("Detecting", "Point camera at the target image", ColorDetecting);
                break;
            case WorkflowTrackingStatus.Detected:
                Set("Detected", "Target image found ✓", ColorDetected);
                break;
        }
    }

    void Set(string label, string detail, Color color)
    {
        if (_statusLabel  != null) _statusLabel.text  = label;
        if (_statusDetail != null) _statusDetail.text = detail;
        if (_indicator    != null) _indicator.style.backgroundColor = new StyleColor(color);
    }
}
