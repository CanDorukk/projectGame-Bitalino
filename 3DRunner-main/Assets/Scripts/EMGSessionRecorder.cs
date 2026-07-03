using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records EMG session metrics and signals from live UDP samples (leftRms/rightRms),
/// then generates a PDF report via session_report.py.
/// </summary>
[DefaultExecutionOrder(-280)]
public class EMGSessionRecorder : MonoBehaviour
{
    private const float MinMvcSpan = 1e-6f;

    [Header("Session")]
    public string participantId = "participant";
    public string sessionsRootFolder = "";

    [Header("Report")]
    public bool generatePdfOnSessionEnd = true;
    public string pythonExecutable = "python";

    [Header("Status (read-only)")]
    public bool isRecording;
    public string lastSessionFolder = "";
    public string lastReportPath = "";

    private EMGInputBridge inputBridge;
    private bool wasGameStarted;
    private bool sessionFinalized;

    private DateTime startedAtUtc;
    private float sessionClock;
    private float lastSampleUnscaledTime = -1f;
    private int signalSampleCount;
    private float leftActivationSeconds;
    private float rightActivationSeconds;
    private float leftMvcSum;
    private float rightMvcSum;
    private int mvcSampleCount;
    private float leftPeakMvcPercent;
    private float rightPeakMvcPercent;
    private float sessionBaseline;
    private float sessionMvc;
    private float sessionThreshold;
    private string currentSessionId;
    private string currentSessionDir;
    private StreamWriter signalsWriter;
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    [Serializable]
    private class SessionMetricsJson
    {
      public string participantId;
      public string sessionId;
      public string startedAtUtc;
      public string endedAtUtc;
      public float durationSeconds;
      public float baseline;
      public float mvc;
      public float emgThreshold;
      public float leftActivationSeconds;
      public float rightActivationSeconds;
      public float leftPeakMvcPercent;
      public float rightPeakMvcPercent;
      public float leftMeanMvcPercent;
      public float rightMeanMvcPercent;
      public float symmetryPercent;
      public int laneChangeCount;
      public int gameScore;
      public int signalSampleCount;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRecorderExists()
    {
      if (EMGInputBridge.Instance == null)
      {
        return;
      }

      if (EMGInputBridge.Instance.GetComponent<EMGSessionRecorder>() != null)
      {
        return;
      }

      EMGInputBridge.Instance.gameObject.AddComponent<EMGSessionRecorder>();
    }

    private void Awake()
    {
      inputBridge = GetComponent<EMGInputBridge>();
      if (inputBridge == null)
      {
        inputBridge = EMGInputBridge.Instance;
      }
    }

    private void Update()
    {
      if (inputBridge == null)
      {
        inputBridge = EMGInputBridge.Instance;
      }

      if (inputBridge == null || GameManager.instance == null)
      {
        return;
      }

      bool gameStarted = GameManager.instance.IsGameStarted;
      bool gameOver = GameManager.instance.isGameOver;

      if (gameStarted && !wasGameStarted)
      {
        BeginRecording();
      }

      if (isRecording && gameOver)
      {
        FinalizeSession();
      }

      wasGameStarted = gameStarted;
    }

    private void OnApplicationQuit()
    {
      if (isRecording)
      {
        FinalizeSession();
      }
    }

    private void OnDestroy()
    {
      UnsubscribeFromSamples();
      CloseSignalsWriter();
    }

    private string ResolveSessionsRoot()
    {
      if (!string.IsNullOrWhiteSpace(sessionsRootFolder))
      {
        return sessionsRootFolder;
      }

      return Path.Combine(Application.persistentDataPath, "EMG_Sessions");
    }

