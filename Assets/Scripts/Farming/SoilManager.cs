using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Precision farming for the open fields: a soil model per plot (N, P, K, pH, organic matter, salinity, compaction, weeds,
/// standing water), crop water use from the season, weather and region heat, irrigation method efficiency and salt build-up,
/// groundwater pumping cost, tillage quality (hand hoe vs tractor plow, soil moisture), sowing density and seed rate,
/// temperature-driven growth and heat stress at flowering, mulch / straw, soil tests, and a per-field yield history.
/// Yield = base x every factor (FieldPlot.YieldModifiers); the lowest one is named as the limiting factor.
/// Soil processes tick every 2 s (cheap) and once per in-game day; nothing runs per frame except the cached drying factor.
/// E on a field: take a soil sample (kit), spread an amendment you own, or leach salts; the Fields page of the farm info
/// screen (pause menu > Fields, LB/RB in the info screen) lists every field. Saved under "mazraa.soil.*"; reset on a new game.
/// </summary>
[MazraaSystem(32)]
public class SoilManager : MonoBehaviour
{
    public static SoilManager Instance;
    public const string P = "mazraa.soil.";
    public const string KitId = "kit";
    public const float TickSeconds = 2f;
    public const float LeachEc = 4f;
    public const int HistoryMax = 6;

    private readonly Dictionary<int, SoilState> states = new Dictionary<int, SoilState>();
    private readonly Dictionary<string, int> items = new Dictionary<string, int>();
    private readonly HashSet<int> dirty = new HashSet<int>();

    /// <summary>Sowing density for the next plantings: 0 low, 1 normal, 2 high.</summary>
    public int Density { get; private set; } = 1;
    public float WaterTodayM3 { get; private set; }
    public float WaterCostToday { get; private set; }
    public float TempToday { get; private set; } = 25f;
    public float SeasonEtToday { get; private set; } = 1f;
    public float WeatherEtToday { get; private set; } = 1f;

    private float tickTimer, saveTimer = 10f;

    private void Awake()
    {
        Instance = this;
        LoadGlobals();
        RegisterShop();
    }

    private void Start() => RefreshClimate();

    private void OnEnable()
    {
        WorldClock.DayStarted += OnDay;
        WorldClock.WeatherChanged += OnWeather;
        FieldPlot.FieldPlanted += OnPlanted;
        FieldPlot.FieldHarvested += OnHarvested;
        FieldPlot.FieldPlowed += OnPlowed;
        FieldPlot.FieldWatered += OnWatered;
        CropCareManager.Fertilized += OnFertilized;
        GameEvents.NewGameStarted += OnNewGame;
        AddOnce(FieldPlot.GrowthModifiers, PlotGrowth);
        AddOnce(FieldPlot.YieldModifiers, PlotYield);
        AddOnce(FieldPlot.DryModifiers, PlotDry);
        AddOnce(FieldPlot.PromptExtras, PlotExtras);
        AddOnce(FieldPlot.PromptOverrides, PlotOverride);
        AddOnce(FieldPlot.InteractHandlers, PlotInteract);
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnDay;
        WorldClock.WeatherChanged -= OnWeather;
        FieldPlot.FieldPlanted -= OnPlanted;
        FieldPlot.FieldHarvested -= OnHarvested;
        FieldPlot.FieldPlowed -= OnPlowed;
        FieldPlot.FieldWatered -= OnWatered;
        CropCareManager.Fertilized -= OnFertilized;
        GameEvents.NewGameStarted -= OnNewGame;
        SaveDirty();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static void AddOnce<T>(List<T> list, T f) { if (!list.Contains(f)) list.Add(f); }

    // ---------------------------------------------------------------- state

    private void LoadGlobals()
    {
        items.Clear();
        items[KitId] = SaveData.GetInt(P + "item." + KitId, 0);
        foreach (var a in SoilData.Data.amendments) if (a.shop) items[a.id] = SaveData.GetInt(P + "item." + a.id, 0);
        Density = Mathf.Clamp(SaveData.GetInt(P + "density", 1), 0, 2);
        WaterTodayM3 = SaveData.GetFloat(P + "waterToday", 0f);
        WaterCostToday = SaveData.GetFloat(P + "waterCost", 0f);
    }

    public SoilState Soil(int index)
    {
        if (states.TryGetValue(index, out var s)) return s;
        s = SoilState.Unpack(SaveData.GetString(P + "p." + index, ""), SoilData.CurrentRegion.start);
        s.history = SaveData.GetString(P + "h." + index, "");
        s.lastCrop = SaveData.GetString(P + "c." + index, "");
        states[index] = s;
        return s;
    }

    public SoilState Soil(FieldPlot p) => Soil(p.index);

    /// <summary>Give a plot the region's starting soil again (new land, tests).</summary>
    public SoilState ResetSoil(int index)
    {
        var s = SoilState.New(SoilData.CurrentRegion.start);
        states[index] = s;
        SavePlot(index);
        return s;
    }

    private void SavePlot(int index)
    {
        var s = Soil(index);
        SaveData.SetString(P + "p." + index, s.Pack());
        SaveData.SetString(P + "h." + index, s.history);
        SaveData.SetString(P + "c." + index, s.lastCrop);
        dirty.Remove(index);
        SaveData.Save();
    }

    private void SaveDirty()
    {
        if (dirty.Count == 0) return;
        foreach (var i in new List<int>(dirty)) SavePlot(i);
    }

    /// <summary>Write every loaded plot to the save (also on quit).</summary>
    public void SaveAll()
    {
        foreach (var i in new List<int>(states.Keys)) SavePlot(i);
        SaveGlobals();
    }

    private void OnApplicationQuit() => SaveAll();

    private void SaveGlobals()
    {
        SaveData.SetInt(P + "density", Density);
        SaveData.SetFloat(P + "waterToday", WaterTodayM3);
        SaveData.SetFloat(P + "waterCost", WaterCostToday);
        SaveData.Save();
    }

    public int Items(string id) => items.TryGetValue(id, out var n) ? n : 0;
    public void AddItem(string id, int n = 1) { items[id] = Items(id) + n; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save(); }
    public bool UseItem(string id)
    {
        if (Items(id) <= 0) return false;
        items[id]--; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save();
        return true;
    }

    public void SetDensity(int d) { Density = Mathf.Clamp(d, 0, 2); SaveGlobals(); }
    public void CycleDensity(int dir) => SetDensity((Density + dir + 3) % 3);

    public bool Sampled(SoilState s)
    {
        var c = WorldClock.Instance;
        if (c == null) return s.sampledDay > -1000;
        return c.Day - s.sampledDay < Mathf.Max(1, c.daysPerSeason);
    }

    // ---------------------------------------------------------------- climate

    private void OnWeather(Weather w) => RefreshClimate();

    public void RefreshClimate()
    {
        var c = WorldClock.Instance;
        var r = SoilData.CurrentRegion;
        Season s = c != null ? c.Season : Season.Spring;
        Weather w = c != null ? c.Weather : Weather.Sunny;
        TempToday = SoilModel.Temperature(r, s, w);
        SeasonEtToday = SoilModel.SeasonEt(r, s);
        WeatherEtToday = SoilModel.WeatherEt(w);
    }

    private static bool Hot()
    {
        var c = WorldClock.Instance;
        return c != null && (c.Season == Season.Summer || c.Weather == Weather.Heatwave);
    }

    public static IrrigationKind Method()
    {
        var g = UtilityGrid.Instance;
        return g != null && UtilityHooks.FieldsAutoWatered ? g.Model.Irrigation : IrrigationKind.None;
    }

    // ---------------------------------------------------------------- ticking

    private void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= TickSeconds) { Tick(tickTimer); tickTimer = 0f; }
        saveTimer -= Time.deltaTime;
        if (saveTimer <= 0f) { saveTimer = 10f; SaveDirty(); }
    }

    /// <summary>Soil processes for the last few seconds: crop water use, salt, stress history, weeds, drainage.</summary>
    public void Tick(float seconds)
    {
        if (NetSession.IsClient) return;   // co-op: the host runs the soil
        var fm = FieldManager.Instance;
        if (fm == null) return;
        var r = SoilData.CurrentRegion;
        var irr = SoilData.Irrigation(Method());
        var clock = WorldClock.Instance;
        bool rain = clock != null && clock.Weather == Weather.Rain;
        float et = SeasonEtToday * WeatherEtToday;
        foreach (var p in fm.Plots)
        {
            if (p == null) continue;
            var s = Soil(p.index);
            bool growing = p.State == PlotState.Growing || p.State == PlotState.Ripe;
            if (growing)
            {
                if (s.lastGrowth < 0f || p.Growth < s.lastGrowth) s.lastGrowth = p.Growth;
                float dg = p.Growth - s.lastGrowth;
                if (dg > 0f)
                {
                    float mm = SoilModel.Grow(s, SoilData.Crop(p.CropId), s.lastGrowth, dg, p.Water, TempToday, et, rain, irr, r.ecWater);
                    BookWater(s, p, mm, r);
                    s.lastGrowth = p.Growth;
                }
            }
            if (!GameSettings.Weeds) s.weeds = 0f; else   // Settings > Game > Weeds
            SoilModel.GrowWeeds(s, seconds * EconomyManager.GrowthSpeed, p.State == PlotState.Growing, p.Growth, p.Water, TempToday);
            SoilModel.Drain(s, seconds, r.drainage);
            dirty.Add(p.index);
        }
    }

    private void BookWater(SoilState s, FieldPlot p, float mm, SoilRegionDef r)
    {
        if (mm <= 0f) return;
        float m3 = mm * p.size.x * p.size.y / 1000f;
        float cost = m3 * r.waterCost;
        s.cycWater += m3; s.cycCost += cost;
        WaterTodayM3 += m3; WaterCostToday += cost;
    }

    private void OnDay(int day)
    {
        RefreshClimate();
        if (NetSession.IsClient) return;
        var c = WorldClock.Instance;
        var r = SoilData.CurrentRegion;
        var fm = FieldManager.Instance;
        if (fm != null)
            foreach (var p in fm.Plots)
            {
                if (p == null) continue;
                SoilModel.Daily(Soil(p.index), c != null ? c.Season : Season.Spring, c != null ? c.Weather : Weather.Sunny, r);
                SavePlot(p.index);
            }
        int bill = Mathf.RoundToInt(WaterCostToday);
        if (bill > 0 && EconomyManager.Instance != null)
        {
            EconomyManager.Instance.AddMoney(-bill);
            Say(SoilText.R("Groundwater pumping") + ": " + WaterTodayM3.ToString("F0") + " m3, " + bill + " " + Loc.Raw("SAR"));
        }
        WaterTodayM3 = 0f; WaterCostToday = 0f;
        SaveGlobals();
    }

    // ---------------------------------------------------------------- field events

    private void OnPlanted(FieldPlot p)
    {
        var s = Soil(p.index);
        SoilModel.ClearCycle(s);
        s.lastGrowth = p.Growth;
        s.density = Density;
        var eco = EconomyManager.Instance;
        var crop = eco != null ? eco.FieldCrop(p.CropId) : null;
        if (crop != null)
        {
            int extra = Mathf.RoundToInt(crop.seedPrice * (SoilModel.DensitySeed[Density] - 1f));
            if (extra > 0 && eco.Money < extra) s.density = 1;            // can't pay for the extra seed: sow normally
            else if (extra != 0) eco.AddMoney(-extra);                     // high density buys more seed; low density sells the rest back
            s.cycCost += crop.seedPrice * SoilModel.DensitySeed[s.density];
        }
        SavePlot(p.index);
    }

    private void OnHarvested(FieldPlot p, string cropId, float kg)
    {
        var s = Soil(p.index);
        var crop = SoilData.Crop(cropId);
        var f = SoilModel.Factors(s, crop, PestFactor(p));
        var eco = EconomyManager.Instance;
        var def = eco != null ? eco.FieldCrop(cropId) : null;
        float pct = def != null && def.yieldKg > 0f ? kg / def.yieldKg : 1f;
        var clock = WorldClock.Instance;
        AddHistory(s, (clock != null ? clock.Day : 0) + "|" + cropId + "|" + kg.ToString("F0", CultureInfo.InvariantCulture) + "|" + Mathf.RoundToInt(pct * 100f) + "|" +
                      Mathf.RoundToInt(s.cycN) + "|" + s.cycWater.ToString("F1", CultureInfo.InvariantCulture) + "|" + Mathf.RoundToInt(s.cycCost) + "|" + f.Limiting);
        bool regrow = CropCareManager.Instance != null && CropCareManager.Instance.Plot(p.index).regrow >= 0f;
        SoilModel.Harvest(s, crop, f.Product, Vehicle.Current != null, p.Water, regrow);
        s.lastCrop = cropId;
        SavePlot(p.index);
    }

    private static void AddHistory(SoilState s, string rec)
    {
        var list = new List<string>(string.IsNullOrEmpty(s.history) ? new string[0] : s.history.Split(';'));
        list.Add(rec);
        while (list.Count > HistoryMax) list.RemoveAt(0);
        s.history = string.Join(";", list.ToArray());
    }

    private void OnPlowed(FieldPlot p, bool byHand)
    {
        var s = Soil(p.index);
        string msg = SoilModel.Till(s, !byHand, p.Water, Hot());
        SavePlot(p.index);
        if (msg.StartsWith("Too")) Say(SoilText.R(msg));
    }

    private void OnWatered(FieldPlot p, float before)
    {
        var s = Soil(p.index);
        SoilModel.OverWater(s, before);
        SavePlot(p.index);
        if (s.waterlog > 0.5f) Say(SoilText.R("Over-watered: water is standing in the field"));
    }

    private void OnFertilized(FieldPlot p, CureDef cure)
    {
        var a = cure != null ? SoilData.ForCareItem(cure.id) : null;
        if (a == null) return;
        var s = Soil(p.index);
        SoilModel.Apply(s, a);
        s.cycCost += cure.price;
        SavePlot(p.index);
    }

    private void OnNewGame()
    {
        states.Clear(); dirty.Clear();
        LoadGlobals();
        foreach (var k in new List<string>(items.Keys)) items[k] = 0;
        Density = 1; WaterTodayM3 = 0f; WaterCostToday = 0f;
        RefreshClimate();
    }

    // ---------------------------------------------------------------- yield / growth hooks

    private static float PestFactor(FieldPlot p)
    {
        var care = CropCareManager.Instance;
        if (care == null) return 1f;
        var c = care.Plot(p.index);
        return (1f - Mathf.Clamp01(c.damage)) * (string.IsNullOrEmpty(c.pest) ? 1f : 0.97f);
    }

    /// <summary>Yield factors of the crop on this plot now (what a harvest would get).</summary>
    public SoilFactors Factors(FieldPlot p) => SoilModel.Factors(Soil(p.index), SoilData.Crop(p.CropId), PestFactor(p));

    public float YieldFactor(FieldPlot p) => p.State == PlotState.Growing || p.State == PlotState.Ripe ? SoilModel.Factors(Soil(p.index), SoilData.Crop(p.CropId)).Product : 1f;

    public float GrowthFactor(FieldPlot p)
    {
        var c = SoilData.Crop(p.CropId);
        return SoilModel.TempGrowth(TempToday, c) * SoilModel.GrowthFactor(Soil(p.index), c);
    }

    /// <summary>Expected harvest (kg) with every registered modifier, or 0 when nothing is planted.</summary>
    public static float ExpectedKg(FieldPlot p)
    {
        if (p.State != PlotState.Growing && p.State != PlotState.Ripe) return 0f;
        var def = EconomyManager.Instance != null ? EconomyManager.Instance.FieldCrop(p.CropId) : null;
        return (def != null ? def.yieldKg : 50f) * FieldPlot.YieldMultiplier(p);
    }

    private static float PlotYield(FieldPlot p) => Instance != null ? Instance.YieldFactor(p) : 1f;
    private static float PlotGrowth(FieldPlot p) => Instance != null ? Instance.GrowthFactor(p) : 1f;
    private static float PlotDry(FieldPlot p) => Instance != null ? SoilModel.DryFactor(Instance.Soil(p.index), p.Growth, Instance.SeasonEtToday) : 1f;

    // ---------------------------------------------------------------- E actions

    public enum ActionKind { None, Sample, Amend, Leach }
    public struct PlotAction { public ActionKind kind; public SoilAmendmentDef amendment; }

    /// <summary>What E does on this plot (before the default job), if anything.</summary>
    public PlotAction NextAction(FieldPlot p)
    {
        var none = new PlotAction();
        var s = Soil(p.index);
        bool sampled = Sampled(s);
        if ((p.State == PlotState.Untilled || p.State == PlotState.Plowed) && Items(KitId) > 0 && !sampled) return new PlotAction { kind = ActionKind.Sample };
        if (p.State == PlotState.Plowed)
        {
            var a = BestAmendment(s, sampled, false);
            if (a != null && (a.ec < 0f || s.ec <= LeachEc)) return new PlotAction { kind = ActionKind.Amend, amendment = a };
            if (sampled && s.ec > LeachEc && !FarmModifiers.IrrigationBanned) return new PlotAction { kind = ActionKind.Leach };
            if (a != null) return new PlotAction { kind = ActionKind.Amend, amendment = a };
        }
        else if (p.State == PlotState.Growing && p.Water >= 0.35f)
        {
            var a = BestAmendment(s, sampled, true);
            if (a != null) return new PlotAction { kind = ActionKind.Amend, amendment = a };
        }
        return none;
    }

    private SoilAmendmentDef BestAmendment(SoilState s, bool sampled, bool growing)
    {
        var list = SoilData.Data.amendments;
        for (int i = 0; i < list.Length && i < 31; i++)
        {
            var a = list[i];
            if (!a.shop || Items(a.id) <= 0 || (s.appliedMask & (1 << i)) != 0) continue;
            if (growing && !a.growing) continue;
            if (Useful(a, s, sampled)) return a;
        }
        return null;
    }

    /// <summary>Herbicide and mulch need no test (you see weeds); other amendments are spread where a soil test shows a need,
    /// or once per crop "blind" on an untested field.</summary>
    public static bool Useful(SoilAmendmentDef a, SoilState s, bool sampled)
    {
        if (a.herbicide) return s.weeds > 0.2f;
        if (a.mulch) return !s.mulch;
        if (!sampled) return true;
        return (a.n > 0f && s.n < 150f) || (a.p > 0f && SoilModel.EffectiveP(s) < 45f) || (a.k > 0f && s.k < 180f) || (a.om > 0f && s.om < 2f)
            || (a.ec < 0f && s.ec > 2.5f) || (a.ph < 0f && s.ph > 7.6f) || (a.ph > 0f && s.ph < 6f);
    }

    private static string PlotOverride(FieldPlot p)
    {
        var me = Instance; if (me == null) return null;
        var act = me.NextAction(p);
        switch (act.kind)
        {
            case ActionKind.Sample: return SoilText.R("[E] Take a soil sample") + " (" + me.Items(KitId) + ")";
            case ActionKind.Leach: return SoilText.R("[E] Leach the salts") + " (" + SoilData.Data.leachMm.ToString("F0") + " mm) - EC " + me.Soil(p.index).ec.ToString("F1") + " dS/m";
            case ActionKind.Amend:
                return SoilText.R("[E] Spread") + " " + SoilData.Name(act.amendment) + " (" + me.Items(act.amendment.id) + ")" +
                       (me.Sampled(me.Soil(p.index)) || act.amendment.herbicide || act.amendment.mulch ? "" : " - " + SoilText.R("soil not tested"));
        }
        return null;
    }

    private static bool PlotInteract(FieldPlot p)
    {
        var me = Instance; if (me == null) return false;
        var act = me.NextAction(p);
        switch (act.kind)
        {
            case ActionKind.Sample: return me.TakeSample(p);
            case ActionKind.Leach: return me.LeachSalts(p);
            case ActionKind.Amend: return me.Spread(p, act.amendment);
        }
        return false;
    }

    /// <summary>Take a soil sample (uses a kit): the field's values show on the Fields page for the rest of the season.</summary>
    public bool TakeSample(FieldPlot p)
    {
        if (!UseItem(KitId)) return false;
        var s = Soil(p.index);
        s.sampledDay = WorldClock.Instance != null ? WorldClock.Instance.Day : 0;
        SavePlot(p.index);
        GameEvents.RaiseCustom("soilSample", 1f);
        Say(FieldName(p) + " - " + SoilText.R("Soil test") + ": " + TestLine(s));
        return true;
    }

    public bool Spread(FieldPlot p, SoilAmendmentDef a)
    {
        if (a == null || !UseItem(a.id)) return false;
        var s = Soil(p.index);
        SoilModel.Apply(s, a);
        int i = Array.IndexOf(SoilData.Data.amendments, a);
        if (i >= 0 && i < 31) s.appliedMask |= 1 << i;
        s.cycCost += a.price;
        SavePlot(p.index);
        GameEvents.RaiseCustom("soilAmend", 1f);
        Say(SoilData.Name(a) + " - " + FieldName(p));
        return true;
    }

    public bool LeachSalts(FieldPlot p)
    {
        if (FarmModifiers.IrrigationBanned) return false;
        var s = Soil(p.index);
        float before = s.ec;
        SoilModel.Leach(s);
        BookWater(s, p, SoilData.Data.leachMm, SoilData.CurrentRegion);
        SavePlot(p.index);
        GameEvents.RaiseCustom("soilLeach", 1f);
        Say(SoilText.R("Salts leached") + ": EC " + before.ToString("F1") + " -> " + s.ec.ToString("F1") + " dS/m");
        return true;
    }

    private static string PlotExtras(FieldPlot p)
    {
        var me = Instance; if (me == null) return null;
        var s = me.Soil(p.index);
        var parts = new List<string>();
        if (p.State == PlotState.Growing) parts.Add(SoilText.R(SoilModel.Stage(p.Growth)));
        if (s.weeds > 0.2f) parts.Add(SoilText.R("Weeds") + " " + CropsText.Pct(s.weeds));
        if (s.waterlog > 0.5f) parts.Add(SoilText.R("Waterlogged"));
        if (p.State != PlotState.Growing && p.State != PlotState.Ripe && me.Sampled(s)) parts.Add(TestLine(s));
        return parts.Count > 0 ? string.Join(" | ", parts.ToArray()) : null;
    }

    // ---------------------------------------------------------------- shop

    private void RegisterShop()
    {
        ShopCatalog.Register(new ShopEntry
        {
            id = "soil_" + KitId,
            category = "Soil",
            name = () => SoilText.R("Soil sample kit (lab test)"),
            detail = () => "(" + (Instance != null ? Instance.Items(KitId) : 0) + ")",
            price = () => SoilData.Data.kitPrice,
            onBought = () => { if (Instance != null) Instance.AddItem(KitId); },
        });
        foreach (var amend in SoilData.Data.amendments)
        {
            if (!amend.shop) continue;
            var a = amend;
            ShopCatalog.Register(new ShopEntry
            {
                id = "soil_" + a.id,
                category = "Soil",
                name = () => SoilData.Name(a),
                detail = () => "(" + (Instance != null ? Instance.Items(a.id) : 0) + ")" + (a.manureKg > 0f ? " " + a.manureKg.ToString("F0") + " kg " + SoilText.R("manure") : ""),
                price = () => a.price,
                blockReason = a.manureKg > 0f ? (Func<string>)(() => EconomyManager.Instance != null && EconomyManager.Instance.Stored("manure") >= a.manureKg ? null : "Not enough manure in storage") : null,
                onBought = () =>
                {
                    if (a.manureKg > 0f && EconomyManager.Instance != null) EconomyManager.Instance.TakeGood("manure", a.manureKg);
                    if (Instance != null) Instance.AddItem(a.id);
                },
            });
        }
    }

    // ---------------------------------------------------------------- text

    public static string FieldName(FieldPlot p) => SoilText.R("Field") + " " + (p.index + 1);

    /// <summary>Short soil test result line (translated, not shaped).</summary>
    public static string TestLine(SoilState s)
    {
        return "N " + s.n.ToString("F0") + " · P " + s.p.ToString("F0") + " (" + SoilText.R("available") + " " + SoilModel.EffectiveP(s).ToString("F0") + ") · K " + s.k.ToString("F0") + " kg/ha · pH " +
               s.ph.ToString("F1") + " · " + SoilText.R("OM") + " " + s.om.ToString("F1") + "% · EC " + s.ec.ToString("F1") + " dS/m · " + SoilText.R("Compaction") + " " + CropsText.Pct(s.compaction);
    }

    public static string Advice(int limiting, SoilState s)
    {
        switch (limiting)
        {
            case 0: return "Spread urea or NPK fertilizer";
            case 1: return s.ph > 7.6f ? "Spread superphosphate; sulfur frees phosphorus in alkaline soil" : "Spread superphosphate";
            case 2: return "Spread potassium sulfate";
            case 3: return s.ph > 7f ? "Spread agricultural sulfur to lower the pH" : "Spread agricultural lime to raise the pH";
            case 4: return "Spread gypsum and leach the salts; drip builds up less salt than flooding";
            case 5: return "Plow deep with the tractor when the soil is not wet";
            case 6: return "Water more often or install drip irrigation";
            case 7: return "Water less and let the field drain";
            case 8: return "Sow in the crop's season to avoid heat at flowering";
            case 9: return "Spray herbicide or plow deep";
            case 10: return "Plow at the right soil moisture";
            case 11: return "Use normal sowing density when water or nitrogen is short";
            case 12: return "Treat the pests (Crop care)";
            default: return "";
        }
    }

    public static string DensityText(int d) => SoilText.R(SoilModel.DensityNames[Mathf.Clamp(d, 0, 2)]) + " (" + SoilText.R("seed") + " " + Mathf.RoundToInt(SoilModel.DensitySeed[Mathf.Clamp(d, 0, 2)] * 100f) + "%)";

    private static string CropName(string id)
    {
        if (string.IsNullOrEmpty(id)) return SoilText.R("none");
        var eco = EconomyManager.Instance;
        var def = eco != null ? eco.FieldCrop(id) : null;
        return def != null ? FieldCropText.Name(def) : id;
    }

    private static string Line(string s) => "\n" + Loc.Display(s);

    /// <summary>Fields page (precision farming): every field's soil, crop, expected yield, limiting factor, advice and yield history.
    /// Shows `count` fields from `first`. Translated and shaped (also used by tests).</summary>
    public static string FieldsText(int first = 0, int count = 4)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(SoilText.T("Fields - precision farming")));
        var me = Instance; var fm = FieldManager.Instance;
        if (me == null || fm == null) return sb.ToString();
        var irr = SoilData.Irrigation(Method());
        sb.Append(Line(SoilText.R("Sowing density") + ": < " + DensityText(me.Density) + " >"));
        sb.Append(Line(SoilText.R("Irrigation") + ": " + Loc.Raw(UtilitiesUI.IrrigationName(Method())) + " - " + SoilText.R("efficiency") + " " + Mathf.RoundToInt(irr.eff * 100f) + "%"));
        sb.Append(Line(SoilText.R("Today") + ": " + me.TempToday.ToString("F0") + " C · " + SoilText.R("Groundwater") + " " + me.WaterTodayM3.ToString("F1") + " m3 (" + me.WaterCostToday.ToString("F1") + " " + Loc.Raw("SAR") + ")"));
        int n = fm.Plots.Count;
        first = Mathf.Clamp(first, 0, Mathf.Max(0, n - 1));
        int last = Mathf.Min(n, first + Mathf.Max(1, count));
        for (int i = first; i < last; i++)
        {
            var p = fm.Plots[i];
            if (p == null) continue;
            var s = me.Soil(p.index);
            bool sampled = me.Sampled(s);
            bool growing = p.State == PlotState.Growing || p.State == PlotState.Ripe;
            sb.Append("\n");
            string head = FieldName(p) + ": ";
            if (growing) head += CropName(p.CropId) + " " + Mathf.FloorToInt(p.Growth * 100f) + "% (" + SoilText.R(p.State == PlotState.Ripe ? "Ripe" : SoilModel.Stage(p.Growth)) + ")";
            else head += SoilText.R(p.State == PlotState.Plowed ? "Plowed" : "Untilled");
            head += " · " + SoilText.R("Moisture") + " " + CropsText.Pct(p.State == PlotState.Untilled ? 0f : p.Water) + " · " + SoilText.R("Weeds") + " " + CropsText.Pct(s.weeds);
            if (s.waterlog > 0.5f) head += " · " + SoilText.R("Waterlogged");
            sb.Append("\n").Append(MenuKit.Title(Loc.Display(head)));
            sb.Append(Line(sampled ? SoilText.R("Soil test") + ": " + TestLine(s) : SoilText.R("Soil: not tested - buy a soil sample kit (Farm Shop, Soil) and press [E] on the field")));
            string l3 = SoilText.R("Last crop") + ": " + CropName(s.lastCrop);
            int limiting = -1;
            if (growing)
            {
                var f = me.Factors(p);
                limiting = f.Limiting;
                l3 += " · " + SoilText.R("Expected yield") + ": " + ExpectedKg(p).ToString("F0") + " kg (" + Mathf.RoundToInt(FieldPlot.YieldMultiplier(p) * 100f) + "%)";
                l3 += " · " + SoilText.R("Limiting") + ": " + (limiting < 0 ? SoilText.R("nothing") : SoilFactors.NeedsTest(limiting) && !sampled ? SoilText.R("test the soil") : SoilText.R(SoilFactors.Names[limiting]));
            }
            else if (sampled)
            {
                limiting = SoilModel.Factors(s, SoilData.Crop("")).Limiting;
            }
            sb.Append(Line(l3));
            if (limiting >= 0 && (sampled || !SoilFactors.NeedsTest(limiting))) sb.Append(Line(SoilText.R("Advice") + ": " + SoilText.R(Advice(limiting, s))));
            sb.Append(Line(SoilText.R("History") + ": " + HistoryText(s, 3)));
        }
        if (n > count) sb.Append(Line(SoilText.R("Fields") + " " + (first + 1) + "-" + last + " / " + n));
        sb.Append(Line(SoilText.R("Farm") + ": " + FarmSummary()));
        return sb.ToString();
    }

    /// <summary>Last harvests of a field, e.g. "Day 3 Wheat 52 kg 87% (N 45, 38 m3)".</summary>
    public static string HistoryText(SoilState s, int max)
    {
        if (string.IsNullOrEmpty(s.history)) return SoilText.R("no harvests yet");
        var recs = s.history.Split(';');
        var parts = new List<string>();
        for (int i = recs.Length - 1; i >= 0 && parts.Count < max; i--)
        {
            var f = recs[i].Split('|');
            if (f.Length < 7) continue;
            string lim = "";
            if (f.Length > 7 && int.TryParse(f[7], out int li) && li >= 0 && li < SoilFactors.Names.Length) lim = ", " + SoilText.R(SoilFactors.Names[li]);
            parts.Add(SoilText.R("Day") + " " + f[0] + " " + CropName(f[1]) + " " + f[2] + " kg " + f[3] + "% (N " + f[4] + ", " + f[5] + " m3" + lim + ")");
        }
        return string.Join(" · ", parts.ToArray());
    }

    /// <summary>All fields: harvests recorded, kg and the average share of the potential yield.</summary>
    public static string FarmSummary()
    {
        var me = Instance; var fm = FieldManager.Instance;
        if (me == null || fm == null) return "";
        int count = 0; float kg = 0f, pct = 0f;
        foreach (var p in fm.Plots)
        {
            if (p == null) continue;
            var h = me.Soil(p.index).history;
            if (string.IsNullOrEmpty(h)) continue;
            foreach (var rec in h.Split(';'))
            {
                var f = rec.Split('|');
                if (f.Length < 4) continue;
                float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float k);
                float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float q);
                count++; kg += k; pct += q;
            }
        }
        if (count == 0) return SoilText.R("no harvests yet");
        return count + " " + SoilText.R("harvests") + ", " + kg.ToString("F0") + " kg, " + SoilText.R("average") + " " + Mathf.RoundToInt(pct / count) + "% " + SoilText.R("of potential");
    }

    private static void Say(string translatedUnshaped) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(translatedUnshaped)); }
}

/// <summary>Text for the soil feature: English keys with Arabic in Resources/Data/Loc/91_soil.json (tests check every key).</summary>
public static class SoilText
{
    public static string R(string english) => Loc.Raw(english);
    public static string T(string english) => Loc.T(english);
}
