using UnityEngine;
using UnityEngine.UIElements;
using Logger = Workflow.Utils.Logger;

public class WorkflowStepManager : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private WorkflowStepSO[] _stepAssets;

    private WorkflowStep[] _steps;
    private Label _stepNumber;
    private Label _stepName;
    private Label _stepDescription;
    private Button _btnNext;
    private Button _btnPrev;
    private int _currentIndex;

    private readonly Logger _logger = new(true, nameof(WorkflowStepManager));

    private void Awake()
    {
        _logger.Log($"BUG: Awake — _stepAssets count={(_stepAssets != null ? _stepAssets.Length : 0)} _uiDocument={(_uiDocument == null ? "NULL" : "assigned")}");

        _steps = new WorkflowStep[_stepAssets.Length];
        for (int i = 0; i < _stepAssets.Length; i++)
        {
            _steps[i] = _stepAssets[i].ToWorkflowStep();
            _logger.Log($"BUG: Step[{i}] — name='{_steps[i].Name}' targetClass='{_steps[i].TargetClass}' prefabKey='{_steps[i].PrefabKey}'");
        }
    }

    private void OnEnable()
    {
        _logger.Log("BUG: OnEnable — querying UXML elements");

        var root     = _uiDocument.rootVisualElement;
        var stepView = root.Q<VisualElement>("step-view");

        _logger.Log($"BUG: step-view found={stepView != null}");

        _stepNumber      = stepView.Q<Label>("step-number");
        _stepName        = stepView.Q<Label>("step-name");
        _stepDescription = stepView.Q<Label>("step-description");
        _btnNext         = root.Q<Button>("btn-next");
        _btnPrev         = root.Q<Button>("btn-prev");

        _logger.Log($"BUG: UXML elements — step-number={_stepNumber != null} step-name={_stepName != null} step-description={_stepDescription != null} btn-next={_btnNext != null} btn-prev={_btnPrev != null}");

        _btnNext.clicked += OnNext;
        _btnPrev.clicked += OnPrev;
    }

    // Start runs after all Awake/OnEnable calls — all event listeners are registered by now.
    private void Start()
    {
        _logger.Log("BUG: Start — calling ShowStep(0) now that all listeners are registered");
        ShowStep(0);
    }

    private void OnDisable()
    {
        _logger.Log("BUG: OnDisable");
        _btnNext.clicked -= OnNext;
        _btnPrev.clicked -= OnPrev;
    }

    private void OnNext()
    {
        _logger.Log($"BUG: OnNext — currentIndex={_currentIndex} stepsLength={_steps.Length}");
        if (_currentIndex < _steps.Length - 1)
            ShowStep(_currentIndex + 1);
    }

    private void OnPrev()
    {
        _logger.Log($"BUG: OnPrev — currentIndex={_currentIndex}");
        if (_currentIndex > 0)
            ShowStep(_currentIndex - 1);
    }

    private void ShowStep(int index)
    {
        _currentIndex = index;
        var step = _steps[index];

        _logger.Log($"BUG: ShowStep — index={index} name='{step.Name}' targetClass='{step.TargetClass}'");

        _stepNumber.text      = step.Number;
        _stepName.text        = step.Name;
        _stepDescription.text = step.Description;

        _btnPrev.SetEnabled(_currentIndex > 0);
        _btnNext.SetEnabled(_currentIndex < _steps.Length - 1);

        WorkflowEvents.RaiseStepChanged(index, step);
    }
}
