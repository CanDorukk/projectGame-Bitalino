using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public enum RunnerInputCommand
{
    NONE,
    LEFT,
    RIGHT
}

/// <summary>Raw EMG reading from one UDP packet (BITalino backend).</summary>
public struct EmgSampleReading
{
    public float leftRms;
    public float rightRms;
    public float baseline;
    public float mvc;
    public float emgThreshold;
}

/// <summary>
/// Receives EMG UDP JSON from bundled emg_backend.exe (port 5055).
/// </summary>
[DefaultExecutionOrder(-300)]
public class EMGInputBridge : MonoBehaviour
{
    public static EMGInputBridge Instance { get; private set; }

    [Header("UDP")]
    public int udpPort = 5055;
    public bool clearCommandIfNoUdpForAWhile = false;
    public float commandHoldSeconds = 0.15f;
    public float calibrationUdpTimeoutSeconds = 5f;

    [Header("Lane Step Debounce")]
    public float laneStepCooldownSeconds = 0.15f;

    [Header("Pause (STOP)")]
    public float pauseMinActivation = 0.40f;
    public float pauseCooldownSeconds = 1.2f;

    [Header("Runtime State")]
    public RunnerInputCommand currentCommand = RunnerInputCommand.NONE;
    public float leftActivation;
    public float rightActivation;
    public float leftRms;
    public float rightRms;
    public float emgThreshold;
    public float baseline;
    public float mvc;
    public int laneChangeCount;
    public bool isConnected;
    public bool deviceConnected;
    public bool isCalibrating;
    public bool isCalibrated;

    private bool calibrationComplete;

    public string calibrationTitle = "EMG Calibration";
    public string calibrationInstruction = "Starting EMG software, please wait...";
    public float calibrationSecondsRemaining = -1f;

    public bool IsGameInputAllowed => calibrationComplete && !isCalibrating;
    public bool IsReadyForStartMenu => calibrationComplete && !isCalibrating;

    public event Action<EmgSampleReading> OnEmgSample;

    private UdpClient udpClient;
    private Thread udpThread;
    private volatile bool keepListening;
    private readonly object stateLock = new object();
    private readonly ConcurrentQueue<string> pendingPayloads = new ConcurrentQueue<string>();
    private DateTime lastUdpMessageUtc = DateTime.MinValue;
    private bool pauseToggleQueued;
    private bool stopWasHeld;
    private float lastPauseToggleTime;
    private RunnerInputCommand lastStepCommand = RunnerInputCommand.NONE;
    private float lastLaneStepTime = -999f;

    [Serializable]
    private class UdpInputPacket
    {
        public string command;
        public float leftActivation;
        public float rightActivation;
        public float leftRms;
        public float rightRms;
        public float emgThreshold;
        public float baseline;
        public float mvc;
        public string phaseTitle;
        public string phaseInstruction;
        public float secondsRemaining;
        public bool isCalibrated;
        public bool deviceConnected;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        isCalibrated = false;
        isCalibrating = true;
        calibrationComplete = false;
        deviceConnected = false;
        StartUdpListener();
    }

    private void Update()
    {
        string latestPayload = null;

        while (pendingPayloads.TryDequeue(out string payload))
        {
            latestPayload = payload;
        }

        if (!string.IsNullOrEmpty(latestPayload))
        {
            ParsePayloadOnMainThread(latestPayload);
        }

        TimeSpan timeSinceLastMessage = DateTime.UtcNow - lastUdpMessageUtc;
        float udpTimeout = isCalibrating ? calibrationUdpTimeoutSeconds : 1.5f;
        isConnected = timeSinceLastMessage.TotalSeconds < udpTimeout;

        if (clearCommandIfNoUdpForAWhile && timeSinceLastMessage.TotalSeconds > commandHoldSeconds)
        {
            SetState(RunnerInputCommand.NONE, 0f, 0f, false);
        }
    }

    private void OnDestroy()
    {
        StopUdpListener();
    }

    public float GetHorizontalInput()
    {
        if (!IsGameInputAllowed)
        {
            return 0f;
        }

        lock (stateLock)
        {
            return CommandToAxis(currentCommand);
        }
    }

