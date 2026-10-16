using UnityEngine;

/// <summary>
/// Farm power and water utilities (Al-Qassim). Tracks supply vs demand every in-game hour (WorldClock),
/// pays the SEC bill each morning, sells the equipment in the shop (pages "Power" and "Water"),
/// builds the equipment in the world (UtilityYard), feeds UtilityHooks (greenhouse growth, field drying,
/// auto irrigation), shows HUD warnings and the daily water-saving limit, and registers 3 missions.
/// Saved under "mazraa.util.*".
/// </summary>
[MazraaSystem(40)]
public class UtilityGrid : MonoBehaviour
{
    public static UtilityGrid Instance;
    public const string Pfx = "mazraa.util.";

    public UtilityModel Model { get; private set; } = new UtilityModel();
    public UtilityEnv Env { get; private set; } = new UtilityEnv();
    public UtilityYard Yard { get; private set; }

    private int lastHour = -1;
    private float noClockTimer;
    private float saveTimer = 5f;

    private void Awake()
    {
        Instance = this;
        Load();
        UtilityHooks.ResetHooks();
    }

    private void Start()
    {
        RegisterShop();
        RegisterHud();
        RegisterMissions();
        WorldClock.DayStarted += OnDayStarted;
        GameEvents.NewGameStarted += OnNewGame;
        Yard = gameObject.AddComponent<UtilityYard>();
        if (GetComponent<UtilitiesUI>() == null) gameObject.AddComponent<UtilitiesUI>();
        Recompute();
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDayStarted;
        GameEvents.NewGameStarted -= OnNewGame;
        if (Instance == this) { Instance = null; UtilityHooks.ResetHooks(); }
    }

    // ---------------- ticking ----------------

    private int ClockHour()
    {
        var c = WorldClock.Instance;
        return c == null ? -1 : (c.Day - 1) * 24 + Mathf.FloorToInt(c.DayProgress * 24f);
    }

    private void Update()
    {
        int now = ClockHour();
        if (now < 0)
        {
            noClockTimer += Time.deltaTime;
            if (noClockTimer >= 15f) { noClockTimer = 0f; Tick(1f); }
        }
        else if (lastHour < 0 || now < lastHour) lastHour = now;
        else if (now > lastHour)
        {
            int steps = Mathf.Min(now - lastHour, 48);
            lastHour = now;
            for (int i = 0; i < steps; i++) Tick(1f);
        }
        saveTimer -= Time.deltaTime;
        if (saveTimer <= 0f) { saveTimer = 10f; Save(); }
    }

    /// <summary>Gather the farm's state from the scene.</summary>
    public UtilityEnv GatherEnv()
    {
        var e = Env;
        var c = WorldClock.Instance;
        e.sun = c != null ? Mathf.Clamp01(c.SunHeight) : 1f;
        e.weather = c != null ? c.Weather : Weather.Sunny;
        e.season = c != null ? c.Season : Season.Spring;
        e.day = c != null ? c.Day : 1;
        e.hour = c != null ? Mathf.FloorToInt((6f + c.DayProgress * 24f) % 24f) : 12;
        e.greenhouses = 0; e.beds = 0;
        if (GameManager.Instance != null)
            foreach (var g in GameManager.Instance.Greenhouses)
                if (g != null) { e.greenhouses++; e.beds += g.Cropbeds.Count; }
        e.plots = 0; e.growingPlots = 0;
        var soil = SoilManager.Instance;
        if (soil != null) { if (e.plotKinds == null) e.plotKinds = new int[4]; System.Array.Clear(e.plotKinds, 0, 4); }
        else e.plotKinds = null;
        if (FieldManager.Instance != null)
            foreach (var p in FieldManager.Instance.Plots)
                if (p != null)
                {
                    e.plots++;
                    if (p.State != PlotState.Growing) continue;
                    e.growingPlots++;
                    if (soil != null) e.plotKinds[Mathf.Clamp((int)soil.EffectiveKind(p), 0, 3)]++;   // each field's own irrigation method
                }
        e.animalWater = 0f;
        var eco = EconomyManager.Instance;
        if (eco != null) foreach (var a in eco.Data.animals) e.animalWater += eco.Owned(a.id) * AnimalWater(a.id);
        e.manureKg = eco != null ? eco.Stored("manure") : 0f;
        var wells = FindObjectsByType<WellController>(FindObjectsSortMode.None);
        e.wellRunning = wells.Length == 0;
        foreach (var w in wells) if (w.IsRunning) e.wellRunning = true;
        return e;
    }

