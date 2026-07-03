using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives left/right vertical intensity bars on the Canvas from live EMG RMS values.
/// </summary>
public class EMGIntensityBarsUI : MonoBehaviour
{
    private const float MinMvcSpan = 1e-6f;

    public EMGInputBridge inputBridge;
    public Image leftFillBar;
    public Image rightFillBar;
    public float smoothing = 18f;

    private float smoothedLeft;
    private float smoothedRight;
    private bool barsResolved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureOnCanvas()
    {
        EMGInputDebugUI debugUi = FindObjectOfType<EMGInputDebugUI>();
        if (debugUi == null)
        {
            return;
        }

        if (debugUi.GetComponent<EMGIntensityBarsUI>() != null)
        {
            return;
        }

        debugUi.gameObject.AddComponent<EMGIntensityBarsUI>();
    }

    private void Awake()
    {
        if (inputBridge == null)
        {
            inputBridge = EMGInputBridge.Instance;
        }
    }

    private void Start()
    {
        ResolveBars();
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

        if (!barsResolved)
        {
            ResolveBars();
        }

        if (leftFillBar == null && rightFillBar == null)
        {
            return;
        }

        float leftTarget = NormalizeLevel(inputBridge.leftRms);
        float rightTarget = NormalizeLevel(inputBridge.rightRms);

        float blend = smoothing * Time.unscaledDeltaTime;
        smoothedLeft = Mathf.Lerp(smoothedLeft, leftTarget, blend);
        smoothedRight = Mathf.Lerp(smoothedRight, rightTarget, blend);

        if (leftFillBar != null)
        {
            leftFillBar.fillAmount = smoothedLeft;
        }

        if (rightFillBar != null)
        {
            rightFillBar.fillAmount = smoothedRight;
        }
    }

    private void ResolveBars()
    {
        if (rightFillBar == null)
        {
            GameObject rightGo = GameObject.Find("FillRighBar");
            if (rightGo == null)
            {
                rightGo = GameObject.Find("FillRightBar");
            }

            if (rightGo != null)
            {
                rightFillBar = rightGo.GetComponent<Image>();
            }
        }

        if (leftFillBar == null)
        {
            GameObject fillLeftGo = GameObject.Find("FillLeft");
            if (fillLeftGo != null)
            {
                Transform existingChild = fillLeftGo.transform.Find("FillLeftBar");
                if (existingChild != null)
                {
                    leftFillBar = existingChild.GetComponent<Image>();
                }
                else
                {
                    leftFillBar = CreateFillBarChild(
                        fillLeftGo.transform,
                        "FillLeftBar",
                        new Color(0.25f, 0.55f, 1f, 1f),
                        rightFillBar);
                }

                EnsureBackgroundBar(fillLeftGo.GetComponent<Image>());
            }
        }

        ConfigureFillImage(leftFillBar);
        ConfigureFillImage(rightFillBar);
        barsResolved = leftFillBar != null || rightFillBar != null;
    }

    private static Image CreateFillBarChild(Transform parent, string name, Color color, Image spriteSource)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rt = go.GetComponent<RectTransform>();
        StretchFull(rt);

        Image img = go.GetComponent<Image>();
        if (spriteSource != null && spriteSource.sprite != null)
        {
            img.sprite = spriteSource.sprite;
        }

        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void EnsureBackgroundBar(Image background)
    {
        if (background == null)
        {
            return;
        }

        background.type = Image.Type.Simple;
        background.fillAmount = 1f;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void ConfigureFillImage(Image img)
    {
        if (img == null)
        {
            return;
        }

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Vertical;
        img.fillOrigin = (int)Image.OriginVertical.Bottom;
    }

    private float NormalizeLevel(float rms)
    {
        if (inputBridge.isCalibrated && inputBridge.mvc > inputBridge.baseline)
        {
            return NormalizeRms(rms, inputBridge.baseline, inputBridge.mvc);
        }

        float threshold = inputBridge.emgThreshold;
        if (threshold > 0f)
        {
            return Mathf.Clamp01(rms / Mathf.Max(threshold * 2.5f, 1f));
        }

        return Mathf.Clamp01(rms / 50f);
    }

    private static float NormalizeRms(float rms, float baseline, float mvc)
    {
        if (mvc <= baseline || rms <= baseline)
        {
            return 0f;
        }

        return Mathf.Clamp01((rms - baseline) / Mathf.Max(mvc - baseline, MinMvcSpan));
    }
}
