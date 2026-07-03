using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

/// <summary>
/// Starts bundled emg_backend.exe when the game launches.
/// Editor-only fallback: system Python + synced code.py if exe is missing.
/// </summary>
[DefaultExecutionOrder(-200)]
public class EMGProcessLauncher : MonoBehaviour
{
    public static EMGProcessLauncher Instance { get; private set; }

    [Header("Auto Start")]
    public bool autoLaunchOnPlay = true;
    public string bundledExeName = "emg_backend.exe";
    public string bitalinoPort = "COM6";
    public string unityIp = "127.0.0.1";
    public int unityPort = 5055;

#if UNITY_EDITOR
    [Header("Editor")]
    public bool editorPreferPythonForSpeed = true;
    public string pythonExecutable = "python";
#endif

    [Header("Paths (empty = auto)")]
    public string backendPathOverride = "";

    [Header("Debug")]
    public bool logBackendOutputToUnityConsole = true;

    private Process backendProcess;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (autoLaunchOnPlay)
        {
            LaunchBackend();
        }
    }

    public void LaunchBackend()
    {
        StopBackend();
        KillOrphanBackendProcesses();

        string emgDir = ResolveEmgDirectory();
        string bundledExe = Path.Combine(emgDir, bundledExeName);
        string backendArgs = $"--port {bitalinoPort} --unity-ip {unityIp} --unity-port {unityPort}";

        UnityEngine.Debug.Log($"[EMG] EMG dir: {emgDir}");
        UnityEngine.Debug.Log($"[EMG] Bundled exe exists: {File.Exists(bundledExe)} -> {bundledExe}");

        ProcessStartInfo psi;
        bool redirectOutput;

        if (!string.IsNullOrWhiteSpace(backendPathOverride) && File.Exists(backendPathOverride))
        {
            bool isScript = backendPathOverride.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
            psi = CreateStartInfo(
                backendPathOverride,
                isScript ? $"\"{backendPathOverride}\" {backendArgs}" : backendArgs,
                Path.GetDirectoryName(backendPathOverride),
                redirectOutput: isScript);
            redirectOutput = isScript;
        }
#if UNITY_EDITOR
        else if (editorPreferPythonForSpeed && TryResolveEditorScriptPath(emgDir, out string editorScript))
        {
            string args = $"\"{editorScript}\" {backendArgs}";
            psi = CreateStartInfo(pythonExecutable, args, Path.GetDirectoryName(editorScript), redirectOutput: true);
            redirectOutput = true;
        }
#endif
        else if (File.Exists(bundledExe))
        {
            psi = CreateStartInfo(bundledExe, backendArgs, emgDir, redirectOutput: false);
            redirectOutput = false;
        }
#if UNITY_EDITOR
        else if (TryResolveEditorScriptPath(emgDir, out string fallbackScript))
        {
            string args = $"\"{fallbackScript}\" {backendArgs}";
            psi = CreateStartInfo(pythonExecutable, args, Path.GetDirectoryName(fallbackScript), redirectOutput: true);
            redirectOutput = true;
        }
#endif
        else
        {
            UnityEngine.Debug.LogError(
                "EMG backend not found. Run EMG > Build Bundled EMG Backend.\n" +
                $"Looked for: {bundledExe}");
            return;
        }

        try
        {
            backendProcess = Process.Start(psi);
            if (backendProcess == null)
            {
                UnityEngine.Debug.LogError("[EMG] Process.Start returned null.");
                return;
            }

            if (backendProcess.HasExited)
            {
                UnityEngine.Debug.LogError(
                    $"[EMG] Backend exited immediately (code {backendProcess.ExitCode}). " +
                    $"Check BITalino COM port ({bitalinoPort}) and {psi.FileName}");
            }

            if (logBackendOutputToUnityConsole && redirectOutput)
            {
                backendProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        UnityEngine.Debug.Log("[EMG] " + e.Data);
                    }
                };
                backendProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        UnityEngine.Debug.LogWarning("[EMG] " + e.Data);
                    }
                };
                backendProcess.BeginOutputReadLine();
                backendProcess.BeginErrorReadLine();
            }

            UnityEngine.Debug.Log("[EMG] Backend started: " + psi.FileName);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("EMG failed to start: " + ex.Message);
        }
    }

#if UNITY_EDITOR
    private static bool TryResolveEditorScriptPath(string emgDir, out string scriptPath)
    {
        string streamingScript = Path.Combine(emgDir, "code.py");
        if (File.Exists(streamingScript))
        {
            scriptPath = streamingScript;
            return true;
        }

        string assetsRoot = Application.dataPath;
        string repoRoot = Directory.GetParent(assetsRoot).Parent.FullName;
        string devScript = Path.Combine(repoRoot, "code.py");
        if (File.Exists(devScript))
        {
            scriptPath = devScript;
            return true;
        }

        scriptPath = null;
        return false;
    }
#endif

    private static ProcessStartInfo CreateStartInfo(
        string fileName, string arguments, string workingDirectory, bool redirectOutput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        if (redirectOutput)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }

        return psi;
    }

    public void StopBackend()
    {
        if (backendProcess == null)
        {
            return;
        }

        try
        {
            if (!backendProcess.HasExited)
            {
                backendProcess.Kill();
            }
        }
        catch
        {
        }

        backendProcess.Dispose();
        backendProcess = null;
    }

    private void KillOrphanBackendProcesses()
    {
        string processName = Path.GetFileNameWithoutExtension(bundledExeName);
        if (string.IsNullOrEmpty(processName))
        {
            return;
        }

        try
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    private string ResolveEmgDirectory()
    {
        return Path.Combine(Application.streamingAssetsPath, "EMG");
    }

    private void OnApplicationQuit()
    {
        StopBackend();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            StopBackend();
            Instance = null;
        }
    }
}
