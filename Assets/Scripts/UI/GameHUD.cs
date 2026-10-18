using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-screen HUD built entirely from code (no scene edits needed), laid out like Farming Simulator's HUD:
/// top-right clock block (time, day / season, weather + temperature, money) with the farm status / mission and training
/// panels stacked under it; top-left context help box (the keys for what you are doing now - ContextHelp); bottom-right
/// vehicle gauge cluster (speed, gear / RPM, fuel / damage / load bars, cruise, implement state); the minimap with field
/// numbers sits bottom-left (Minimap). Crop reference info shows while a crop bed panel is open; H toggles the help overlay.
/// Everything mirrors left / right when Arabic is active. Key hints follow the device in use (Glyphs).
/// Pausing is done by GameMenu (P / Start); NewGame() is called from its menus.
/// </summary>
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    private static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.05f, 0.72f);
    private static readonly Color Accent = new Color(0.55f, 0.85f, 0.35f, 1f);

    private Canvas canvas;
    private RectTransform statusPanel, infoPanel, hintPanel, helpPanel, trainingPanel, partnerPanel, truckPanel;
    private TextMeshProUGUI statusText, infoText, hintText, helpText, trainingText, partnerText, truckText;
    private RectTransform arrow, pricePanel, clockPanel;
    private TextMeshProUGUI priceText, clockText, gaugeDetail;
    private RectTransform fuelFill, damageFill, loadFill, rpmFill;
    private TextMeshProUGUI fuelLabel, damageLabel, loadLabel, rpmLabel;
    private GameObject loadRow;
    private int glyphVersion = -1;
    private GreenhouseManager partnerGreenhouse;
    private SponsorFile sponsors;
    private Cropbed infoBed;
    private float refreshTimer;

    private void Awake() => Instance = this;

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
        if (Instance == this) Instance = null;
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
            RefreshClock();
            RefreshContext();
            RefreshTruck();
            RefreshPrices();
            if (infoBed != null) ShowCropInfo(infoBed);
            if (glyphVersion != GameInput.Version) RefreshAll();   // keyboard <-> gamepad, rebound keys
        }
    }

    /// <summary>Settings > Game > Help hints: show or hide the key hints box.</summary>
    public void SetHintsVisible(bool on) { if (hintPanel != null && hintPanel.gameObject.activeSelf != on) hintPanel.gameObject.SetActive(on); }
    public bool HintsVisible => hintPanel != null && hintPanel.gameObject.activeSelf;

    private void LateUpdate()
    {
        var kb = Keyboard.current;
        if (NetSession.Typing) kb = null;   // typing a co-op chat line / name
        if (kb != null && kb.mKey.wasPressedThisFrame && pricePanel != null && Time.timeScale > 0f) { pricePanel.gameObject.SetActive(!pricePanel.gameObject.activeSelf); RefreshPrices(); }
        UpdateArrow();
        StackRightColumn();
    }

    /// <summary>Clock block, status and training panels stacked down the right edge (left edge in Arabic).</summary>
    private void StackRightColumn()
    {
        if (clockPanel == null) return;
        float y = -24f;
        foreach (var rt in new[] { clockPanel, statusPanel, trainingPanel })
        {
            if (rt == null || !rt.gameObject.activeSelf) continue;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
            y -= rt.rect.height + 10f;
        }
    }

    /// <summary>Wipe the save and reset every system in place (main menu / pause menu "New game").</summary>
    public void StartNewGame() => NewGame();

    private void NewGame()
    {
        SaveData.DeleteAll();
        if (EconomyManager.Instance != null) EconomyManager.Instance.ResetAll();
        if (MissionManager.Instance != null) MissionManager.Instance.ResetChain();
        if (GameManager.Instance != null) GameManager.Instance.ResetProgress();
        if (WorldClock.Instance != null) WorldClock.Instance.ResetCalendar();
        // FarmCore fields reset themselves on GameEvents.NewGameStarted (FarmFields.OnNewGame)
        if (MarketManager.Instance != null) MarketManager.Instance.ResetMarket();
        if (AnimalPen.Instance != null) AnimalPen.Instance.ResetPen();
        if (FarmTruck.Instance != null) FarmTruck.Instance.ClearCargo();
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) v.Refuel(v.fuelCapacity);
        GameEvents.RaiseNewGameStarted();
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("New game started"));
    }

    // Points the HUD arrow at the next stop: loading bay when empty, market when loaded.
    private void UpdateArrow()
    {
        var v = Vehicle.Current;
        if (arrow == null || v == null) return;
        Vector3 targetPos;
        if (v is FarmTruck truck) { var z = TruckTarget(truck); if (z == null) return; targetPos = z.transform.position; }
        else if (v.Fuel < v.fuelCapacity * 0.2f && DeliveryZone.LoadingBay != null) targetPos = DeliveryZone.LoadingBay.transform.position;
        else if (FarmFields.Instance != null && FarmFields.Instance.Fields.Count > 0) targetPos = FarmFields.Instance.Fields[0].center;
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
                string what = c.Field() ? Loc.Raw(c.task) : EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g);
                sb.Append('\n').Append(Loc.Display(MarketManager.BuyerName(c) + ": " + c.delivered.ToString("F0") + "/" + c.amount.ToString("F0") + " " + what));
                string pay = c.unitPrice > 0f ? c.unitPrice.ToString("F1") + " " + Loc.Raw("SAR") : c.Field() ? c.reward.ToString("N0") + " " + Loc.Raw("SAR") : "+" + Mathf.RoundToInt((c.bonus - 1f) * 100f) + "%";
                sb.Append("\n<size=80%><color=#BBBBBB>").Append(Loc.Display(pay + " - " + Loc.Raw("due day") + " " + c.dueDay)).Append("</color></size>");
            }
        }
        sb.Append("\n<size=80%>").Append(Loc.T("[M] close")).Append(" · ").Append(Loc.T("Pause menu: Market prices, Contracts, Production, Finances")).Append("</size>");
        priceText.text = sb.ToString();
    }

    private static string FuelLine(Vehicle v)
    {
        float pct = v.Fuel / Mathf.Max(1f, v.fuelCapacity);
        string c = pct < 0.15f ? "#FF6B5B" : pct < 0.35f ? "#F2B84B" : "#DDDDDD";
        return "<color=" + c + ">" + Loc.T("Fuel {0}%", Mathf.RoundToInt(pct * 100f)) + (pct <= 0.001f ? " - " + Loc.T("Out of fuel - press R to tow (150 SAR)") : "") + "</color>";
    }

    private static readonly List<string> hudFlags = new List<string>();

    /// <summary>Drivetrain, traction, assists and condition lines of the vehicle panel (plus the driving key hints).</summary>
    public static void DriveLines(Vehicle v, StringBuilder sb)
    {
        var d = v.Drive;
        if (!d.EngineOn) sb.Append('\n').Append("<color=#F2B84B>").Append(Glyphs.Apply(Loc.T("Engine off - press [F] to start"))).Append("</color>");
        else sb.Append('\n').Append(Loc.T("Gear {0}   {1} rpm", d.GearLabel, Mathf.RoundToInt(d.Rpm / 10f) * 10));
        int slip = Mathf.RoundToInt(d.Slip * 100f);
        string slipColor = slip >= 25 ? "#FF6B5B" : slip >= 12 ? "#F2B84B" : "#DDDDDD";
        sb.Append('\n').Append("<color=").Append(slipColor).Append('>')
          .Append(Loc.T("Slip {0}%   {1}", slip, DriveSurfaces.Name(d.Surface, d.Wet))).Append("</color>");
        var flags = hudFlags; flags.Clear();
        if (d.FourWD) flags.Add(Loc.Raw("4WD"));
        if (d.DiffLock) flags.Add(Loc.Raw("Diff lock"));
        if (d.LowPressure) flags.Add(Loc.Raw("Low tyre pressure"));
        if (v.Assist.Cruise == CruiseMode.Cruise) flags.Add(string.Format(Loc.Raw("Cruise {0} km/h"), Vehicle.Kmh(v.Assist.CruiseSpeed)));
        if (v.Assist.Cruise == CruiseMode.Limiter) flags.Add(string.Format(Loc.Raw("Limiter {0} km/h"), Vehicle.Kmh(v.Assist.CruiseSpeed)));
        if (v.Assist.LaneAssist) flags.Add(Loc.Raw("GPS line"));
        if (d.WorkSpeedLimit > 0f) flags.Add(string.Format(Loc.Raw("Working max {0} km/h"), Vehicle.Kmh(d.WorkSpeedLimit)));
        if (flags.Count > 0) sb.Append('\n').Append("<color=#8CD95A>").Append(Loc.Display(string.Join("  ·  ", flags))).Append("</color>");
        int eng = Mathf.RoundToInt(v.EngineCondition * 100f), tyre = Mathf.RoundToInt(v.TyreCondition * 100f);
        string cond = Mathf.Min(eng, tyre) < 50 ? "#FF6B5B" : Mathf.Min(eng, tyre) < 80 ? "#F2B84B" : "#BBBBBB";
        sb.Append('\n').Append("<color=").Append(cond).Append('>').Append(Loc.T("Engine {0}%   Tyres {1}%   {2} t", eng, tyre, (v.TotalMass / 1000f).ToString("F1"))).Append("</color>");
        if (v.AtWorkshop && v.NeedsRepair) sb.Append('\n').Append(Glyphs.Apply(Loc.T("[R] Repair for {0} SAR", v.RepairCost.ToString("N0"))));
        sb.Append("\n<size=75%>").Append(Glyphs.Apply(Loc.T("[F] Engine  [Shift]/[Ctrl] Gear  [Y] Cruise [Minus]/[Plus]  [Z] 4WD / diff lock  [Slash] GPS"))).Append("</size>");
    }

    private void RefreshTruck()
    {
        var v = Vehicle.Current;
        if (truckPanel == null) return;
        truckPanel.gameObject.SetActive(v != null);
        if (v == null) return;
        RefreshGauges(v);
        if (v is Tractor tractor)
        {
            var tb = new StringBuilder();
            tb.Append(SpeedLine(tractor));
            tb.Append('\n').Append(Loc.Display(Loc.Raw("Implement") + ": " + Tractor.ImplementName(tractor.ActiveImplement) + ImplementState(tractor)));
            tb.Append('\n').Append(FuelLine(tractor));
            truckText.text = tb.ToString();
            var db = new StringBuilder();
            DriveLines(tractor, db);
            gaugeDetail.text = db.ToString().TrimStart('\n');
            return;
        }
        var truck = (FarmTruck)v;
        var eco = EconomyManager.Instance;
        var target = TruckTarget(truck);
        float dist = target != null ? Vector3.Distance(target.transform.position, truck.transform.position) : 0f;
        string where = target == DeliveryZone.Market ? Loc.Raw("Market") : Loc.Raw("Loading bay");
        var sb = new StringBuilder();
        sb.Append(SpeedLine(truck));
        sb.Append('\n').Append(Loc.T("Cargo {0}/{1} kg", truck.CargoWeight.ToString("F0"), (eco != null ? eco.TruckCapacity : 0f).ToString("F0")));
        if (target != null) sb.Append('\n').Append(Loc.Display(where + ": " + dist.ToString("F0") + " m"));
        sb.Append('\n').Append(FuelLine(truck));
        truckText.text = sb.ToString();
        var detail = new StringBuilder();
        DriveLines(truck, detail);
        gaugeDetail.text = detail.ToString().TrimStart('\n');
    }

    // ---------------------------------------------------------------- FS-style gauge cluster + clock block + context help

    /// <summary>Big speed readout with the cruise / limiter setting beside it.</summary>
    public static string SpeedLine(Vehicle v)
    {
        string kmh = "<size=170%><b>" + Vehicle.Kmh(v.Speed) + "</b></size> " + Loc.T("km/h");
        if (v.Assist.Cruise == CruiseMode.Cruise) kmh += "   <color=#8CD95A>" + Loc.T("Cruise {0} km/h", Vehicle.Kmh(v.Assist.CruiseSpeed)) + "</color>";
        else if (v.Assist.Cruise == CruiseMode.Limiter) kmh += "   <color=#8CD95A>" + Loc.T("Limiter {0} km/h", Vehicle.Kmh(v.Assist.CruiseSpeed)) + "</color>";
        return kmh;
    }

    /// <summary>" (lowered)", " (raised, folded)" ... for the attached implement (translated, unshaped).</summary>
    public static string ImplementState(Tractor t)
    {
        if (t == null || t.ActiveImplement == Implement.None) return "";
        string s = Loc.Raw(t.ImplementLowered ? "lowered" : "raised");
        if (t.ImplementFolded) s += ", " + Loc.Raw("folded");
        if (t.Working) s += ", " + Loc.Raw("working");
        return " (" + s + ")";
    }

    /// <summary>Gauge values 0..1 shown by the bars (tests read them too).</summary>
    public static float FuelFraction(Vehicle v) => v == null ? 0f : Mathf.Clamp01(v.Fuel / Mathf.Max(1f, v.fuelCapacity));
    public static float DamageFraction(Vehicle v) => v == null ? 0f : Mathf.Clamp01(1f - Mathf.Min(v.EngineCondition, v.TyreCondition));
    public static float RpmFraction(Vehicle v) => v == null || !v.Drive.EngineOn ? 0f : Mathf.Clamp01(v.Drive.Rpm / 2400f);

    private void RefreshGauges(Vehicle v)
    {
        if (fuelFill == null) return;
        float fuel = FuelFraction(v), dmg = DamageFraction(v), rpm = RpmFraction(v);
        SetBar(fuelFill, fuel, fuel < 0.15f ? new Color(1f, 0.42f, 0.36f) : fuel < 0.35f ? new Color(0.95f, 0.72f, 0.3f) : new Color(0.55f, 0.85f, 0.35f));
        SetBar(damageFill, dmg, dmg > 0.5f ? new Color(1f, 0.42f, 0.36f) : dmg > 0.2f ? new Color(0.95f, 0.72f, 0.3f) : new Color(0.6f, 0.6f, 0.6f));
        SetBar(rpmFill, rpm, rpm > 0.9f ? new Color(1f, 0.42f, 0.36f) : new Color(0.62f, 0.83f, 1f));
        fuelLabel.text = Loc.T("Fuel {0}%", Mathf.RoundToInt(fuel * 100f));
        damageLabel.text = Loc.Display(Loc.Raw("Damage") + " " + Mathf.RoundToInt(dmg * 100f) + "%");
        rpmLabel.text = v.Drive.EngineOn ? Loc.Display(Loc.Raw("Gear") + " " + v.Drive.GearLabel + "   " + Mathf.RoundToInt(v.Drive.Rpm / 10f) * 10 + " rpm") : Loc.T("Engine off");
        var truck = v as FarmTruck; var eco = EconomyManager.Instance;
        bool showLoad = truck != null && eco != null && eco.TruckCapacity > 0f;
        if (loadRow.activeSelf != showLoad) loadRow.SetActive(showLoad);
        if (showLoad)
        {
            float load = Mathf.Clamp01(truck.CargoWeight / eco.TruckCapacity);
            SetBar(loadFill, load, new Color(1f, 0.76f, 0.3f));
            loadLabel.text = Loc.Display(Loc.Raw("Load") + " " + Mathf.RoundToInt(load * 100f) + "%");
        }
        foreach (var l in new[] { fuelLabel, damageLabel, rpmLabel, loadLabel })
        {
            Loc.ApplyFont(l);
            l.alignment = Loc.IsArabic ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        }
    }

    private static void SetBar(RectTransform fill, float frac, Color c)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        var img = fill.GetComponent<Image>();
        if (img != null) img.color = c;
    }

    /// <summary>Clock block: 24 h time, day and season, weather with temperature, money.</summary>
    public static string ClockText()
    {
        var sb = new StringBuilder();
        var clock = WorldClock.Instance;
        if (clock != null)
        {
            var t = WorldClock.Temperature(clock.Season, clock.Weather);
            sb.Append("<size=150%><b>").Append(clock.TimeText()).Append("</b></size>");
            sb.Append('\n').Append(Loc.Display(Loc.Raw("Day") + " " + clock.Day + " - " + clock.SeasonName() + " " + clock.DayOfSeason + "/" + clock.daysPerSeason));
            sb.Append('\n').Append("<color=#9FD3FF>").Append(Loc.Display(clock.WeatherName() + "  " + t.x + " C")).Append("</color>");
        }
        var eco = EconomyManager.Instance;
        if (eco != null) sb.Append('\n').Append("<color=#FFC24D><b>").Append(Loc.Display(eco.Money.ToString("N0") + " " + Loc.Raw("SAR"))).Append("</b></color>");
        return sb.ToString();
    }

    private void RefreshClock()
    {
        if (clockText == null) return;
        Loc.ApplyFont(clockText);
        clockText.alignment = Loc.IsArabic ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
        clockText.text = ClockText();
    }

    private void RefreshContext()
    {
        if (hintText == null || !hintPanel.gameObject.activeSelf) return;
        Loc.ApplyFont(hintText);
        hintText.text = ContextHelp.Text();
    }

    /// <summary>Context help text shown now (tests).</summary>
    public string ContextText => hintText != null ? hintText.text : "";
    /// <summary>The gauge cluster is showing (in a vehicle).</summary>
    public bool GaugesVisible => truckPanel != null && truckPanel.gameObject.activeSelf;
    /// <summary>Fuel bar fill now (0..1).</summary>
    public float FuelBar => fuelFill != null ? fuelFill.anchorMax.x : 0f;
    public RectTransform ClockPanel => clockPanel;
    public RectTransform ContextPanel => hintPanel;
    public RectTransform GaugePanel => truckPanel;

    /// <summary>A labelled bar row of the gauge cluster; returns the fill (its anchorMax.x is the value).</summary>
    private RectTransform MakeBar(Transform parent, string name, out TextMeshProUGUI label, out GameObject row)
    {
        var r = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        r.transform.SetParent(parent, false);
        r.GetComponent<LayoutElement>().preferredHeight = 24f;
        var bg = r.GetComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.12f); bg.raycastTarget = false;
        var f = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        f.transform.SetParent(r.transform, false);
        var frt = (RectTransform)f.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(1f, 1f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero; frt.pivot = new Vector2(0f, 0.5f);
        var fi = f.GetComponent<Image>(); fi.color = Accent; fi.raycastTarget = false;
        var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lgo.transform.SetParent(r.transform, false);
        var lrt = (RectTransform)lgo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-8f, 0f);
        label = lgo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 17; label.color = Color.white; label.raycastTarget = false; label.alignment = TextAlignmentOptions.Left;
        Loc.ExcludeFromScene(label);
        row = r;
        return frt;
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

        clockPanel = MakePanel("Clock", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(330, 0), out clockText, 24);
        statusPanel = MakePanel("Status", new Vector2(1, 1), new Vector2(-24, -170), new Vector2(460, 0), out statusText, 21);
        infoPanel = MakePanel("CropInfo", new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(480, 0), out infoText, 22);
        hintPanel = MakePanel("Context help", new Vector2(0, 1), new Vector2(24, -24), new Vector2(430, 0), out hintText, 19);   // FS: top-left key help
        helpPanel = MakePanel("Help", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 0), out helpText, 26);
        // Below the minimap (top centre).
        trainingPanel = MakePanel("Training", new Vector2(1, 1), new Vector2(-24, -84), new Vector2(400, 0), out trainingText, 18);   // top-right, under Co-op: keeps the view clear
        partnerPanel = MakePanel("Partners", new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(460, 0), out partnerText, 20);
        truckPanel = MakePanel("Gauges", new Vector2(1, 0), new Vector2(-24, 24), new Vector2(420, 0), out truckText, 21);   // FS: bottom-right gauge cluster
        truckText.alignment = TextAlignmentOptions.TopLeft;
        truckPanel.GetComponent<VerticalLayoutGroup>().spacing = 6f;
        rpmFill = MakeBar(truckPanel, "Rpm", out rpmLabel, out _);
        fuelFill = MakeBar(truckPanel, "Fuel", out fuelLabel, out _);
        damageFill = MakeBar(truckPanel, "Damage", out damageLabel, out _);
        loadFill = MakeBar(truckPanel, "Load", out loadLabel, out loadRow);
        var dgo = new GameObject("Detail", typeof(RectTransform), typeof(TextMeshProUGUI));
        dgo.transform.SetParent(truckPanel, false);
        gaugeDetail = dgo.GetComponent<TextMeshProUGUI>();
        gaugeDetail.fontSize = 16; gaugeDetail.color = new Color(0.86f, 0.86f, 0.86f); gaugeDetail.raycastTarget = false; gaugeDetail.richText = true;
        gaugeDetail.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(gaugeDetail);
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
        arrow.SetParent(truckPanel, false);
        arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 1f); arrow.anchoredPosition = new Vector2(0, 56);
        holder.AddComponent<LayoutElement>().ignoreLayout = true;
        pricePanel = MakePanel("Prices", new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(440, 0), out priceText, 20);
        pricePanel.gameObject.SetActive(false);
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
        Mirror(statusPanel, !rtl);
        Mirror(clockPanel, !rtl);
        Mirror(trainingPanel, !rtl);
        Mirror(pricePanel, !rtl);
        Mirror(truckPanel, !rtl);
        Mirror(infoPanel, rtl);
        Mirror(hintPanel, rtl);
        Mirror(partnerPanel, !rtl);
        foreach (var t in new[] { statusText, infoText, hintText, helpText, partnerText, gaugeDetail })
        {
            Loc.ApplyFont(t);
            t.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }
        Loc.ApplyFont(trainingText);
        Loc.ApplyFont(truckText);
        truckText.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        trainingText.alignment = TextAlignmentOptions.Top;
        glyphVersion = GameInput.Version;
        hintText.text = ContextHelp.Text();
        RefreshClock();
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
            sb.Append("\n<size=85%>").Append(UiText.T("[T] Start   [B] Next scenario")).Append("</size>");
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
        // day, time, weather and money: the clock block above (FS layout)
        if (gm != null)
        {
            sb.Append('\n').Append(Loc.T("Harvested: {0} kg", gm.TotalHarvestKg.ToString("F0")));
            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                sb.Append('\n').Append(Loc.T("Storage: {0} kg   Feed: {1}", eco.StorageWeight.ToString("F0"), eco.Feed.ToString("F1")));
                var pen = AnimalPen.Instance;
                if (pen != null && pen.AnimalCount > 0) sb.Append('\n').Append("<size=85%>").Append(pen.StatusLine()).Append("</size>");
                foreach (var line in HudLines.Status)
                {
                    string l = null; try { l = line(); } catch (System.Exception e) { Debug.LogException(e); }
                    if (!string.IsNullOrEmpty(l)) sb.Append('\n').Append("<size=85%>").Append(l).Append("</size>");
                }
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
        if (mm != null && mm.Missions.Length == 0)
            sb.Append("\n\n").Append(Title(Loc.T(GameModes.Current == GameModes.Mode.Challenge ? "Challenge" : "Free play")));   // no mission chain outside the campaign
        else if (mm != null)
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