    private void BeginRecording()
    {
      if (isRecording)
      {
        return;
      }

      sessionFinalized = false;
      isRecording = true;
      startedAtUtc = DateTime.UtcNow;
      sessionClock = 0f;
      lastSampleUnscaledTime = -1f;
      signalSampleCount = 0;
      leftActivationSeconds = 0f;
      rightActivationSeconds = 0f;
      leftMvcSum = 0f;
      rightMvcSum = 0f;
      mvcSampleCount = 0;
      leftPeakMvcPercent = 0f;
      rightPeakMvcPercent = 0f;

      sessionBaseline = inputBridge.baseline;
      sessionMvc = inputBridge.mvc;
      sessionThreshold = inputBridge.emgThreshold;

      inputBridge.ResetLaneChangeCount();

      currentSessionId = startedAtUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
      currentSessionDir = Path.Combine(ResolveSessionsRoot(), currentSessionId);
      Directory.CreateDirectory(currentSessionDir);

      string csvPath = Path.Combine(currentSessionDir, "signals.csv");
      signalsWriter = new StreamWriter(csvPath, false, Utf8NoBom);
      signalsWriter.WriteLine("time_s,left_rms,right_rms");

      lastSessionFolder = currentSessionDir;
      lastReportPath = "";

      inputBridge.OnEmgSample += HandleEmgSample;

      UnityEngine.Debug.Log(
        $"[EMG Session] Recording started: {currentSessionDir} " +
        $"(baseline={sessionBaseline:F2}, mvc={sessionMvc:F2}, thr={sessionThreshold:F2})");
    }

    private void HandleEmgSample(EmgSampleReading sample)
    {
      if (!isRecording || signalsWriter == null)
      {
        return;
      }

      float now = Time.unscaledTime;
      float dt = lastSampleUnscaledTime >= 0f ? now - lastSampleUnscaledTime : 0f;
      lastSampleUnscaledTime = now;

      if (sample.baseline > 0f || sample.mvc > sample.baseline)
      {
        sessionBaseline = sample.baseline;
        sessionMvc = sample.mvc;
      }

      if (sample.emgThreshold > 0f)
      {
        sessionThreshold = sample.emgThreshold;
      }

      sessionClock += dt;

      signalsWriter.WriteLine(
        string.Format(
          CultureInfo.InvariantCulture,
          "{0:F3},{1:F2},{2:F2}",
          sessionClock,
          sample.leftRms,
          sample.rightRms));

      signalSampleCount++;

      if (sessionThreshold > 0f)
      {
        if (sample.leftRms > sessionThreshold)
        {
          leftActivationSeconds += dt;
        }

        if (sample.rightRms > sessionThreshold)
        {
          rightActivationSeconds += dt;
        }
      }

      float leftMvc = RmsToMvcPercent(sample.leftRms, sessionBaseline, sessionMvc);
      float rightMvc = RmsToMvcPercent(sample.rightRms, sessionBaseline, sessionMvc);
      leftMvcSum += leftMvc;
      rightMvcSum += rightMvc;
      mvcSampleCount++;

      if (leftMvc > leftPeakMvcPercent)
      {
        leftPeakMvcPercent = leftMvc;
      }

      if (rightMvc > rightPeakMvcPercent)
      {
        rightPeakMvcPercent = rightMvc;
      }
    }

    private static float RmsToMvcPercent(float rms, float baseline, float mvc)
    {
      if (rms <= baseline)
      {
        return 0f;
      }

      float span = Mathf.Max(mvc - baseline, MinMvcSpan);
      return Mathf.Clamp01((rms - baseline) / span) * 100f;
    }

