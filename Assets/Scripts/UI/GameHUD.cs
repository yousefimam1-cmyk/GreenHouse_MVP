using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    private RectTransform statusPanel, infoPanel, hintPanel, helpPanel, trainingPanel, partnerPanel, truckPanel, pausePanel;
    private TextMeshProUGUI statusText, infoText, hintText, helpText, trainingText, partnerText, truckText, pauseText;
    private RectTransform arrow, pricePanel;
    private TextMeshProUGUI priceText;
    private bool paused;
    private GreenhouseManager partnerGreenhouse;
    private SponsorFile sponsors;
    private Cropbed infoBed;
    private float refreshTimer;

    private void Start()
    {
        BuildCanvas();
        Loc.OnLanguageChanged += RefreshAll;
        if (GameManager.Instance != null) GameManager.Instance.OnStatsChanged += RefreshStatus;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OnCropbedPanelChanged += ShowCropInfo;
            UIManager.Instance.OnControlStationPanelChanged += ShowPartners;
        }
        TextAsset sj = Resources.Load<TextAsset>("Data/sponsors");
        if (sj != null) { try { sponsors = JsonUtility.FromJson<SponsorFile>(sj.text); } catch { sponsors = null; } }
        RefreshAll();
    }

    private void OnDestroy()
    {
        Loc.OnLanguageChanged -= RefreshAll;
        if (GameManager.Instance != null) GameManager.Instance.OnStatsChanged -= RefreshStatus;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OnCropbedPanelChanged -= ShowCropInfo;
            UIManager.Instance.OnControlStationPanelChanged -= ShowPartners;
        }
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.25f;
            RefreshTraining();
            RefreshStatus();
            RefreshTruck();
            RefreshPrices();
            if (infoBed != null) ShowCropInfo(infoBed);
        }
    }

    private void LateUpdate()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) SetPaused(!paused);
        if (kb != null && kb.mKey.wasPressedThisFrame && pricePanel != null) { pricePanel.gameObject.SetActive(!pricePanel.gameObject.activeSelf); RefreshPrices(); }
        if (paused && kb != null && kb.backspaceKey.wasPressedThisFrame) NewGame();
        UpdateArrow();
    }

    private void SetPaused(bool value)
    {
        paused = value;
        Time.timeScale = paused ? 0f : 1f;
        if (pausePanel != null) pausePanel.gameObject.SetActive(paused);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(paused);
        RefreshAll();
    }

    private void NewGame()
    {
        SaveData.DeleteAll();
        if (EconomyManager.Instance != null) EconomyManager.Instance.ResetAll();
        if (MissionManager.Instance != null) MissionManager.Instance.ResetChain();
        if (GameManager.Instance != null) GameManager.Instance.ResetProgress();
        if (WorldClock.Instance != null) WorldClock.Instance.ResetCalendar();
        if (FieldManager.Instance != null) FieldManager.Instance.ResetAll();
        if (MarketManager.Instance != null) MarketManager.Instance.ResetMarket();
        if (AnimalPen.Instance != null) AnimalPen.Instance.ResetPen();
        if (FarmTruck.Instance != null) FarmTruck.Instance.ClearCargo();
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) v.Refuel(v.fuelCapacity);
        SetPaused(false);
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("New game started"));
    }

    private void OnDisable() { if (paused) Time.timeScale = 1f; }

    // Points the HUD arrow at the next stop: loading bay when empty, market when loaded.
    private void UpdateArrow()
    {
        var v = Vehicle.Current;
        if (arrow == null || v == null) return;
        Vector3 targetPos;
        if (v is FarmTruck truck) { var z = TruckTarget(truck); if (z == null) return; targetPos = z.transform.position; }
        else if (v.Fuel < v.fuelCapacity * 0.2f && DeliveryZone.LoadingBay != null) targetPos = DeliveryZone.LoadingBay.transform.position;
        else if (FieldManager.Instance != null && FieldManager.Instance.Plots.Count > 0) targetPos = FieldManager.Instance.Plots[0].transform.position;
        else return;
        Vector3 to = targetPos - v.transform.position; to.y = 0f;
        float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up), to, Vector3.up);
        arrow.localRotation = Quaternion.Euler(0, 0, -angle);
    }

    private static DeliveryZone TruckTarget(FarmTruck truck)
    {
        var eco = EconomyManager.Instance;
        bool loaded = truck.CargoWeight > 0.5f && (eco == null || truck.CargoWeight >= eco.TruckCapacity * 0.98f || eco.StorageWeight < 0.5f);
        return loaded ? DeliveryZone.Market : DeliveryZone.LoadingBay;
    }

    private void RefreshPrices()
    {
        if (pricePanel == null || !pricePanel.gameObject.activeSelf) return;
        var eco = EconomyManager.Instance; var market = MarketManager.Instance;
        if (eco == null) return;
        Loc.ApplyFont(priceText);
        priceText.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        var sb = new StringBuilder();
        sb.Append(Title(Loc.T("Market prices today")));
        foreach (var g in eco.Data.goods)
        {
            float p = market != null ? market.Price(g.id) : g.price;
            string arrowColor = p > g.price * 1.05f ? "#8CD95A" : p < g.price * 0.95f ? "#FF7B6B" : "#DDDDDD";
            sb.Append('\n').Append("<color=").Append(arrowColor).Append('>')
              .Append(Loc.Display(EconomyManager.GoodName(g) + ": " + p.ToString("F1") + " " + Loc.Raw("SAR") + " / " + EconomyManager.Unit(g)))
              .Append("</color>");
        }
        if (market != null && market.Contracts.Count > 0)
        {
            sb.Append("\n\n").Append(Title(Loc.T("Contracts")));
            foreach (var c in market.Contracts)
            {
                var g = eco.Good(c.good);
                sb.Append('\n').Append(Loc.Display(MarketManager.BuyerName(c) + ": " + c.delivered.ToString("F0") + "/" + c.amount.ToString("F0") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g)));
                sb.Append("\n<size=80%><color=#BBBBBB>").Append(Loc.Display("+" + Mathf.RoundToInt((c.bonus - 1f) * 100f) + "% - " + Loc.Raw("due day") + " " + c.dueDay)).Append("</color></size>");
            }
        }
        sb.Append("\n<size=80%>").Append(Loc.T("[M] close")).Append("</size>");
        priceText.text = sb.ToString();
    }

    private static string FuelLine(Vehicle v)
    {
        float pct = v.Fuel / Mathf.Max(1f, v.fuelCapacity);
        string c = pct < 0.15f ? "#FF6B5B" : pct < 0.35f ? "#F2B84B" : "#DDDDDD";
        return "<color=" + c + ">" + Loc.T("Fuel {0}%", Mathf.RoundToInt(pct * 100f)) + (pct <= 0.001f ? " - " + Loc.T("Out of fuel - press R to tow (150 SAR)") : "") + "</color>";
    }

    private void RefreshTruck()
    {
        var v = Vehicle.Current;
        if (truckPanel == null) return;
        truckPanel.gameObject.SetActive(v != null);
        if (v == null) return;
        if (v is Tractor tractor)
        {
            var tb = new StringBuilder();
            tb.Append(Title(Loc.T("{0} km/h", Mathf.Abs(tractor.Speed * 3.6f).ToString("F0"))));
            tb.Append('\n').Append(Loc.Display(Loc.Raw("Implement") + ": " + Tractor.ImplementName(tractor.ActiveImplement)));
            tb.Append('\n').Append(FuelLine(tractor));
            tb.Append("\n<size=80%>").Append(Loc.T("[Q] Change implement   [E] Exit   [R] Reset")).Append("</size>");
            truckText.text = tb.ToString();
            return;
        }
        var truck = (FarmTruck)v;
        var eco = EconomyManager.Instance;
        var target = TruckTarget(truck);
        float dist = target != null ? Vector3.Distance(target.transform.position, truck.transform.position) : 0f;
        string where = target == DeliveryZone.Market ? Loc.Raw("Market") : Loc.Raw("Loading bay");
        var sb = new StringBuilder();
        sb.Append(Title(Loc.T("{0} km/h", Mathf.Abs(truck.Speed * 3.6f).ToString("F0"))));
        sb.Append('\n').Append(Loc.T("Cargo {0}/{1} kg", truck.CargoWeight.ToString("F0"), (eco != null ? eco.TruckCapacity : 0f).ToString("F0")));
        if (target != null) sb.Append('\n').Append(Loc.Display(where + ": " + dist.ToString("F0") + " m"));
        sb.Append('\n').Append(FuelLine(truck));
        sb.Append("\n<size=80%>").Append(Loc.T("[E] Exit   [Space] Brake   [R] Reset truck")).Append("</size>");
        truckText.text = sb.ToString();
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
        trainingPanel = MakePanel("Training", new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(520, 0), out trainingText, 22);
        partnerPanel = MakePanel("Partners", new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(460, 0), out partnerText, 20);
        truckPanel = MakePanel("Truck", new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(420, 0), out truckText, 22);
        truckText.alignment = TextAlignmentOptions.Top;
        // Direction arrow (two bars forming a chevron) above the truck panel.
        var holder = new GameObject("Arrow", typeof(RectTransform));
        holder.transform.SetParent(canvas.transform, false);
        arrow = (RectTransform)holder.transform;
        arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0); arrow.anchoredPosition = new Vector2(0, 230); arrow.sizeDelta = new Vector2(80, 80);
        foreach (float a in new[] { 40f, -40f })
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(arrow, false);
            var rt = (RectTransform)bar.transform;
            rt.sizeDelta = new Vector2(12, 46);
            rt.anchoredPosition = new Vector2(a > 0 ? -14 : 14, 0);
            rt.localRotation = Quaternion.Euler(0, 0, a);
            var img = bar.GetComponent<Image>(); img.color = new Color(1f, 0.6f, 0.15f, 0.95f); img.raycastTarget = false;
        }
        arrow.SetParent(truckPanel, true);
        holder.AddComponent<LayoutElement>().ignoreLayout = true;
        pricePanel = MakePanel("Prices", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(440, 0), out priceText, 20);
        pricePanel.gameObject.SetActive(false);
        pausePanel = MakePanel("Pause", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 0), out pauseText, 30);
        pauseText.alignment = TextAlignmentOptions.Top;
        pausePanel.gameObject.SetActive(false);
        truckPanel.gameObject.SetActive(false);
        infoPanel.gameObject.SetActive(false);
        helpPanel.gameObject.SetActive(false);
        partnerPanel.gameObject.SetActive(false);
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
        Loc.ExcludeFromScene(text);
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
        Mirror(partnerPanel, !rtl);
        foreach (var t in new[] { statusText, infoText, hintText, helpText, partnerText })
        {
            Loc.ApplyFont(t);
            t.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }
        Loc.ApplyFont(trainingText);
        Loc.ApplyFont(truckText);
        Loc.ApplyFont(pauseText);
        trainingText.alignment = TextAlignmentOptions.Top;
        hintText.text = Loc.T("[H] Help   [L] Arabic   [T] Training") + "\n" + Loc.T("[G] Graphics   [P] Pause   [M] Prices");
        pauseText.text = Title(Loc.T("Paused")) + "\n" + Loc.T("[P] Resume") + "\n<size=75%>" + Loc.T("[Backspace] New game (clears saved progress)") + "</size>";
        helpText.text = Title(Loc.T("HELP")) + "\n" + Loc.T("HELP_BODY");
        RefreshStatus();
        RefreshTraining();
        if (infoBed != null) ShowCropInfo(infoBed);
        if (partnerGreenhouse != null) ShowPartners(partnerGreenhouse);
    }

    private void RefreshTraining()
    {
        var sm = ScenarioManager.Instance;
        if (trainingPanel == null) return;
        if (sm == null || !sm.HasScenarios) { trainingPanel.gameObject.SetActive(false); return; }
        var d = sm.Selected;
        var sb = new StringBuilder();
        sb.Append(Title(Loc.Display(Loc.Raw("Training") + ": " + ScenarioManager.Title(d))));
        if (sm.Running)
        {
            sb.Append('\n').Append(sm.ProgressText());
            sb.Append("\n<size=85%>").Append(Loc.T("[T] Stop training")).Append("</size>");
        }
        else
        {
            sb.Append('\n').Append(Loc.Display(ScenarioManager.Brief(d)));
            float best = sm.BestTime(d);
            if (best >= 0f) sb.Append('\n').Append(Loc.T("Best: {0}s", best.ToString("F0")));
            sb.Append("\n<size=85%>").Append(Loc.T("[T] Start   [N] Next scenario")).Append("</size>");
        }
        trainingText.text = sb.ToString();
        // Show while running, or briefly as a menu when nothing else is open.
        bool uiOpen = UIManager.Instance != null && UIManager.Instance.IsUIOpened;
        trainingPanel.gameObject.SetActive(sm.Running || !uiOpen);
    }

    private void ShowPartners(GreenhouseManager gh)
    {
        partnerGreenhouse = gh;
        if (partnerPanel == null) return;
        if (gh == null || sponsors?.slots == null || sponsors.slots.Length == 0) { partnerPanel.gameObject.SetActive(false); return; }
        var sb = new StringBuilder();
        sb.Append(Title(Loc.T("Partners")));
        foreach (var s in sponsors.slots)
        {
            bool ar = Loc.IsArabic;
            string module = Loc.Raw(ModuleLabel(s.module));
            string name = ar && !string.IsNullOrEmpty(s.nameAr) ? s.nameAr : s.nameEn;
            string tag = ar && !string.IsNullOrEmpty(s.taglineAr) ? s.taglineAr : s.taglineEn;
            sb.Append('\n').Append(Loc.Display(module + ": " + name));
            if (!string.IsNullOrEmpty(tag)) sb.Append("\n<size=80%><color=#BBBBBB>").Append(Loc.Display(tag)).Append("</color></size>");
        }
        partnerText.text = sb.ToString();
        partnerPanel.gameObject.SetActive(true);
    }

    private static string ModuleLabel(string module)
    {
        switch (module)
        {
            case "Irrigation": return "Water";
            case "Nutrient": return "Nutrient";
            case "Energy": return "Energy";
            case "ClimateControl": return "Climate Control";
            default: return module ?? "";
        }
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
        var clock = WorldClock.Instance;
        if (clock != null)
            sb.Append('\n').Append("<color=#9FD3FF>").Append(Loc.Display(Loc.Raw("Day") + " " + clock.Day + " " + clock.TimeText() + " - " + clock.SeasonName() + " " + clock.DayOfSeason + "/" + clock.daysPerSeason + " - " + clock.WeatherName())).Append("</color>");
        if (gm != null)
        {
            sb.Append('\n').Append(Loc.T("Harvested: {0} kg", gm.TotalHarvestKg.ToString("F0")));
            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                sb.Append('\n').Append("<color=#FFC24D>").Append(Loc.T("Money: {0} SAR", eco.Money.ToString("N0"))).Append("</color>");
                sb.Append('\n').Append(Loc.T("Storage: {0} kg   Feed: {1}", eco.StorageWeight.ToString("F0"), eco.Feed.ToString("F1")));
                var pen = AnimalPen.Instance;
                if (pen != null && pen.AnimalCount > 0) sb.Append('\n').Append("<size=85%>").Append(pen.StatusLine()).Append("</size>");
            }
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
        var mm = MissionManager.Instance;
        if (mm != null)
        {
            sb.Append("\n\n");
            if (mm.AllDone) sb.Append(Title(Loc.T("All missions complete - free play")));
            else
            {
                var m = mm.Current;
                sb.Append(Title(Loc.Display(Loc.Raw("Mission") + " " + (mm.Index + 1) + "/" + mm.Missions.Length + ": " + MissionManager.Title(m))));
                sb.Append('\n').Append(Loc.Display(MissionManager.Desc(m)));
                if (m.target > 1f) sb.Append('\n').Append(Loc.Display(Mathf.Min(mm.Progress, m.target).ToString("F0") + " / " + m.target.ToString("F0")));
                sb.Append("\n<size=85%><color=#FFC24D>").Append(Loc.Display(Loc.Raw("Reward") + ": " + m.reward.ToString("N0") + " " + Loc.Raw("SAR"))).Append("</color></size>");
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

[System.Serializable]
public class SponsorSlot { public string module, nameEn, nameAr, taglineEn, taglineAr; }

[System.Serializable]
public class SponsorFile { public SponsorSlot[] slots; }
