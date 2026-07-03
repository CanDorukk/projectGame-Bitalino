using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Real-time EMG line graph (reference BioStep pygame draw_emg_graph).
/// Shows RMS history + red threshold line.
/// </summary>
public class EMGRealtimeGraph : MonoBehaviour
{
    public RawImage graphImage;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statsText;

    [Header("Layout")]
    public int graphWidth = 200;
    public int graphHeight = 100;

    [Header("Colors")]
    public Color backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
    public Color lineColor = new Color(0.4f, 0.78f, 1f, 1f);
    public Color thresholdColor = new Color(1f, 0.2f, 0.2f, 1f);
    public Color borderColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private float[] history;
    private Texture2D texture;
    private float threshold;
    private float graphMax = 50f;

    private void Awake()
    {
        EnsureTexture();
    }

    private void OnDestroy()
    {
        if (texture != null)
        {
            Destroy(texture);
        }
    }

    public void PushSample(float rms, float emgThreshold)
    {
        EnsureTexture();

        threshold = emgThreshold;
        graphMax = threshold > 0f ? Mathf.Max(threshold * 2.5f, 50f) : 50f;

        if (history == null || history.Length != graphWidth)
        {
            history = new float[graphWidth];
        }

        for (int i = 0; i < graphWidth - 1; i++)
        {
            history[i] = history[i + 1];
        }

        history[graphWidth - 1] = rms;
        Redraw();

        if (statsText != null)
        {
            string state = rms > emgThreshold ? "ACTIVE" : "rest";
            statsText.text = $"RMS {rms:F1}   Thr {threshold:F1}   [{state}]";
        }
    }

    public void SetTitle(string title)
    {
        if (titleText != null)
        {
            titleText.text = title;
        }
    }

    public void ClearHistory()
    {
        if (history != null)
        {
            for (int i = 0; i < history.Length; i++)
            {
                history[i] = 0f;
            }
        }

        Redraw();
    }

