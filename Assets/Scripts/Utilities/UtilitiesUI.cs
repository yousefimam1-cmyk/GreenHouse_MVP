using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Utilities panel (key U, built in code like ShopUI): power and water supply vs demand, every source,
/// today's use against the daily water limit, the running cost, and warnings. U / Esc / Tab closes.
/// </summary>
public class UtilitiesUI : MonoBehaviour
{
    public static UtilitiesUI Instance;
    public bool IsOpen { get; private set; }

    private Canvas canvas;
    private TextMeshProUGUI body;
    private int openedFrame;
    private float refreshTimer;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; Loc.OnLanguageChanged -= Refresh; }

    private void Start()
    {
        Build();
        Loc.OnLanguageChanged += Refresh;
    }

    private void Build()
    {
        var go = new GameObject("Utilities UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 61;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGo.transform.SetParent(canvas.transform, false);
        var panel = (RectTransform)panelGo.transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(860, 0);
        panelGo.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.08f, 0.94f);
        var vl = panelGo.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(28, 28, 22, 22); vl.spacing = 8;
        vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        tgo.transform.SetParent(panel, false);
        body = tgo.GetComponent<TextMeshProUGUI>();
        body.fontSize = 22; body.color = Color.white; body.richText = true; body.raycastTarget = false;
        body.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(body);
        canvas.gameObject.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen || canvas == null) return;
        IsOpen = true;
        openedFrame = Time.frameCount;
        canvas.gameObject.SetActive(true);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        canvas.gameObject.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && Time.timeScale > 0f)
        {
            bool shopOpen = ShopUI.Instance != null && ShopUI.Instance.IsOpen;
            if (kb.uKey.wasPressedThisFrame && !shopOpen && !NetSession.Typing && Time.frameCount != openedFrame) Toggle();
            else if (IsOpen && Time.frameCount != openedFrame && (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) Close();
        }
        if (!IsOpen) return;
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 0.5f; Refresh(); }
    }

    private static string Head(string s) => "<color=#F2A93B><b>" + s + "</b></color>";
    private static string Bad(string s) => "<color=#FF6A4D>" + s + "</color>";
    private static string Ok(string s) => "<color=#8CE08C>" + s + "</color>";
    private static string F(float v) => v.ToString("F1");

    /// <summary>Panel text (translated + shaped), also used by tests.</summary>
    public static string BuildText()
    {
        var g = UtilityGrid.Instance;
        if (g == null) return "";
        var m = g.Model;
        var sb = new StringBuilder();
        sb.Append("<size=130%>").Append(Head(Loc.T("Utilities - Power & Water"))).Append("</size>\n");

        // Power
        string pLine = Loc.T("Power: demand {0} kW, supplied {1} kW (capacity {2} kW)", F(m.PowerDemand), F(m.PowerServed), m.PowerCapacity.ToString("F0"));
        sb.Append('\n').Append(Head(m.PowerShort ? Bad(pLine) : Ok(pLine)));
        sb.Append('\n').Append("  " + Loc.T("SEC grid: {0} of {1} kW", F(m.GridOut), UtilityModel.GridKw[Mathf.Clamp(m.GridLevel, 0, 2)].ToString("F0")));
        if (m.Solar > 0) sb.Append('\n').Append("  " + Loc.T("Solar ({0} x 10 kWp): {1} kW, dust {2}%", m.Solar, F(m.SolarOut), Mathf.RoundToInt(m.Dust * 100f)));
        if (m.Wind > 0) sb.Append('\n').Append("  " + Loc.T("Wind ({0} x 5 kW): {1} kW", m.Wind, F(m.WindOut)));
        if (m.Biogas) sb.Append('\n').Append("  " + Loc.T("Biogas: {0} kW (manure {1} kg)", F(m.BiogasOut), (EconomyManager.Instance != null ? EconomyManager.Instance.Stored("manure") : 0f).ToString("F0")));
        if (m.Generator) sb.Append('\n').Append("  " + Loc.T("Diesel generator: {0} kW (fuel {1} L)", F(m.DieselOut), m.DieselL.ToString("F0")));
        sb.Append('\n').Append("  " + Loc.T("Uses: greenhouse cooling {0} kW, pumps {1} kW, irrigation {2} kW, farm {3} kW", F(m.CoolingKw), F(m.PumpKw), F(m.IrrigationKw), F(m.BaseKw)));

        // Water
        string wLine = Loc.T("Water: demand {0} m3/day, supply {1} m3/day, stored {2}/{3} m3", F(m.WaterDemand), F(m.WaterSupply), m.Stored.ToString("F0"), m.StorageCapacity.ToString("F0"));
        sb.Append("\n\n").Append(Head(m.WaterShort ? Bad(wLine) : Ok(wLine)));
        sb.Append('\n').Append(m.WellOut > 0f ? "  " + Loc.T("Farm well: {0} m3/day", F(m.WellOut)) : Bad("  " + Loc.T("Farm well: stopped - start the well")));
        if (m.DeepWell) sb.Append('\n').Append("  " + Loc.T("Deep aquifer well: {0} m3/day (needs power)", F(m.DeepOut)));
        if (m.Tse) sb.Append('\n').Append("  " + Loc.T("Treated wastewater: {0} m3/day (fodder fields only)", F(UtilityModel.TseM3)));
        sb.Append('\n').Append("  " + Loc.T("Uses: greenhouse beds {0}, fields {1}, animals {2} m3/day", F(m.GhWater), F(IrrigationSystem.Instance != null ? IrrigationSystem.Instance.WaterM3Today : 0f), F(m.AnimalWater)));

        // Today + cost
        float limit = UtilityModel.DailyWaterLimit;
        string used = Loc.T("Water used today: {0} / {1} m3 (daily limit)", m.WaterUsedToday.ToString("F1"), limit.ToString("F0"));
        sb.Append("\n\n").Append(m.WaterUsedToday >= limit ? Bad(used) : used);
        sb.Append('\n').Append(Loc.T("Bill so far today: {0} SAR   Daily cost now: about {1} SAR", Mathf.RoundToInt(m.BillSoFar()), Mathf.RoundToInt(m.DailyCostEstimate())));
        sb.Append('\n').Append(Loc.T("Days in a row with enough power: {0}", m.PowerOkDays));

        // Warnings
        if (m.PowerShort) sb.Append('\n').Append(Bad(Loc.T("! Not enough power: greenhouse cooling pads slow down and beds stop growing")));
        if (m.WaterShort) sb.Append('\n').Append(Bad(Loc.T("! Not enough water: greenhouse beds slow down and fields dry out faster")));
        if (m.Dust >= 0.3f) sb.Append('\n').Append(Bad(Loc.T("! Solar panels are dusty - clean them with E")));
        if (m.Generator && m.DieselL < 50f) sb.Append('\n').Append(Bad(Loc.T("! Generator fuel is low")));
        if (m.WaterUsedToday >= limit * 0.8f) sb.Append('\n').Append(Bad(Loc.T("! Save water: drip irrigation uses about half the water")));

        sb.Append("\n\n<color=#BBBBBB>").Append(Loc.T("Buy equipment at a Farm Shop (Power and Water pages)   [U] / [Esc] close")).Append("</color>");
        return sb.ToString();
    }

    private void Refresh()
    {
        if (body == null || !IsOpen) return;
        Loc.ApplyFont(body);
        body.text = BuildText();
        body.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
    }
}
