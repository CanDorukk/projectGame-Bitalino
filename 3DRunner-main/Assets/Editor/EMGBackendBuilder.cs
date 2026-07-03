#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds bundled emg_backend.exe (PyInstaller) into StreamingAssets/EMG.
/// Run once before shipping a build; end users do not need Python.
/// </summary>
public static class EMGBackendBuilder
{
    [MenuItem("EMG/Build Bundled EMG Backend (no Python for users)")]
    public static void BuildFromMenu()
    {
        string assets = Application.dataPath;
        string repoRoot = Directory.GetParent(assets).Parent.FullName;
        string script = Path.Combine(repoRoot, "tools", "build_emg_backend.ps1");

        if (!File.Exists(script))
        {
            EditorUtility.DisplayDialog(
                "EMG",
                "build_emg_backend.ps1 not found at:\n" + script,
                "OK");
            return;
        }

        bool proceed = EditorUtility.DisplayDialog(
            "EMG Backend Bundle",
            "This runs PyInstaller and copies emg_backend.exe to StreamingAssets/EMG.\n\n" +
            "Requires Python + pip on THIS machine (developer only).\n" +
            "May take several minutes.\n\nContinue?",
            "Build",
            "Cancel");

        if (!proceed)
        {
            return;
        }

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{script}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    UnityEngine.Debug.LogError("EMG backend build failed:\n" + error + "\n" + output);
                    EditorUtility.DisplayDialog("EMG", "Build failed. See Console for details.", "OK");
                    return;
                }

                UnityEngine.Debug.Log(output);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "EMG",
                    "Bundled backend ready in Assets/StreamingAssets/EMG.\nNow create your Unity build.",
                    "OK");
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("EMG", "Could not run build script:\n" + ex.Message, "OK");
        }
    }

    [MenuItem("EMG/Build Bundled EMG Backend (no Python for users)", true)]
    private static bool BuildFromMenuValidate()
    {
        return !EditorApplication.isPlaying;
    }
}
#endif