    /// <summary>m3 of water per animal per day (game balance).</summary>
    public static float AnimalWater(string id) { switch (id) { case "chicken": return 0.05f; case "sheep": return 0.2f; case "cow": return 0.8f; default: return 0.3f; } }
    /// <summary>kg of manure per animal per day.</summary>
    public static float AnimalManure(string id) { switch (id) { case "chicken": return 0.5f; case "sheep": return 3f; case "cow": return 15f; default: return 2f; } }

    /// <summary>Book one (or more) in-game hours.</summary>
    public void Tick(float hours)
    {
        var e = GatherEnv();
        var eco = EconomyManager.Instance;
        // Manure from the animals accumulates in farm storage (sellable, or fed to the biogas digester).
        if (eco != null)
        {
            float manure = 0f;
            foreach (var a in eco.Data.animals) manure += eco.Owned(a.id) * AnimalManure(a.id) * hours / 24f;
            if (manure > 0f && eco.Good("manure") != null) { eco.AddGood("manure", manure); e.manureKg += manure; }
        }
        bool wasPowerShort = Model.PowerShort, wasWaterShort = Model.WaterShort;
        float used = Model.Advance(e, hours);
        if (used > 0f && eco != null) eco.TakeGood("manure", Mathf.Min(used, eco.Stored("manure")));
        ApplyHooks();
        if (Model.PowerShort && !wasPowerShort) Say(Loc.T("Power is short! Greenhouse cooling is failing - press U"));
        if (Model.WaterShort && !wasWaterShort) Say(Loc.T("Water is short! Crops are drying - press U"));
        CheckWaterLimit();
        if (Yard != null) Yard.Refresh();
    }

    /// <summary>Recompute the rates now without booking time (after buying something).</summary>
    public void Recompute()
    {
        Model.Advance(GatherEnv(), 0f);
        ApplyHooks();
        if (Yard != null) Yard.Refresh();
    }

    private void ApplyHooks()
    {
        UtilityHooks.GreenhouseGrowth = Model.GreenhouseFactor;
        UtilityHooks.FieldDryFactor = Model.FieldDryFactor;
        UtilityHooks.FieldsAutoWatered = Model.FieldsAutoWatered;
    }

    private void CheckWaterLimit()
    {
        float used = Model.WaterUsedToday, limit = UtilityModel.DailyWaterLimit;
        if (!Model.WarnedLimit80 && used >= limit * 0.8f && used < limit)
        {
            Model.WarnedLimit80 = true;
            Say(Loc.T("80% of today's water allowance used ({0}/{1} m3). Save water!", used.ToString("F0"), limit.ToString("F0")));
        }
        if (!Model.WarnedLimit && used >= limit)
        {
            Model.WarnedLimit = Model.WarnedLimit80 = true;
            Say(Loc.T("Daily water limit reached ({0} m3). Save water - drip irrigation uses far less!", limit.ToString("F0")));
            GameEvents.RaiseCustom("waterLimitPassed", 1f);
        }
    }

    private void OnDayStarted(int day)
    {
        if (NetSession.IsClient) return; // co-op: the host runs daily costs and events
        // Book any hours of the previous day not yet ticked, so the bill is complete.
        int now = ClockHour();
        if (now >= 0 && lastHour >= 0 && now > lastHour)
        {
            int steps = Mathf.Min(now - lastHour, 48);
            for (int i = 0; i < steps; i++) Tick(1f);
            lastHour = now;
        }
        var c = WorldClock.Instance;
        bool okYesterday = !Model.PowerShortToday;
        int bill = Mathf.RoundToInt(Model.EndDay(c != null ? c.Season : Season.Spring, c != null ? c.Weather : Weather.Sunny));
        if (bill > 0 && EconomyManager.Instance != null)
        {
            EconomyManager.Instance.AddMoney(-bill);
            Say(Loc.T("SEC electricity bill: {0} SAR", bill));
        }
        if (okYesterday) GameEvents.RaiseCustom("powerOkDay", 1f);
        Save();
        if (Yard != null) Yard.Refresh();
    }

