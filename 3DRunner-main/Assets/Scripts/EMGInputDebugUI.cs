using TMPro;
using UnityEngine;

/// <summary>
/// In-game EMG command, RMS text, and corner graphs during gameplay.
/// </summary>
public class EMGInputDebugUI : MonoBehaviour
{
    public EMGInputBridge inputBridge;
    public TextMeshProUGUI commandText;
    public TextMeshProUGUI leftActivationText;
    public TextMeshProUGUI rightActivationText;
    public TextMeshProUGUI connectionText;
    public TextMeshProUGUI sessionText;
    public EMGRealtimeGraph leftGraph;
    public EMGRealtimeGraph rightGraph;

    [Header("Graph Layout (in-game only)")]
    public Vector2 graphMargin = new Vector2(50f, 50f);
    public Vector2 graphSize = new Vector2(200f, 80f);

    private void Start()
    {
        if (inputBridge == null)
        {
            inputBridge = EMGInputBridge.Instance;
        }

        SetGraphsVisible(false);
    }

    private void Update()
    {
        if (inputBridge == null)
        {
            inputBridge = EMGInputBridge.Instance;
        }

        if (inputBridge == null)
        {
            return;
        }

        if (commandText != null)
        {
            commandText.text = "Command: " + inputBridge.currentCommand;
        }

        if (leftActivationText != null)
        {
            leftActivationText.text =
                $"Left RMS: {inputBridge.leftRms:F1}  Thr: {inputBridge.emgThreshold:F1}";
        }

        if (rightActivationText != null)
        {
            rightActivationText.text =
                $"Right RMS: {inputBridge.rightRms:F1}  Thr: {inputBridge.emgThreshold:F1}";
        }

        bool showGraphs = ShouldShowGraphs();
        if (showGraphs)
        {
            EnsureGraphs();
            SetGraphsVisible(true);
            float threshold = inputBridge.emgThreshold;
            leftGraph?.PushSample(inputBridge.leftRms, threshold);
            rightGraph?.PushSample(inputBridge.rightRms, threshold);
        }
        else
        {
            SetGraphsVisible(false);
        }

        if (connectionText != null)
        {
            connectionText.text = inputBridge.isConnected ? "EMG: Connected" : "EMG: Waiting";
        }

        if (sessionText != null)
        {
            if (!inputBridge.IsReadyForStartMenu && inputBridge.isCalibrating)
            {
                sessionText.text = "State: Calibration";
            }
            else if (inputBridge.IsReadyForStartMenu &&
                     GameManager.instance != null &&
                     !GameManager.instance.IsGameStarted)
            {
                sessionText.text = "State: Start menu";
            }
            else if (GameManager.instance != null && GameManager.instance.IsGameStarted)
            {
                sessionText.text = "State: Game";
            }
            else
            {
                sessionText.text = "State: Waiting";
            }
        }
    }

    private bool ShouldShowGraphs()
    {
        if (inputBridge == null || !inputBridge.IsGameInputAllowed)
        {
            return false;
        }

        return GameManager.instance != null && GameManager.instance.IsGameStarted;
    }

    private void EnsureGraphs()
    {
        Transform parent = transform;

        if (leftGraph == null)
        {
            leftGraph = EMGRealtimeGraph.Create(
                parent,
                "DebugLeftEMGGraph",
                Vector2.zero,
                "EMG Left",
                new Color(0.4f, 0.78f, 1f, 1f),
                graphSize);
        }

        if (rightGraph == null)
        {
            rightGraph = EMGRealtimeGraph.Create(
                parent,
                "DebugRightEMGGraph",
                Vector2.zero,
                "EMG Right",
                new Color(1f, 0.78f, 0.4f, 1f),
                graphSize);
        }

        ApplyGraphLayout(leftGraph, false);
        ApplyGraphLayout(rightGraph, true);
    }

    private void ApplyGraphLayout(EMGRealtimeGraph graph, bool rightSide)
    {
        if (graph == null)
        {
            return;
        }

        EMGRealtimeGraph.ApplyBottomCornerLayout(
            graph.GetComponent<RectTransform>(), rightSide, graphMargin);
    }

    private void SetGraphsVisible(bool visible)
    {
        if (leftGraph != null)
        {
            leftGraph.gameObject.SetActive(visible);
        }

        if (rightGraph != null)
        {
            rightGraph.gameObject.SetActive(visible);
        }
    }
}
