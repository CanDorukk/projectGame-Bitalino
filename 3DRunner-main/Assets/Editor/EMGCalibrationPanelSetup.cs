#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor tools for EMG UI setup and scene cleanup.
/// Menu: EMG / Setup EMG UI
/// </summary>
public static class EMGCalibrationPanelSetup
{
    private const string CalibPanelName = "CalibrationPanel";
    private const string StartPanelName = "StartGamePanel";

    [MenuItem("EMG/Setup EMG UI (Calibration + Start)")]
    public static void SetupFromMenu()
    {
        SetupInActiveScene(silent: false);
    }

    [MenuItem("EMG/Clean Scene (Remove Duplicate EMG UI)")]
    public static void CleanSceneFromMenu()
    {
        CleanDuplicateEmgUi(silent: false);
    }

    public static void CleanDuplicateEmgUi(bool silent)
    {
        GameManager gm = Object.FindObjectOfType<GameManager>();
        GameObject canonicalStart = gm != null ? gm.startGamePanel : null;

        if (canonicalStart == null)
        {
            EMGStartMenuUI[] menus = Object.FindObjectsOfType<EMGStartMenuUI>(true);
            if (menus.Length > 0 && menus[0] != null)
            {
                canonicalStart = menus[0].gameObject;
            }
        }

        var panelsToRemove = new System.Collections.Generic.List<GameObject>();
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (go == null || go.name != StartPanelName)
            {
                continue;
            }

            if (canonicalStart != null && go == canonicalStart)
            {
                continue;
            }

            panelsToRemove.Add(go);
        }

        int removedPanels = 0;
        foreach (GameObject go in panelsToRemove)
        {
            if (go == null)
            {
                continue;
            }

            Undo.DestroyObjectImmediate(go);
            removedPanels++;
        }

        string[] legacyNames =
        {
            "LeftBarBg", "LeftBarFill", "RightBarBg", "RightBarFill",
            "FillLeftBar", "DebugLeftEMGGraph", "DebugRightEMGGraph",
        };

        var legacyToRemove = new System.Collections.Generic.List<GameObject>();
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (go == null)
            {
                continue;
            }