    private void OnNewGame()
    {
        Model.ResetAll();
        lastHour = -1;
        Save();
        Recompute();
        if (Yard != null) Yard.Rebuild();
    }

    // ---------------- actions ----------------

    public void CleanPanels()
    {
        if (Model.Solar <= 0) return;
        bool wasDirty = Model.Dust > 0.01f;
        Model.Dust = 0f;
        Save(); Recompute();
        Say(Loc.T("Solar panels cleaned - full power from the sun"));
        if (wasDirty) GameEvents.RaiseCustom("cleanSolar", 1f);
    }

    /// <summary>Buy litres of diesel straight into the generator tank at the per-litre price.</summary>
    public bool BuyDiesel(float litres)
    {
        if (!Model.Generator) { Say(Loc.T("Buy a diesel generator first")); return false; }
        litres = Mathf.Min(litres, UtilityModel.DieselTankL - Model.DieselL);
        if (litres < 1f) { Say(Loc.T("The fuel tank is full")); return false; }
        int cost = Mathf.CeilToInt(litres * UtilityModel.DieselPrice);
        var eco = EconomyManager.Instance;
        if (eco == null || !eco.Spend(cost)) { Say(Loc.T("Not enough money")); return false; }
        Model.DieselL += litres;
        Save(); Recompute();
        Say(Loc.T("Generator refuelled: {0} L for {1} SAR", litres.ToString("F0"), cost));
        return true;
    }

    /// <summary>Order desalinated water by tanker into the reservoir and tanks.</summary>
    public bool OrderTanker(float m3)
    {
        float space = Model.StorageCapacity - Model.Stored;
        if (space < m3 - 0.01f) { Say(Loc.T("Not enough tank space")); return false; }
        Model.Stored += m3;
        Save(); Recompute();
        Say(Loc.T("Water tanker delivered {0} m3 of desalinated water", m3.ToString("F0")));
        return true;
    }

    private void Installed(string what)
    {
        Save(); Recompute();
        if (Yard != null) Yard.Rebuild();
        GameEvents.RaiseCustom(what, 1f);
    }

    // ---------------- shop ----------------

    private static UtilityModel M => Instance != null ? Instance.Model : null;

    private static void Reg(string id, string category, string nameKey, System.Func<string> detail, System.Func<int> price, System.Func<string> block, System.Action bought)
    {
        ShopCatalog.Register(new ShopEntry
        {
            id = id, category = category,
            name = () => Loc.Raw(nameKey),
            detail = detail != null ? (System.Func<string>)(() => M != null ? detail() : "") : null,
            price = () => M != null ? price() : -1,
            blockReason = block != null ? (System.Func<string>)(() => M != null ? block() : null) : null,
            onBought = () => { if (M != null) bought(); }
        });
    }

