#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// Ensures bundled emg_backend.exe exists before a player build.
/// </summary>
public class EMGBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string bundled = System.IO.Path.Combine(
            UnityEngine.Application.dataPath,
            "StreamingAssets",
            "EMG",
            "emg_backend.exe");

        if (!System.IO.File.Exists(bundled))
        {
            UnityEngine.Debug.LogWarning(
                "EMG: emg_backend.exe not found in Assets/StreamingAssets/EMG. " +
                "Run EMG > Build Bundled EMG Backend before building the game.");
        }
    }
}
#endif