    public static EMGRealtimeGraph Create(
        Transform parent,
        string name,
        Vector2 anchoredPos,
        string title,
        Color line,
        Vector2 size)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(EMGRealtimeGraph));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(parent, false);

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(size.x, size.y + 36f);
        rootRt.anchoredPosition = anchoredPos;

        EMGRealtimeGraph graph = root.GetComponent<EMGRealtimeGraph>();
        graph.lineColor = line;

        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.layer = root.layer;
        titleGo.transform.SetParent(root.transform, false);
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(0f, 18f);
        titleRt.anchoredPosition = Vector2.zero;
        graph.titleText = titleGo.GetComponent<TextMeshProUGUI>();
        graph.titleText.fontSize = 14;
        graph.titleText.alignment = TextAlignmentOptions.Left;
        graph.titleText.color = new Color(0.78f, 0.78f, 0.78f, 1f);
        graph.titleText.text = title;

        GameObject statsGo = new GameObject("Stats", typeof(RectTransform), typeof(TextMeshProUGUI));
        statsGo.layer = root.layer;
        statsGo.transform.SetParent(root.transform, false);
        RectTransform statsRt = statsGo.GetComponent<RectTransform>();
        statsRt.anchorMin = new Vector2(0f, 1f);
        statsRt.anchorMax = new Vector2(1f, 1f);
        statsRt.pivot = new Vector2(0.5f, 1f);
        statsRt.sizeDelta = new Vector2(0f, 16f);
        statsRt.anchoredPosition = new Vector2(0f, -18f);
        graph.statsText = statsGo.GetComponent<TextMeshProUGUI>();
        graph.statsText.fontSize = 12;
        graph.statsText.alignment = TextAlignmentOptions.Left;
        graph.statsText.color = new Color(0.65f, 0.65f, 0.65f, 1f);
        graph.statsText.text = "RMS 0.0   Thr 0.0";

        GameObject borderGo = new GameObject("GraphBorder", typeof(RectTransform), typeof(Image));
        borderGo.layer = root.layer;
        borderGo.transform.SetParent(root.transform, false);
        RectTransform borderRt = borderGo.GetComponent<RectTransform>();
        borderRt.anchorMin = new Vector2(0f, 0f);
        borderRt.anchorMax = new Vector2(1f, 0f);
        borderRt.pivot = new Vector2(0.5f, 0f);
        borderRt.sizeDelta = new Vector2(0f, size.y);
        borderRt.anchoredPosition = Vector2.zero;
        borderGo.GetComponent<Image>().color = graph.borderColor;

        GameObject graphGo = new GameObject("Graph", typeof(RectTransform), typeof(RawImage));
        graphGo.layer = root.layer;
        graphGo.transform.SetParent(borderGo.transform, false);
        RectTransform graphRt = graphGo.GetComponent<RectTransform>();
        graphRt.anchorMin = Vector2.zero;
        graphRt.anchorMax = Vector2.one;
        graphRt.offsetMin = new Vector2(1f, 1f);
        graphRt.offsetMax = new Vector2(-1f, -1f);
        graph.graphImage = graphGo.GetComponent<RawImage>();

        graph.graphWidth = Mathf.Max(32, Mathf.RoundToInt(size.x - 2f));
        graph.graphHeight = Mathf.Max(24, Mathf.RoundToInt(size.y - 2f));
        graph.EnsureTexture();
        graph.ClearHistory();

        return graph;
    }

    /// <summary>
    /// Pin graph to bottom-left or bottom-right with pixel margin (scales with Canvas).
    /// </summary>
    public static void ApplyBottomCornerLayout(RectTransform rt, bool rightSide, Vector2 margin)
    {
        if (rt == null)
        {
            return;
        }

        if (rightSide)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-margin.x, margin.y);
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = margin;
        }
    }

    private void EnsureTexture()
    {
        if (graphImage == null)
        {
            return;
        }

        if (texture == null || texture.width != graphWidth || texture.height != graphHeight)
        {
            if (texture != null)
            {
                Destroy(texture);
            }

            texture = new Texture2D(graphWidth, graphHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            graphImage.texture = texture;
        }

        if (history == null || history.Length != graphWidth)
        {
            history = new float[graphWidth];
        }
    }

    private void Redraw()
    {
        if (texture == null || history == null)
        {
            return;
        }

        Color[] pixels = new Color[graphWidth * graphHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
        }

        if (threshold > 0f)
        {
            int thrY = ValueToY(threshold);
            DrawHorizontalLine(pixels, thrY, thresholdColor, 2);
        }

        for (int x = 1; x < graphWidth; x++)
        {
            float v0 = Mathf.Min(history[x - 1], graphMax);
            float v1 = Mathf.Min(history[x], graphMax);
            int y0 = ValueToY(v0);
            int y1 = ValueToY(v1);
            DrawLine(pixels, x - 1, y0, x, y1, lineColor);
        }

        texture.SetPixels(pixels);
        texture.Apply();
    }

    private int ValueToY(float value)
    {
        float t = Mathf.Clamp01(value / graphMax);
        return Mathf.RoundToInt(t * (graphHeight - 1));
    }

    private void DrawHorizontalLine(Color[] pixels, int y, Color color, int thickness)
    {
        for (int dy = 0; dy < thickness; dy++)
        {
            int py = y + dy;
            if (py < 0 || py >= graphHeight)
            {
                continue;
            }

            for (int x = 0; x < graphWidth; x++)
            {
                pixels[py * graphWidth + x] = color;
            }
        }
    }

    private void DrawLine(Color[] pixels, int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0;
        int y = y0;

        while (true)
        {
            if (x >= 0 && x < graphWidth && y >= 0 && y < graphHeight)
            {
                pixels[y * graphWidth + x] = color;
            }

            if (x == x1 && y == y1)
            {
                break;
            }

            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }
        }
    }
}
