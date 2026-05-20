using UnityEngine;
using Logger = Workflow.Utils.Logger;

public class WorkflowDetectionSwitcher : MonoBehaviour
{
    [SerializeField] bool _useRTDetr = true;

    [Space]
    [SerializeField] WorkflowDetectionController _rtDetrController;

#if USING_SNAPDRAGON_SPACES_SDK
    [SerializeField] WorkflowARImageTrackingController _imageTrackingController;
#endif

    private readonly Logger _logger = new(true, nameof(WorkflowDetectionSwitcher));

    void Awake()
    {
        _logger.Log($"BUG: Awake — _useRTDetr={_useRTDetr}");
        _logger.Log($"BUG: _rtDetrController={(  _rtDetrController   == null ? "NULL" : "assigned")}");

#if USING_SNAPDRAGON_SPACES_SDK
        _logger.Log($"BUG: _imageTrackingController={(_imageTrackingController == null ? "NULL" : "assigned")}");
#endif

        if (_rtDetrController != null)
        {
            _rtDetrController.enabled = _useRTDetr;
            _logger.Log($"BUG: WorkflowDetectionController.enabled set to {_useRTDetr}");
        }
        else
        {
            _logger.Log("BUG: WorkflowDetectionController is NULL — cannot toggle");
        }

#if USING_SNAPDRAGON_SPACES_SDK
        if (_imageTrackingController != null)
        {
            _imageTrackingController.enabled = !_useRTDetr;
            _logger.Log($"BUG: WorkflowARImageTrackingController.enabled set to {!_useRTDetr}");
        }
        else
        {
            _logger.Log("BUG: WorkflowARImageTrackingController is NULL — cannot toggle");
        }
#endif
    }
}