    private static void RegisterShop()
    {
        const int tankerM3 = 20, dieselL = 200;
        Reg("util_grid", "Power", "SEC grid upgrade",
            () => "(" + UtilityModel.GridKw[Mathf.Clamp(M.GridLevel, 0, 2)].ToString("F0") + " kW)",
            () => M.NextGridCost, null, () => { M.GridLevel++; Instance.Installed("upgradeGrid"); });
        Reg("util_solar", "Power", "Solar panels 10 kW",
            () => "(" + M.Solar + "/" + UtilityModel.MaxSolar + ")",
            () => M.Solar < UtilityModel.MaxSolar ? 4000 : -1, null, () => { M.Solar++; Instance.Installed("installSolar"); });
        Reg("util_wind", "Power", "Small wind turbine 5 kW",
            () => "(" + M.Wind + "/" + UtilityModel.MaxWind + ")",
            () => M.Wind < UtilityModel.MaxWind ? 3000 : -1, null, () => { M.Wind++; Instance.Installed("installWind"); });
        Reg("util_biogas", "Power", "Biogas digester 8 kW (manure)", null,
            () => M.Biogas ? -1 : 5000, null, () => { M.Biogas = true; Instance.Installed("installBiogas"); });
        Reg("util_generator", "Power", "Diesel generator 30 kW", null,
            () => M.Generator ? -1 : 3500, null, () => { M.Generator = true; M.DieselL = Mathf.Max(M.DieselL, 100f); Instance.Installed("installGenerator"); });
        Reg("util_diesel", "Power", "Diesel 200 L",
            () => "(" + M.DieselL.ToString("F0") + " L)",
            () => Mathf.CeilToInt(dieselL * UtilityModel.DieselPrice),
            () => !M.Generator ? "Buy a diesel generator first" : M.DieselL + dieselL > UtilityModel.DieselTankL ? "The fuel tank is full" : null,
            () => { M.DieselL += dieselL; Instance.Save(); Instance.Recompute(); });

        Reg("util_deepwell", "Water", "Deep aquifer well 60 m3/day", null,
            () => M.DeepWell ? -1 : 8000, null, () => { M.DeepWell = true; Instance.Installed("installDeepWell"); });
        Reg("util_tank", "Water", "Water tank 50 m3",
            () => "(" + M.Tanks + "/" + UtilityModel.MaxTanks + ")",
            () => M.Tanks < UtilityModel.MaxTanks ? 1500 : -1, null, () => { M.Tanks++; Instance.Installed("installTank"); });
        Reg("util_tanker", "Water", "Desalinated water tanker 20 m3",
            () => "(" + M.Stored.ToString("F0") + "/" + M.StorageCapacity.ToString("F0") + " m3)",
            () => Mathf.RoundToInt(tankerM3 * UtilityModel.TankerPrice),
            () => M.StorageCapacity - M.Stored < tankerM3 ? "Not enough tank space" : null,
            () => { Instance.OrderTanker(tankerM3); });
        Reg("util_tse", "Water", "Treated wastewater line (fodder fields)", null,
            () => M.Tse ? -1 : 2000, null, () => { M.Tse = true; Instance.Installed("installTse"); });
        Reg("util_sprinklers", "Water", "Sprinklers for the fields",
            () => M.Irrigation == IrrigationKind.Sprinklers ? "(" + Loc.Raw("active") + ")" : "",
            () => M.Irrigation == IrrigationKind.Sprinklers ? -1 : 1500, null, () => { M.Irrigation = IrrigationKind.Sprinklers; M.MarkOwned(IrrigationKind.Sprinklers); Instance.Installed("installSprinklers"); });
        Reg("util_drip", "Water", "Drip irrigation lines",
            () => M.Irrigation == IrrigationKind.Drip ? "(" + Loc.Raw("active") + ")" : "",
            () => M.Irrigation == IrrigationKind.Drip ? -1 : 2500, null, () => { M.Irrigation = IrrigationKind.Drip; M.MarkOwned(IrrigationKind.Drip); Instance.Installed("installDrip"); });
        Reg("util_pivot", "Water", "Center pivot",
            () => M.Irrigation == IrrigationKind.CenterPivot ? "(" + Loc.Raw("active") + ")" : "",
            () => M.Irrigation == IrrigationKind.CenterPivot ? -1 : 12000, null, () => { M.Irrigation = IrrigationKind.CenterPivot; M.MarkOwned(IrrigationKind.CenterPivot); Instance.Installed("installPivot"); });
    }

    // ---------------- HUD + missions ----------------

    private static readonly System.Func<string> hudLine = () => Instance != null ? Instance.HudLine() : null;

    private static void RegisterHud()
    {
        if (!HudLines.Status.Contains(hudLine)) HudLines.Status.Add(hudLine);
    }

    /// <summary>HUD warning (only when something is short or over the daily limit).</summary>
    public string HudLine()
    {
        var m = Model;
        string s = null;
        if (m.PowerShort)
            s = "<color=#FF6A4D>" + Loc.T("Power short: {0}/{1} kW  [U]", m.PowerServed.ToString("F0"), m.PowerDemand.ToString("F0")) + "</color>";
        if (m.WaterShort)
            s = (s != null ? s + "\n" : "") + "<color=#FF6A4D>" + Loc.T("Water short: {0}% supplied  [U]", Mathf.RoundToInt(m.WaterRatio * 100f)) + "</color>";
        if (m.WaterUsedToday >= UtilityModel.DailyWaterLimit)
            s = (s != null ? s + "\n" : "") + "<color=#FFC24D>" + Loc.T("Over today's water limit ({0} m3)", UtilityModel.DailyWaterLimit.ToString("F0")) + "</color>";
        return s;
    }

