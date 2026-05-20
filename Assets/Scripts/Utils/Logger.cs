using UnityEngine;

namespace Workflow.Utils
{
  public class Logger
  {
    public static bool logsEnabled = true;
    private bool _enabled = true;
    private string _tag;

    public Logger(string tag)
    {
      _tag = tag;
    }

    public Logger(bool enabled, string tag)
    {
      _enabled = enabled;
      _tag = tag;
    }

    public void Log(string message)
    {
      if (_enabled && logsEnabled)
        Debug.Log("<color=teal>" + _tag + "</color>: " + message);
    }

    public void LogError(string message)
    {
      if (_enabled && logsEnabled)
        Debug.LogError("<color=red>" + _tag + "</color>: " + message);
    }
  }
}