            string goName = go.name;
            foreach (string legacyName in legacyNames)
            {
                if (goName == legacyName)
                {
                    legacyToRemove.Add(go);
                    break;
                }
            }
        }

        int removedLegacy = 0;
        foreach (GameObject go in legacyToRemove)
        {
            if (go == null)
            {
                continue;
            }

            Undo.DestroyObjectImmediate(go);
            removedLegacy++;
        }

        if (canonicalStart == null)
        {
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go != null && go.name == StartPanelName)
                {
                    canonicalStart = go;
                    break;
                }
            }
        }

        if (gm != null && canonicalStart != null)
        {
            Undo.RecordObject(gm, "Wire Start Panel");
            gm.startGamePanel = canonicalStart;
            canonicalStart.SetActive(false);
            EditorUtility.SetDirty(gm);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        if (!silent)
        {
            Debug.Log($"EMG cleanup: removed {removedPanels} duplicate StartGamePanel(s), {removedLegacy} legacy object(s).");
        }
    }

    private static void SetupInActiveScene(bool silent)
    {
        Canvas canvas = FindMainCanvas();
        if (canvas == null)
        {
            if (!silent)
            {
                EditorUtility.DisplayDialog("EMG", "Could not find 'Canvas' in the scene.", "OK");
            }
            return;
        }

        EMGInputBridge bridge = Object.FindObjectOfType<EMGInputBridge>();
        if (bridge == null)
        {
            GameObject bridgeGo = new GameObject("EMGInputBridge");
            bridge = bridgeGo.AddComponent<EMGInputBridge>();
            Undo.RegisterCreatedObjectUndo(bridgeGo, "Create EMGInputBridge");
        }
        else
        {
            bridge.gameObject.name = "EMGInputBridge";
        }

        GameManager gm = WireGameManager(bridge);
        WireProcessLauncher(gm);
        WirePlayer(bridge);
        WireDebugUi(canvas, bridge);

        GameObject calibPanel = FindCalibrationPanel();
        if (calibPanel == null)
        {
            calibPanel = CreatePanel(canvas.transform, CalibPanelName, new Color(0.08f, 0.08f, 0.12f, 0.92f));
            EMGCalibrationUI ui = calibPanel.AddComponent<EMGCalibrationUI>();
            ui.inputBridge = bridge;
            ui.calibrationPanel = calibPanel;

            ui.titleText = CreateTmp(calibPanel.transform, "TitleText", "EMG Calibration",
                new Vector2(0, 120), 28, FontStyles.Bold);
            ui.instructionText = CreateTmp(calibPanel.transform, "InstructionText",
                "Starting EMG software automatically...", new Vector2(0, 60), 22, FontStyles.Normal);
            ui.timerText = CreateTmp(calibPanel.transform, "TimerText", "",
                new Vector2(0, 10), 36, FontStyles.Bold);
            ui.statusText = CreateTmp(calibPanel.transform, "StatusText", "Waiting for connection...",
                new Vector2(0, -50), 20, FontStyles.Italic);

            ui.graphMargin = new Vector2(50f, 50f);
            calibPanel.SetActive(true);
            Undo.RegisterCreatedObjectUndo(calibPanel, "Create Calibration Panel");
        }

        GameObject startPanel = FindStartMenuPanel(gm);
        if (startPanel == null && gm != null)
        {
            startPanel = CreateStartMenuPanel(canvas.transform, gm);
            Undo.RegisterCreatedObjectUndo(startPanel, "Create Start Game Panel");
        }

        if (gm != null && startPanel != null)
        {
            Undo.RecordObject(gm, "Wire Start Panel");
            gm.startGamePanel = startPanel;
            startPanel.SetActive(false);
            EditorUtility.SetDirty(gm);
        }

        CleanDuplicateEmgUi(silent: true);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = startPanel != null ? startPanel : calibPanel;

        if (!silent)
        {
            Debug.Log("EMG: Calibration + Start panels ready.");
        }
    }

    private static Canvas FindMainCanvas()
    {
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
        foreach (Canvas c in canvases)
        {
            if (c.gameObject.name == "Canvas" && c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return c;
            }
        }

        return canvases.Length > 0 ? canvases[0] : null;
    }

    private static GameObject FindCalibrationPanel()
    {
        EMGCalibrationUI existing = Object.FindObjectOfType<EMGCalibrationUI>();
        if (existing != null)
        {
            return existing.calibrationPanel != null ? existing.calibrationPanel : existing.gameObject;
        }

        return GameObject.Find(CalibPanelName);
    }

    private static GameObject FindStartMenuPanel(GameManager gm)
    {
        if (gm != null && gm.startGamePanel != null)
        {
            return gm.startGamePanel;
        }

        EMGStartMenuUI existing = Object.FindObjectOfType<EMGStartMenuUI>();
        if (existing != null)
        {
            return existing.gameObject;
        }

        return GameObject.Find(StartPanelName);
    }

    [MenuItem("EMG/Sync Python to StreamingAssets")]
    public static void SyncPythonFromMenu()
    {
        SyncPythonToStreamingAssets();
        Debug.Log("EMG: Python files copied to Assets/StreamingAssets/EMG.");
    }

    public static void SyncPythonToStreamingAssets()
    {
        string assets = Application.dataPath;
        string repoRoot = Directory.GetParent(assets).Parent.FullName;
        string srcCore = Path.Combine(repoRoot, "Python Code", "emg_core.py");
        string srcCode = Path.Combine(repoRoot, "code.py");
        string destDir = Path.Combine(assets, "StreamingAssets", "EMG");

        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(srcCore))
        {
            File.Copy(srcCore, Path.Combine(destDir, "emg_core.py"), true);
        }

        if (File.Exists(srcCode))
        {
            string buildCode = File.ReadAllText(srcCode);
            buildCode = buildCode.Replace(
                "sys.path.insert(0, str(Path(__file__).resolve().parent / \"Python Code\"))",
                "sys.path.insert(0, str(Path(__file__).resolve().parent))");
            File.WriteAllText(Path.Combine(destDir, "code.py"), buildCode);
        }

        AssetDatabase.Refresh();
    }

    private static void WireProcessLauncher(GameManager gm)
    {
        GameObject host = gm != null ? gm.gameObject : GameObject.Find("EMGInputBridge");
        if (host == null)
        {
            return;
        }

        EMGProcessLauncher launcher = host.GetComponent<EMGProcessLauncher>();
        bool created = false;
        if (launcher == null)
        {
            launcher = Undo.AddComponent<EMGProcessLauncher>(host);
            created = true;
        }

        Undo.RecordObject(launcher, "Wire EMG Launcher");
        launcher.autoLaunchOnPlay = true;
        launcher.backendPathOverride = "";
        if (created || string.IsNullOrWhiteSpace(launcher.bitalinoPort))
        {
            launcher.bitalinoPort = "COM7";
        }
        EditorUtility.SetDirty(launcher);
    }

    private static GameManager WireGameManager(EMGInputBridge bridge)
    {
        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm == null)
        {
            return null;
        }

        Undo.RecordObject(gm, "Wire GameManager");
        gm.inputBridge = bridge;

        GameObject pause = GameObject.Find("PauseMenu");
        if (pause != null)
        {
            gm.pausePanel = pause;
            pause.SetActive(false);
        }

        EditorUtility.SetDirty(gm);
        return gm;
    }

    private static void WireDebugUi(Canvas canvas, EMGInputBridge bridge)
    {
        EMGInputDebugUI debugUi = canvas.GetComponent<EMGInputDebugUI>();
        if (debugUi == null)
        {
            debugUi = canvas.gameObject.AddComponent<EMGInputDebugUI>();
        }

        Undo.RecordObject(debugUi, "Wire EMG Debug UI");
        debugUi.inputBridge = bridge;
        EditorUtility.SetDirty(debugUi);
    }

    private static GameObject CreateStartMenuPanel(Transform canvas, GameManager gm)
    {
        GameObject panel = CreatePanel(canvas, StartPanelName, new Color(0.05f, 0.12f, 0.08f, 0.94f));
        panel.SetActive(false);

        CreateTmp(panel.transform, "StartTitle", "Calibration Complete",
            new Vector2(0, 80), 32, FontStyles.Bold);
        CreateTmp(panel.transform, "StartHint", "When ready, start the game",
            new Vector2(0, 20), 22, FontStyles.Normal);

        GameObject buttonGo = new GameObject("StartButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(panel.transform, false);
        buttonGo.layer = LayerMask.NameToLayer("UI");

        RectTransform btnRt = buttonGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(280, 56);
        btnRt.anchoredPosition = new Vector2(0, -60);

        Image btnImg = buttonGo.GetComponent<Image>();
        btnImg.color = new Color(0.15f, 0.65f, 0.25f, 1f);

        Button button = buttonGo.GetComponent<Button>();

        GameObject btnLabel = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        btnLabel.transform.SetParent(buttonGo.transform, false);
        RectTransform labelRt = btnLabel.GetComponent<RectTransform>();
        StretchFull(labelRt);
        TextMeshProUGUI label = btnLabel.GetComponent<TextMeshProUGUI>();
        label.text = "Start Game";
        label.fontSize = 26;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        EMGStartMenuUI menuUi = panel.AddComponent<EMGStartMenuUI>();
        menuUi.gameManager = gm;
        UnityEventTools.AddPersistentListener(button.onClick, menuUi.OnStartButtonPressed);

        return panel;
    }

    private static void WirePlayer(EMGInputBridge bridge)
    {
        PlayerController[] players = Object.FindObjectsOfType<PlayerController>();
        foreach (PlayerController pc in players)
        {
            if (pc.gameObject.CompareTag("Player") || pc.gameObject.name.Contains("Player"))
            {
                Undo.RecordObject(pc, "Wire Player");
                pc.inputBridge = bridge;
                EditorUtility.SetDirty(pc);
                return;
            }
        }

        if (players.Length > 0)
        {
            Undo.RecordObject(players[0], "Wire Player");
            players[0].inputBridge = bridge;
            EditorUtility.SetDirty(players[0]);
        }
    }

    private static GameObject CreatePanel(Transform parent, string name, Color bgColor)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = panel.GetComponent<RectTransform>();
        StretchFull(rt);

        Image img = panel.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true;

        return panel;
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, string text,
        Vector2 anchoredPos, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(700, 80);
        rt.anchoredPosition = anchoredPos;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return tmp;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }
}
#endif