    private void FinalizeSession()
    {
      if (!isRecording || sessionFinalized)
      {
        return;
      }

      sessionFinalized = true;
      isRecording = false;

      UnsubscribeFromSamples();
      CloseSignalsWriter();

      DateTime endedAtUtc = DateTime.UtcNow;
      float durationSeconds = (float)(endedAtUtc - startedAtUtc).TotalSeconds;

      float leftMeanMvc = mvcSampleCount > 0 ? leftMvcSum / mvcSampleCount : 0f;
      float rightMeanMvc = mvcSampleCount > 0 ? rightMvcSum / mvcSampleCount : 0f;
      float symmetryPercent = ComputeSymmetryPercent(leftMeanMvc, rightMeanMvc);

      int gameScore = 0;
      CheckCollisions collisions = FindObjectOfType<CheckCollisions>();
      if (collisions != null)
      {
        gameScore = collisions.score;
      }

      var metrics = new SessionMetricsJson
      {
        participantId = participantId,
        sessionId = currentSessionId,
        startedAtUtc = startedAtUtc.ToString("o", CultureInfo.InvariantCulture),
        endedAtUtc = endedAtUtc.ToString("o", CultureInfo.InvariantCulture),
        durationSeconds = durationSeconds,
        baseline = sessionBaseline,
        mvc = sessionMvc,
        emgThreshold = sessionThreshold,
        leftActivationSeconds = leftActivationSeconds,
        rightActivationSeconds = rightActivationSeconds,
        leftPeakMvcPercent = leftPeakMvcPercent,
        rightPeakMvcPercent = rightPeakMvcPercent,
        leftMeanMvcPercent = leftMeanMvc,
        rightMeanMvcPercent = rightMeanMvc,
        symmetryPercent = symmetryPercent,
        laneChangeCount = inputBridge.laneChangeCount,
        gameScore = gameScore,
        signalSampleCount = signalSampleCount,
      };

      string jsonPath = Path.Combine(currentSessionDir, "session.json");
      File.WriteAllText(jsonPath, JsonUtility.ToJson(metrics, true), Utf8NoBom);

      UnityEngine.Debug.Log(
        $"[EMG Session] Data saved: {currentSessionDir} " +
        $"({signalSampleCount} UDP samples, L/R RMS from BITalino)");

      if (generatePdfOnSessionEnd)
      {
        TryGeneratePdfReport(currentSessionDir);
      }
    }

    private void UnsubscribeFromSamples()
    {
      if (inputBridge != null)
      {
        inputBridge.OnEmgSample -= HandleEmgSample;
      }
    }

    private static float ComputeSymmetryPercent(float leftMeanMvc, float rightMeanMvc)
    {
      float maxSide = Mathf.Max(leftMeanMvc, rightMeanMvc, 0.001f);
      float minSide = Mathf.Min(leftMeanMvc, rightMeanMvc);
      return minSide / maxSide * 100f;
    }

    private void CloseSignalsWriter()
    {
      if (signalsWriter == null)
      {
        return;
      }

      try
      {
        signalsWriter.Flush();
        signalsWriter.Close();
      }
      catch
      {
      }

      signalsWriter = null;
    }

    private void TryGeneratePdfReport(string sessionDir)
    {
      string reportScript = Path.Combine(Application.streamingAssetsPath, "EMG", "session_report.py");
      if (!File.Exists(reportScript))
      {
        UnityEngine.Debug.LogWarning(
          $"[EMG Session] session_report.py not found: {reportScript}");
        return;
      }

      string pdfPath = Path.Combine(sessionDir, "session_report.pdf");

      try
      {
        var psi = new ProcessStartInfo
        {
          FileName = pythonExecutable,
          Arguments = $"\"{reportScript}\" \"{sessionDir}\"",
          WorkingDirectory = Path.GetDirectoryName(reportScript),
          CreateNoWindow = true,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
        };

        using Process process = Process.Start(psi);
        if (process == null)
        {
          UnityEngine.Debug.LogWarning("[EMG Session] Could not start Python for PDF report.");
          return;
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
          UnityEngine.Debug.Log("[EMG Session] " + stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
          UnityEngine.Debug.LogWarning("[EMG Session] " + stderr.Trim());
        }

        if (process.ExitCode == 0 && File.Exists(pdfPath))
        {
          lastReportPath = pdfPath;
          UnityEngine.Debug.Log($"[EMG Session] PDF report: {pdfPath}");
        }
        else
        {
          UnityEngine.Debug.LogWarning(
            $"[EMG Session] PDF generation failed (exit {process.ExitCode}). " +
            "Install Python + matplotlib: pip install matplotlib");
        }
      }
      catch (Exception ex)
      {
        UnityEngine.Debug.LogWarning("[EMG Session] PDF generation error: " + ex.Message);
      }
    }
}
