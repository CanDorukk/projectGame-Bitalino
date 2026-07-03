using TMPro;
using UnityEngine;

/// <summary>
/// Shows Python calibration phases on the Canvas panel via UDP JSON.
/// Left/right EMG line graphs anchored to bottom corners.
/// </summary>
public class EMGCalibrationUI : MonoBehaviour
{
    public EMGInputBridge inputBridge;
    public GameObject calibrationPanel;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI statusText;
    public EMGRealtimeGraph leftGraph;
    public EMGRealtimeGraph rightGraph;

    [Header("Graph Layout")]
    public Vector2 graphMargin = new Vector2(50f, 50f);
    public Vector2 graphSize = new Vector2(220f, 100f);

    [Header("Connection Warning")]
    public Color connectedStatusColor = new Color(0.35f, 0.95f, 0.45f, 1f);
    public Color disconnectedStatusColor = new Color(1f, 0.35f, 0.3f, 1f);

    private string lastCalibrationTitle = "";

    private void Start()
    {
        if (inputBridge == null)
        {
            inputBridge = EMGInputBridge.Instance;
        }

        EnsureGraphs();
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void EnsureGraphs()
    {
        Transform parent = calibrationPanel != null ? calibrationPanel.transform : transform;

        if (leftGraph == null)
        {
            leftGraph = FindOrCreateGraph(parent, "LeftEMGGraph", "EMG Left",
                new Color(0.4f, 0.78f, 1f, 1f));
        }

        if (rightGraph == null)
        {
            rightGraph = FindOrCreateGraph(parent, "RightEMGGraph", "EMG Right",
                new Color(1f, 0.78f, 0.4f, 1f));
        }

        ApplyGraphLayout();
    }

    private EMGRealtimeGraph FindOrCreateGraph(
        Transform parent, string name, string title, Color lineColor)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            EMGRealtimeGraph graph = existing.GetComponent<EMGRealtimeGraph>();
            if (graph != null)
            {
                return graph;
            }
        }

        return EMGRealtimeGraph.Create(parent, name, Vector2.zero, title, lineColor, graphSize);
    }

    private void ApplyGraphLayout()
    {
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

    private void Refresh()
    {
        if (inputBridge == null)
        {
            return;
        }

        bool show = !inputBridge.IsReadyForStartMenu;
        if (calibrationPanel != null)
        {
            calibrationPanel.SetActive(show);
        }

        SetGraphsVisible(show);

        if (!show)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.text = inputBridge.calibrationTitle;
        }

        if (instructionText != null)
        {
            instructionText.text = inputBridge.calibrationInstruction;
        }

        if (timerText != null)
        {
            if (inputBridge.calibrationSecondsRemaining >= 0f)
            {
                timerText.text = Mathf.CeilToInt(inputBridge.calibrationSecondsRemaining) + " s";
            }
            else
            {
                timerText.text = "";
            }
        }

        if (statusText != null)
        {
            bool udpOk = inputBridge.isConnected;
            bool deviceOk = inputBridge.deviceConnected;
            statusText.fontSize = (!udpOk || !deviceOk) ? 26 : 20;
            statusText.fontStyle = (!udpOk || !deviceOk) ? FontStyles.Bold : FontStyles.Italic;

            string title = inputBridge.calibrationTitle ?? "";
            bool calibError = title.Contains("CALIBRATION ERROR") || title.Contains("CONNECTION ERROR");

            if (calibError)
            {
                statusText.text = inputBridge.calibrationInstruction;
                statusText.color = disconnectedStatusColor;
            }
            else if (!udpOk)
            {
                statusText.text = "WAITING FOR EMG\nSoftware signal paused briefly.\nKeep holding if in MVC phase.";
                statusText.color = disconnectedStatusColor;
            }
            else if (!deviceOk)
            {
                statusText.text = "NO CONNECTION\nCould not connect to BITalino.\nCheck COM port and Bluetooth.";
                statusText.color = disconnectedStatusColor;
            }
            else
            {
                statusText.text = "Device connected - continue calibration";
                statusText.color = connectedStatusColor;
            }
        }

        if (titleText != null && (!inputBridge.isConnected || !inputBridge.deviceConnected))
        {
            string t = inputBridge.calibrationTitle ?? "";
            titleText.color = (!t.Contains("NO CONNECTION") && !t.Contains("CONNECTION ERROR"))
                ? disconnectedStatusColor
                : Color.white;
        }
        else if (titleText != null)
        {
            titleText.color = Color.white;
        }

        if (inputBridge.calibrationTitle != lastCalibrationTitle)
        {
            lastCalibrationTitle = inputBridge.calibrationTitle;
            leftGraph?.ClearHistory();
            rightGraph?.ClearHistory();
        }

        float threshold = inputBridge.emgThreshold;
        leftGraph?.PushSample(inputBridge.leftRms, threshold);
        rightGraph?.PushSample(inputBridge.rightRms, threshold);
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