    /// <summary>
    /// A-D style: one lane step per new LEFT/RIGHT command (not while held).
    /// Returns -1 (left), 0 (none), or 1 (right).
    /// </summary>
    public int ConsumeLaneStep()
    {
        if (!IsGameInputAllowed)
        {
            return 0;
        }

        lock (stateLock)
        {
            if (currentCommand != RunnerInputCommand.LEFT &&
                currentCommand != RunnerInputCommand.RIGHT)
            {
                lastStepCommand = RunnerInputCommand.NONE;
                return 0;
            }

            if (currentCommand == RunnerInputCommand.LEFT &&
                (emgThreshold <= 0f || leftRms <= emgThreshold))
            {
                lastStepCommand = RunnerInputCommand.NONE;
                return 0;
            }

            if (currentCommand == RunnerInputCommand.RIGHT &&
                (emgThreshold <= 0f || rightRms <= emgThreshold))
            {
                lastStepCommand = RunnerInputCommand.NONE;
                return 0;
            }

            float now = Time.unscaledTime;

            if (now - lastLaneStepTime < laneStepCooldownSeconds)
            {
                return 0;
            }

            if (currentCommand == lastStepCommand)
            {
                return 0;
            }

            lastStepCommand = currentCommand;
            lastLaneStepTime = now;
            laneChangeCount++;
            return currentCommand == RunnerInputCommand.LEFT ? -1 : 1;
        }
    }

    public void ResetLaneChangeCount()
    {
        lock (stateLock)
        {
            laneChangeCount = 0;
        }
    }

    public bool ConsumePauseToggleSignal()
    {
        if (!IsGameInputAllowed)
        {
            return false;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            return true;
        }

        lock (stateLock)
        {
            bool queued = pauseToggleQueued;
            pauseToggleQueued = false;
            return queued;
        }
    }

    private float CommandToAxis(RunnerInputCommand command)
    {
        if (command == RunnerInputCommand.LEFT)
        {
            return -1f;
        }

        if (command == RunnerInputCommand.RIGHT)
        {
            return 1f;
        }

        return 0f;
    }

    private void StartUdpListener()
    {
        try
        {
            udpClient = new UdpClient(udpPort);
            keepListening = true;
            udpThread = new Thread(UdpListenLoop);
            udpThread.IsBackground = true;
            udpThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError("EMG UDP listener failed to start: " + ex.Message);
        }
    }

    private void StopUdpListener()
    {
        keepListening = false;
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }

    private void UdpListenLoop()
    {
        IPEndPoint anyIp = new IPEndPoint(IPAddress.Any, udpPort);

        while (keepListening)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIp);
                string payload = Encoding.UTF8.GetString(data).Trim();
                pendingPayloads.Enqueue(payload);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
            }
        }
    }

    private void ParsePayloadOnMainThread(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        lastUdpMessageUtc = DateTime.UtcNow;

        if (!payload.StartsWith("{") || !payload.EndsWith("}"))
        {
            return;
        }

        UdpInputPacket packet = JsonUtility.FromJson<UdpInputPacket>(payload);
        if (packet == null)
        {
            return;
        }

        if (IsStatusCommand(packet.command))
        {
            ApplyStatusPacket(packet);
            return;
        }

        if (IsCalibrationCommand(packet.command))
        {
            ApplyCalibrationPacket(packet);
            return;
        }

        if (!IsGameInputAllowed)
        {
            return;
        }

        RunnerInputCommand command = CommandFromRms(packet);
        bool pauseSignal = IsPauseCommand(packet.command);
        float left = Mathf.Clamp01(packet.leftActivation);
        float right = Mathf.Clamp01(packet.rightActivation);
        SetState(command, left, right, pauseSignal, packet);
    }

    private static RunnerInputCommand CommandFromRms(UdpInputPacket packet)
    {
        float thr = packet.emgThreshold;
        if (thr <= 0f)
        {
            return ParseCommandFromJsonField(packet.command);
        }

        bool leftOn = packet.leftRms > thr;
        bool rightOn = packet.rightRms > thr;

        if (leftOn && rightOn)
        {
            if (packet.leftRms > packet.rightRms)
            {
                return RunnerInputCommand.LEFT;
            }

            if (packet.rightRms > packet.leftRms)
            {
                return RunnerInputCommand.RIGHT;
            }

            return RunnerInputCommand.NONE;
        }

        if (leftOn)
        {
            return RunnerInputCommand.LEFT;
        }

        if (rightOn)
        {
            return RunnerInputCommand.RIGHT;
        }

        return RunnerInputCommand.NONE;
    }

    private static RunnerInputCommand ParseCommandFromJsonField(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return RunnerInputCommand.NONE;
        }

        string normalized = raw.Trim().ToUpperInvariant();

        if (normalized == "LEFT")
        {
            return RunnerInputCommand.LEFT;
        }

        if (normalized == "RIGHT")
        {
            return RunnerInputCommand.RIGHT;
        }

        return RunnerInputCommand.NONE;
    }

    private bool IsPauseCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string normalized = raw.Trim().ToUpperInvariant();
        return normalized == "STOP" || normalized == "PAUSE";
    }

    private bool IsCalibrationCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToUpperInvariant().StartsWith("CALIB");
    }

    private bool IsStatusCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToUpperInvariant() == "STATUS";
    }

    private void ApplyStatusPacket(UdpInputPacket packet)
    {
        lock (stateLock)
        {
            calibrationTitle = string.IsNullOrEmpty(packet.phaseTitle) ? calibrationTitle : packet.phaseTitle;
            calibrationInstruction = string.IsNullOrEmpty(packet.phaseInstruction)
                ? calibrationInstruction
                : packet.phaseInstruction;
            deviceConnected = packet.deviceConnected;
            isCalibrating = !calibrationComplete;
            currentCommand = RunnerInputCommand.NONE;
            ApplyEmgMetrics(packet);
        }
    }

    private void ApplyEmgMetrics(UdpInputPacket packet)
    {
        leftRms = packet.leftRms;
        rightRms = packet.rightRms;
        if (packet.emgThreshold > 0f)
        {
            emgThreshold = packet.emgThreshold;
        }

        if (packet.isCalibrated && packet.mvc > packet.baseline)
        {
            baseline = packet.baseline;
            mvc = packet.mvc;
        }
    }

    private void RaiseEmgSample()
    {
        EmgSampleReading sample;
        lock (stateLock)
        {
            sample = new EmgSampleReading
            {
                leftRms = leftRms,
                rightRms = rightRms,
                baseline = baseline,
                mvc = mvc,
                emgThreshold = emgThreshold,
            };
        }

        OnEmgSample?.Invoke(sample);
    }

    private void ApplyCalibrationPacket(UdpInputPacket packet)
    {
        lock (stateLock)
        {
            string cmd = packet.command != null ? packet.command.Trim().ToUpperInvariant() : "";

            if (cmd == "CALIB_DONE")
            {
                calibrationComplete = true;
                isCalibrated = true;
                isCalibrating = false;
            }
            else
            {
                calibrationComplete = false;
                isCalibrated = false;
                isCalibrating = true;
            }

            calibrationTitle = string.IsNullOrEmpty(packet.phaseTitle) ? "EMG Calibration" : packet.phaseTitle;
            calibrationInstruction = string.IsNullOrEmpty(packet.phaseInstruction)
                ? ""
                : packet.phaseInstruction;
            calibrationSecondsRemaining = packet.secondsRemaining;
            deviceConnected = true;

            leftActivation = Mathf.Clamp01(packet.leftActivation);
            rightActivation = Mathf.Clamp01(packet.rightActivation);
            currentCommand = RunnerInputCommand.NONE;
            ApplyEmgMetrics(packet);
        }
    }

    private void SetState(RunnerInputCommand command, float left, float right, bool pauseSignal, UdpInputPacket packet = null)
    {
        lock (stateLock)
        {
            if (pauseSignal && (left < pauseMinActivation || right < pauseMinActivation))
            {
                pauseSignal = false;
            }

            if (pauseSignal && !stopWasHeld)
            {
                float now = Time.unscaledTime;
                if (now - lastPauseToggleTime >= pauseCooldownSeconds)
                {
                    pauseToggleQueued = true;
                    lastPauseToggleTime = now;
                }
            }
            stopWasHeld = pauseSignal;

            currentCommand = command;
            leftActivation = left;
            rightActivation = right;

            if (packet != null)
            {
                ApplyEmgMetrics(packet);
            }

            if (packet != null && IsGameInputAllowed)
            {
                RaiseEmgSample();
            }
        }
    }
}
