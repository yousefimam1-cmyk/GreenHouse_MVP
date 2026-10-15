using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen HUD built entirely from code (no scene edits needed):
/// farm status (top), crop reference info (while a crop bed panel is open), key hints and a help overlay.
/// Mirrors to the right side when Arabic is active.
/// </summary>
public class GameHUD : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.05f, 0.72f);
    private static readonly Color Accent = new Color(0.55f, 0.85f, 0.35f, 1f);

    private Canvas canvas;
    private RectTransform statusPanel, infoPanel, hintPanel, helpPanel;
    private TextMeshProUGUI statusText, infoText, hintText, helpText;
    private Cropbed infoBed;
    private float refreshTimer;

    private void Start()
    {
        BuildCanvas();
        Loc.OnLanguageChanged += RefreshAll;
        if (GameManager.Instance != null) GameManager.Instance.OnStatsChanged += RefreshStatus;
        if (UIManager.Instance != null) UIManager.Instance.OnCropbedPanelChanged += ShowCropInfo;
        RefreshAll();
    }

    private void OnDestroy()
    {
        Loc.OnLanguageChanged -= RefreshAll;
        if (GameManager.Instance != null) GameManager.Instance.OnStatsChanged -= RefreshStatus;
        if (UIManager.Instance != null) UIManager.Instance.OnCropbedPanelChanged -= ShowCropInfo;
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            RefreshStatus();
            if (infoBed != null) ShowCropInfo(infoBed);
        }
    }

    public void ToggleHelp()
    {
        if (helpPanel == null) return;
        helpPanel.gameObject.SetActive(!helpPanel.gameObject.activeSelf);
    }

    // ---------------------------------------------------------------- build

    private void BuildCanvas()
    {
        var go = new GameObject("Mazraa HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        statusPanel = MakePanel("Status", new Vector2(0, 1), new Vector2(24, -24), new Vector2(430, 0), out statusText, 26);
        infoPanel = MakePanel("CropInfo", new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(480, 0), out infoText, 22);
        hintPanel = MakePanel("Hints", new Vector2(0, 0), new Vector2(24, 24), new Vector2(430, 0), out hintText, 20);
        helpPanel = MakePanel("Help", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 0), out helpText, 26);
        infoPanel.gameObject.SetActive(false);
        helpPanel.gameObject.SetActive(false);
    }

    private RectTransform MakePanel(string name, Vector2 anchor, Vector2 offset, Vector2 size, out TextMeshProUGUI text, float fontSize)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
        panel.GetComponent<Image>().color = PanelColor;
        panel.GetComponent<Image>().raycastTarget = false;
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        tgo.transform.SetParent(panel.transform, false);
        text = tgo.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        return rt;
    }

    // ---------------------------------------------------------------- content

    private void RefreshAll()
    {
        if (canvas == null) return;
        bool rtl = Loc.IsArabic;
        Mirror(statusPanel, rtl);
        Mirror(infoPanel, rtl);
        Mirror(hintPanel, rtl);
        foreach (var t in new[] { statusText, infoText, hintText, helpText })
        {
            Loc.ApplyFont(t);
            t.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }
        hintText.text = Loc.T("[H] Help   [L] Arabic");
        helpText.text = Title(Loc.T("HELP")) + "\n" + Loc.T("HELP_BODY");
        RefreshStatus();
        if (infoBed != null) ShowCropInfo(infoBed);
    }

    // Move a corner panel to the opposite side for right-to-left reading.
    private static void Mirror(RectTransform rt, bool rtl)
    {
        float x = rtl ? 1f : 0f;
        rt.anchorMin = new Vector2(x, rt.anchorMin.y);
        rt.anchorMax = new Vector2(x, rt.anchorMax.y);
        rt.pivot = new Vector2(x, rt.pivot.y);
        rt.anchoredPosition = new Vector2(rtl ? -24f : 24f, rt.anchoredPosition.y);
    }

    private static string Title(string s) => "<color=#" + ColorUtility.ToHtmlStringRGB(Accent) + "><b>" + s + "</b></color>";

    private void RefreshStatus()
    {
        if (statusText == null) return;
        var gm = GameManager.Instance;
        var sb = new StringBuilder();
        sb.Append(Title(Loc.T("Mazraa Greenhouse")));
        if (gm != null)
        {
            sb.Append('\n').Append(Loc.T("Harvested: {0} kg", gm.TotalHarvestKg.ToString("F0")));
            for (int i = 0; i < gm.Greenhouses.Count; i++)
            {
                var gh = gm.Greenhouses[i];
                if (gh == null) continue;
                string label = gh.HasCustomName ? gh.DisplayName : Loc.Raw("Greenhouse") + " " + (i + 1);
                int healthy = gh.HealthyCount, total = gh.Cropbeds.Count;
                string color = healthy == total ? "#8CD95A" : "#F2B84B";
                sb.Append('\n').Append("<color=").Append(color).Append('>')
                  .Append(Loc.T("{0}: {1}/{2} beds healthy", label, healthy, total))
                  .Append("</color>");
            }
        }
        statusText.text = sb.ToString();
    }

    private void ShowCropInfo(Cropbed bed)
    {
        infoBed = bed;
        if (infoPanel == null) return;
        if (bed == null) { infoPanel.gameObject.SetActive(false); return; }

        var sb = new StringBuilder();
        var p = bed.Crop;
        sb.Append(Title(Loc.Display(p != null ? Loc.CropName(p) : bed.name)));
        sb.Append('\n').Append(bed.StatusText());
        if (p != null)
        {
            sb.Append("\n\n").Append(Title(Loc.T("Ideal growing conditions")));
            Row(sb, "Day temp", p.dayTempC, " C");
            Row(sb, "Night temp", p.nightTempC, " C");
            Row(sb, "Humidity", p.humidity, "");
            Row(sb, "EC", p.ec, " mS/cm");
            Row(sb, "pH", p.ph, "");
            Row(sb, "Water / plant", p.waterPerPlant, "/day");
            Row(sb, "Crop cycle", p.cycleDays, " d");
            sb.Append("\n<size=80%><i>").Append(Loc.T("Reference ranges - confirm with your agronomist.")).Append("</i></size>");
        }
        infoText.text = sb.ToString();
        infoPanel.gameObject.SetActive(true);
    }

    private static void Row(StringBuilder sb, string label, string value, string unit)
    {
        if (string.IsNullOrEmpty(value)) return;
        // Build the logical line first, then shape once so Arabic order stays correct.
        sb.Append('\n').Append(Loc.Display(Loc.Raw(label) + ": " + value + unit));
    }
}