    private static void RegisterMissions()
    {
        MissionManager.StateMissions["installSolar"] = () => M != null && M.Solar > 0 ? 1f : 0f;
        MissionManager.StateMissions["powerPositiveDays"] = () => M != null ? M.PowerOkDays : 0f;
        MissionManager.StateMissions["installEfficientIrrigation"] = () => M != null && (M.Irrigation == IrrigationKind.Drip || M.Irrigation == IrrigationKind.CenterPivot) ? 1f : 0f;
    }

    // ---------------- save ----------------

    private void Load()
    {
        var m = Model;
        m.GridLevel = Mathf.Clamp(SaveData.GetInt(Pfx + "grid", 0), 0, UtilityModel.GridKw.Length - 1);
        m.Solar = Mathf.Clamp(SaveData.GetInt(Pfx + "solar", 0), 0, UtilityModel.MaxSolar);
        m.Wind = Mathf.Clamp(SaveData.GetInt(Pfx + "wind", 0), 0, UtilityModel.MaxWind);
        m.Tanks = Mathf.Clamp(SaveData.GetInt(Pfx + "tanks", 0), 0, UtilityModel.MaxTanks);
        m.Generator = SaveData.GetInt(Pfx + "generator", 0) == 1;
        m.Biogas = SaveData.GetInt(Pfx + "biogas", 0) == 1;
        m.DeepWell = SaveData.GetInt(Pfx + "deepwell", 0) == 1;
        m.Tse = SaveData.GetInt(Pfx + "tse", 0) == 1;
        m.Irrigation = (IrrigationKind)Mathf.Clamp(SaveData.GetInt(Pfx + "irrigation", 0), 0, 3);
        m.OwnedIrrigation = SaveData.GetInt(Pfx + "irrigationOwned", 0);
        m.MarkOwned(m.Irrigation);   // saves from before per-field irrigation
        m.DieselL = SaveData.GetFloat(Pfx + "diesel", 0f);
        m.Dust = SaveData.GetFloat(Pfx + "dust", 0f);
        m.Stored = Mathf.Clamp(SaveData.GetFloat(Pfx + "stored", UtilityModel.BaseReservoir), 0f, m.StorageCapacity);
        m.GridKwhToday = SaveData.GetFloat(Pfx + "gridKwh", 0f);
        m.ExportKwhToday = SaveData.GetFloat(Pfx + "exportKwh", 0f);
        m.WaterUsedToday = SaveData.GetFloat(Pfx + "waterToday", 0f);
        m.TseUsedToday = SaveData.GetFloat(Pfx + "tseToday", 0f);
        m.PowerShortToday = SaveData.GetInt(Pfx + "shortToday", 0) == 1;
        m.PowerOkDays = SaveData.GetInt(Pfx + "okDays", 0);
    }

    public void Save()
    {
        var m = Model;
        SaveData.SetInt(Pfx + "grid", m.GridLevel);
        SaveData.SetInt(Pfx + "solar", m.Solar);
        SaveData.SetInt(Pfx + "wind", m.Wind);
        SaveData.SetInt(Pfx + "tanks", m.Tanks);
        SaveData.SetInt(Pfx + "generator", m.Generator ? 1 : 0);
        SaveData.SetInt(Pfx + "biogas", m.Biogas ? 1 : 0);
        SaveData.SetInt(Pfx + "deepwell", m.DeepWell ? 1 : 0);
        SaveData.SetInt(Pfx + "tse", m.Tse ? 1 : 0);
        SaveData.SetInt(Pfx + "irrigation", (int)m.Irrigation);
        SaveData.SetInt(Pfx + "irrigationOwned", m.OwnedIrrigation);
        SaveData.SetFloat(Pfx + "diesel", m.DieselL);
        SaveData.SetFloat(Pfx + "dust", m.Dust);
        SaveData.SetFloat(Pfx + "stored", m.Stored);
        SaveData.SetFloat(Pfx + "gridKwh", m.GridKwhToday);
        SaveData.SetFloat(Pfx + "exportKwh", m.ExportKwhToday);
        SaveData.SetFloat(Pfx + "waterToday", m.WaterUsedToday);
        SaveData.SetFloat(Pfx + "tseToday", m.TseUsedToday);
        SaveData.SetInt(Pfx + "shortToday", m.PowerShortToday ? 1 : 0);
        SaveData.SetInt(Pfx + "okDays", m.PowerOkDays);
        SaveData.Save();
    }

    private void OnApplicationQuit() => Save();

    private static void Say(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
