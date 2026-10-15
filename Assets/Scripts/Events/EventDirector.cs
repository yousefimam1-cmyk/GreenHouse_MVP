using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FarmEventDef
{
    public string id, type;                 // type: heat | haboob | flood | frost | locust | waterBan | demand | buyer
    public string nameEn, nameAr, warnEn, warnAr, startEn, startAr;
    public string[] seasons;                // spring / summer / autumn / winter (empty = any)
    public float weight = 1f;
    public int minDay = 2;
    public float startHour = 6f;            // clock hour it starts (day starts 06:00)
    public float hours = 0f;                // 0 = the whole day
    public int days = 1;
    public float fieldDry = 1f, penWater = 1f, growth = 1f, priceMul = 1f, fog = 0f;
    public float lossChance = 0.5f, fertility = 1.4f, bonus = 1.8f, amount = 150f;
    public int plots = 2;
    public string[] crops, goods;
    public string buyerEn, buyerAr;
}

[Serializable] public class FarmEventFile { public float dailyChance = 0.3f; public FarmEventDef[] events; }

/// <summary>
/// Random farm events from Resources/Data/events.json, rolled per season and announced a day ahead
/// (alert + HUD line): extreme heat, haboob dust storm, flash flood in the wadi, frost night, desert locusts,
/// water restriction day, Ramadan demand boom, big buyer visit. Effects go through FarmModifiers (read by
/// FieldPlot / AnimalPen / MarketManager) or existing public methods (FieldPlot.ResetPlot/AddWater,
/// Cropbed.ResetGrowth, MarketManager.Contracts). Greenhouse vents (E at the switch by each control station)
/// protect the beds from dust storms. Saved under mazraa.events.*.
/// </summary>
[MazraaSystem(40)]
public class EventDirector : MonoBehaviour
{
    /// <summary>Daily random rolls on/off (tests switch them off for deterministic runs).</summary>
    public static bool RandomEventsEnabled = true;

    public static EventDirector Instance;

    public FarmEventFile Data { get; private set; }
    public FarmEventDef Pending { get; private set; }
    public int PendingDay { get; private set; }
    public FarmEventDef Active { get; private set; }
    public bool Running { get; private set; }
    public int StormsSurvived { get; private set; }
    public int Pesticide { get; private set; }

    private int firstDay, lastDay;
    private float startP, endP;
    private readonly HashSet<string> closedVents = new HashSet<string>();
    private readonly HashSet<string> exposed = new HashSet<string>();
    private readonly Dictionary<int, float> fertile = new Dictionary<int, float>();
    private readonly List<VentSwitch> vents = new List<VentSwitch>();
    private Contract buyerContract;
    private bool fogSaved, prevFog; private float prevDensity; private Color prevFogColor; private FogMode prevFogMode;

    private static readonly Func<string> Line = () => Instance != null ? Instance.HudLine() : null;

    private void Awake()
    {
        Instance = this;
        FarmModifiers.Reset();
        LoadData();
        Load();
        ShopCatalog.Register(new ShopEntry
        {
            id = "pesticide", category = "Supplies",
            name = () => FinText.R("Locust pesticide"),
            detail = () => Instance != null ? "(" + Instance.Pesticide + ")" : "",
            price = () => 350,
            onBought = () => { if (Instance != null) { Instance.Pesticide++; Instance.Save(); } }
        });
        if (!HudLines.Status.Contains(Line)) HudLines.Status.Add(Line);
    }

    private void LoadData()
    {
        var json = Resources.Load<TextAsset>("Data/events");
        try { if (json != null) Data = JsonUtility.FromJson<FarmEventFile>(json.text); }
        catch (Exception e) { Debug.LogError("[Events] Invalid events.json: " + e.Message); }
        if (Data == null) Data = new FarmEventFile();
        if (Data.events == null) Data.events = new FarmEventDef[0];
    }

    public FarmEventDef Find(string id) => Array.Find(Data.events, e => e.id == id);

    private void OnEnable()
    {
        WorldClock.DayStarted += OnNewDay;
        GameEvents.NewGameStarted += OnNewGame;
        FieldPlot.FieldHarvested += OnFieldHarvested;
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnNewDay;
        GameEvents.NewGameStarted -= OnNewGame;
        FieldPlot.FieldHarvested -= OnFieldHarvested;
        RestoreFog();
        FarmModifiers.Reset();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------------- schedule ----------------

    private void OnNewDay(int day)
    {
        if (Active != null && day > lastDay) EndActive();
        if (Pending != null && PendingDay <= day)
        {
            var p = Pending; Pending = null;
            if (Active == null) Activate(p, day, false);
        }
        if (RandomEventsEnabled && Pending == null && (Active == null || lastDay <= day) && UnityEngine.Random.value < Data.dailyChance)
        {
            var next = Roll(day + 1);
            if (next != null) Schedule(next.id, day + 1);
        }
        Save();
    }

    /// <summary>Season a given day falls in (same formula as WorldClock).</summary>
    public static Season SeasonOf(int day)
    {
        int dps = WorldClock.Instance != null ? Mathf.Max(1, WorldClock.Instance.daysPerSeason) : 5;
        return (Season)(((day - 1) / dps) % 4);
    }

    /// <summary>Weighted pick of an event allowed on that day's season.</summary>
    public FarmEventDef Roll(int day)
    {
        string season = WorldClock.SeasonId(SeasonOf(day));
        var ok = new List<FarmEventDef>(); float total = 0f;
        foreach (var e in Data.events)
        {
            if (day < e.minDay || e.weight <= 0f) continue;
            if (e.seasons != null && e.seasons.Length > 0 && Array.IndexOf(e.seasons, season) < 0) continue;
            ok.Add(e); total += e.weight;
        }
        float r = UnityEngine.Random.value * total;
        foreach (var e in ok) { r -= e.weight; if (r <= 0f) return e; }
        return ok.Count > 0 ? ok[ok.Count - 1] : null;
    }

    /// <summary>Forecast an event for a day (normally tomorrow) and warn the player.</summary>
    public bool Schedule(string id, int day)
    {
        var e = Find(id);
        if (e == null) return false;
        Pending = e; PendingDay = day;
        Save();
        CancelInvoke(nameof(ShowForecast));
        Invoke(nameof(ShowForecast), 2.5f);   // after the clock's own "Day N" alert
        return true;
    }

    private void ShowForecast()
    {
        if (Pending == null) return;
        FinText.Alert(Loc.Display(FinText.R("Forecast for tomorrow") + ": " + Txt(Pending.nameEn, Pending.nameAr) + "\n" + Txt(Pending.warnEn, Pending.warnAr)));
    }

    /// <summary>Start an event right now (debug / tests).</summary>
    public bool StartNow(string id)
    {
        var e = Find(id);
        if (e == null || WorldClock.Instance == null) return false;
        if (Active != null) EndActive();
        Activate(e, WorldClock.Instance.Day, true);
        return true;
    }

    private void Activate(FarmEventDef e, int day, bool now)
    {
        Active = e; Running = false; exposed.Clear();
        firstDay = day; lastDay = day + Mathf.Max(1, e.days) - 1;
        float clockP = WorldClock.Instance != null ? WorldClock.Instance.DayProgress : 0f;
        startP = now ? clockP : Mathf.Repeat(e.startHour - 6f, 24f) / 24f;
        endP = e.hours > 0f ? Mathf.Min(1f, startP + e.hours / 24f) : 1f;
        if (e.days > 1 && e.hours <= 0f) endP = 1f;
        Save();
    }

    // ---------------- running ----------------

    private void Update()
    {
        var clock = WorldClock.Instance;
        if (vents.Count == 0) BuildVents();
        if (Active == null || clock == null) { Neutral(); return; }
        int day = clock.Day; float p = clock.DayProgress;
        bool afterStart = day > firstDay || (day == firstDay && p >= startP);
        bool beforeEnd = day < lastDay || (day == lastDay && p < endP);
        if (!Running && afterStart && beforeEnd) Begin();
        if (!beforeEnd) { EndActive(); return; }
        if (Running) Apply(Active, day, p);
        else Neutral();
    }

    private void Neutral()
    {
        FarmModifiers.FieldDry = 1f; FarmModifiers.PenWaterUse = 1f; FarmModifiers.IrrigationBanned = false;
        FarmModifiers.FrostGrowth = 1f;
        if (FarmModifiers.FrostCrops.Count > 0) FarmModifiers.FrostCrops.Clear();
        if (FarmModifiers.PriceMul.Count > 0) FarmModifiers.PriceMul.Clear();
    }

    private void Apply(FarmEventDef e, int day, float p)
    {
        Neutral();
        FarmModifiers.FieldDry = e.fieldDry;
        FarmModifiers.PenWaterUse = e.penWater;
        FarmModifiers.IrrigationBanned = e.type == "waterBan";
        if (e.type == "frost" && e.crops != null) { foreach (var c in e.crops) FarmModifiers.FrostCrops.Add(c); FarmModifiers.FrostGrowth = e.growth; }
        if (e.type == "demand" && e.goods != null) foreach (var g in e.goods) FarmModifiers.PriceMul[g] = e.priceMul;
        if (e.type == "haboob")
        {
            var gm = GameManager.Instance;
            if (gm != null) foreach (var gh in gm.Greenhouses) if (gh != null && !IsClosed(gh)) exposed.Add(gh.name);
            // Fog thickens over the first part of the storm and thins at the end.
            float span = Mathf.Max(0.001f, endP - startP), t = Mathf.Clamp01((p - startP) / span);
            float k = Mathf.Clamp01(Mathf.Min(t, 1f - t) * 6f);
            RenderSettings.fogDensity = Mathf.Lerp(prevDensity, e.fog, k);
        }
    }

    private void Begin()
    {
        var e = Active;
        Running = true;
        Save();
        string extra = "";
        switch (e.type)
        {
            case "haboob":
                prevFog = RenderSettings.fog; prevDensity = RenderSettings.fogDensity; prevFogColor = RenderSettings.fogColor; prevFogMode = RenderSettings.fogMode;
                fogSaved = true;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogColor = new Color(0.78f, 0.62f, 0.42f);
                break;
            case "flood": extra = Flood(e); break;
            case "locust": extra = Locusts(e); break;
            case "buyer": extra = BigBuyer(e); break;
        }
        FinText.Alert(Loc.Display(Txt(e.startEn, e.startAr)) + (extra != "" ? "\n" + extra : ""));
        GameEvents.RaiseCustom("farmEvent", 1f);
    }

    /// <summary>End the active event now (also called when its time is up).</summary>
    public void EndActive()
    {
        var e = Active;
        if (e == null) return;
        if (!Running) Begin();   // skipped past it (e.g. day jump): instant effects still happen
        Active = null; Running = false;
        string msg = Loc.Display(Txt(e.nameEn, e.nameAr) + " - " + FinText.R("over"));
        if (e.type == "haboob")
        {
            RestoreFog();
            msg += "\n" + StormDamage();
        }
        if (e.type == "buyer" && buyerContract != null && MarketManager.Instance != null)
            MarketManager.Instance.Contracts.Remove(buyerContract);
        buyerContract = null;
        Neutral();
        Save();
        FinText.Alert(msg);
    }

    private void RestoreFog()
    {
        if (!fogSaved) return;
        fogSaved = false;
        RenderSettings.fog = prevFog; RenderSettings.fogDensity = prevDensity; RenderSettings.fogColor = prevFogColor; RenderSettings.fogMode = prevFogMode;
    }

    private string StormDamage()
    {
        int beds = 0;
        var gm = GameManager.Instance;
        if (gm != null)
            foreach (var gh in gm.Greenhouses)
                if (gh != null && exposed.Contains(gh.name))
                    foreach (var b in gh.Cropbeds) if (b != null && b.IsActive && b.Growth > 0.01f) { b.ResetGrowth(); beds++; }
        // Sand dries the open fields.
        if (FieldManager.Instance != null)
            foreach (var p in FieldManager.Instance.Plots) if (p != null && p.State == PlotState.Growing) p.AddWater(-1f);
        bool safe = exposed.Count == 0;
        exposed.Clear();
        if (safe)
        {
            StormsSurvived++;
            GameEvents.RaiseCustom("surviveDustStorm", 1f);
            return FinText.T("Greenhouse vents were closed - no damage. Fields need water.");
        }
        return FinText.T("Sand got into the open greenhouses: {0} beds lost their growth", beds);
    }

    private string Flood(FarmEventDef e)
    {
        var fm = FieldManager.Instance;
        if (fm == null || fm.Plots.Count == 0) return "";
        var list = new List<FieldPlot>();
        foreach (var p in fm.Plots) if (p != null) list.Add(p);
        // Lowest plots flood first: prefer ones with a crop on them (that's what hurts), then random.
        list.Sort((a, b) => (b.State != PlotState.Untilled ? 1 : 0).CompareTo(a.State != PlotState.Untilled ? 1 : 0));
        int n = Mathf.Min(e.plots, list.Count), lost = 0;
        for (int i = 0; i < n; i++)
        {
            var p = list[i];
            if (p.State == PlotState.Growing || p.State == PlotState.Ripe) lost++;
            p.ResetPlot();
            fertile[p.index] = e.fertility;
        }
        Save();
        return FinText.T("{0} plots flooded ({1} crops lost). The silt will make their next harvest bigger.", n, lost);
    }

    private string Locusts(FarmEventDef e)
    {
        if (Pesticide > 0)
        {
            Pesticide--; Save();
            return FinText.T("You sprayed pesticide - the crops are safe");
        }
        int lost = 0;
        if (FieldManager.Instance != null)
            foreach (var p in FieldManager.Instance.Plots)
                if (p != null && (p.State == PlotState.Growing || p.State == PlotState.Ripe) && UnityEngine.Random.value < e.lossChance) { p.ResetPlot(); lost++; }
        return FinText.T("Locusts ate {0} field crops. Keep pesticide in stock!", lost);
    }

    private string BigBuyer(FarmEventDef e)
    {
        var eco = EconomyManager.Instance; var mm = MarketManager.Instance;
        if (eco == null || mm == null || eco.Data.goods.Length == 0) return "";
        GoodDef best = null; float bestValue = -1f;
        foreach (var g in eco.Data.goods)
        {
            float v = eco.Stored(g.id) * g.price;
            if (v > bestValue) { best = g; bestValue = v; }
        }
        if (bestValue <= 0f) best = eco.Data.goods[UnityEngine.Random.Range(0, eco.Data.goods.Length)];
        float amount = best.weight < 0.5f ? e.amount * 4f : e.amount;
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        buyerContract = new Contract { buyerEn = e.buyerEn, buyerAr = e.buyerAr, good = best.id, amount = amount, bonus = e.bonus, dueDay = today };
        mm.Contracts.Add(buyerContract);
        return Loc.Display(FinText.R("Wants") + " " + amount.ToString("F0") + " " + EconomyManager.Unit(best) + " " + EconomyManager.GoodName(best) + " x" + e.bonus.ToString("F1") + " - " + FinText.R("today only"));
    }

    private void OnFieldHarvested(FieldPlot plot, string crop, float kg)
    {
        float bonus;
        if (plot == null || !fertile.TryGetValue(plot.index, out bonus)) return;
        fertile.Remove(plot.index);
        Save();
        float extra = kg * (bonus - 1f);
        if (extra <= 0f || EconomyManager.Instance == null) return;
        EconomyManager.Instance.AddGood(crop, extra);
        FinText.Alert(FinText.T("Wadi silt bonus: +{0} kg", extra.ToString("F0")));
    }

    public bool IsFertile(int plotIndex) => fertile.ContainsKey(plotIndex);

    // ---------------- greenhouse vents ----------------

    /// <summary>True when a dust storm is forecast for tomorrow or is on today.</summary>
    public bool StormSoon => (Active != null && Active.type == "haboob") || (Pending != null && Pending.type == "haboob");

    public bool IsClosed(GreenhouseManager gh) => gh != null && closedVents.Contains(gh.name);

    public void SetClosed(GreenhouseManager gh, bool closed)
    {
        if (gh == null) return;
        if (closed) closedVents.Add(gh.name); else closedVents.Remove(gh.name);
        Save();
        foreach (var v in vents) if (v != null && v.greenhouse == gh) v.Refresh();
    }

    private void BuildVents()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.Greenhouses.Count == 0) return;
        foreach (var gh in gm.Greenhouses)
        {
            if (gh == null) continue;
            var st = gh.GetComponentInChildren<ControllStation>();
            Transform at = st != null ? st.transform : gh.transform;
            vents.Add(VentSwitch.Create(gh, at.position + at.right * 1.8f, at.rotation));
        }
    }

    // ---------------- HUD / save ----------------

    private string HudLine()
    {
        string s = null;
        var clock = WorldClock.Instance;
        if (Active != null && Running && clock != null)
        {
            float left = ((lastDay - clock.Day) + endP - clock.DayProgress) * 24f;
            s = Txt(Active.nameEn, Active.nameAr) + " - " + FinText.R("now") + (Active.hours > 0f || Active.days <= 1 ? " (" + Mathf.Max(0f, left).ToString("F0") + " " + FinText.R("h left") + ")" : "");
        }
        else if (Active != null)
            s = Txt(Active.nameEn, Active.nameAr) + " - " + FinText.R("today at") + " " + Mathf.FloorToInt(Mathf.Repeat(6f + startP * 24f, 24f)).ToString("00") + ":00";
        else if (Pending != null)
            s = FinText.R("Forecast for tomorrow") + ": " + Txt(Pending.nameEn, Pending.nameAr);
        if (s == null) return closedVents.Count > 0 ? Loc.Display(FinText.R("Greenhouse vents closed")) : null;
        return "<color=#FFB347>" + Loc.Display(s) + "</color>";
    }

    private static string Txt(string en, string ar) => Loc.IsArabic && !string.IsNullOrEmpty(ar) ? ar : (en ?? "");

    private void OnNewGame()
    {
        if (Active != null && Active.type == "haboob") RestoreFog();
        if (buyerContract != null && MarketManager.Instance != null) MarketManager.Instance.Contracts.Remove(buyerContract);
        Pending = null; Active = null; Running = false; buyerContract = null;
        closedVents.Clear(); exposed.Clear(); fertile.Clear(); StormsSurvived = 0; Pesticide = 0;
        Neutral();
        Save();
        foreach (var v in vents) if (v != null) v.Refresh();
    }

    private void Save()
    {
        SaveData.SetString("mazraa.events.pending", Pending != null ? Pending.id : "");
        SaveData.SetInt("mazraa.events.pendingDay", PendingDay);
        SaveData.SetString("mazraa.events.active", Active != null ? Active.id : "");
        SaveData.SetInt("mazraa.events.running", Running ? 1 : 0);
        SaveData.SetInt("mazraa.events.firstDay", firstDay);
        SaveData.SetInt("mazraa.events.lastDay", lastDay);
        SaveData.SetFloat("mazraa.events.startP", startP);
        SaveData.SetFloat("mazraa.events.endP", endP);
        SaveData.SetString("mazraa.events.vents", string.Join(",", new List<string>(closedVents).ToArray()));
        var f = new List<string>();
        foreach (var kv in fertile) f.Add(kv.Key + "=" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SaveData.SetString("mazraa.events.fertile", string.Join(";", f.ToArray()));
        SaveData.SetInt("mazraa.events.pesticide", Pesticide);
        SaveData.SetInt("mazraa.events.stormsSurvived", StormsSurvived);
        SaveData.Save();
    }

    private void Load()
    {
        Pending = Find(SaveData.GetString("mazraa.events.pending", ""));
        PendingDay = SaveData.GetInt("mazraa.events.pendingDay", 0);
        Active = Find(SaveData.GetString("mazraa.events.active", ""));
        firstDay = SaveData.GetInt("mazraa.events.firstDay", 0);
        lastDay = SaveData.GetInt("mazraa.events.lastDay", 0);
        startP = SaveData.GetFloat("mazraa.events.startP", 0f);
        endP = SaveData.GetFloat("mazraa.events.endP", 1f);
        // An event that had already begun resumes without repeating its one-off effects (fog is set up again).
        if (Active != null && SaveData.GetInt("mazraa.events.running", 0) == 1)
        {
            Running = true;
            if (Active.type == "haboob")
            {
                prevFog = RenderSettings.fog; prevDensity = RenderSettings.fogDensity; prevFogColor = RenderSettings.fogColor; prevFogMode = RenderSettings.fogMode; fogSaved = true;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogColor = new Color(0.78f, 0.62f, 0.42f);
            }
        }
        foreach (var n in SaveData.GetString("mazraa.events.vents", "").Split(',')) if (n.Length > 0) closedVents.Add(n);
        foreach (var part in SaveData.GetString("mazraa.events.fertile", "").Split(';'))
        {
            var kv = part.Split('='); int i; float b;
            if (kv.Length == 2 && int.TryParse(kv[0], out i) && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b)) fertile[i] = b;
        }
        Pesticide = SaveData.GetInt("mazraa.events.pesticide", 0);
        StormsSurvived = SaveData.GetInt("mazraa.events.stormsSurvived", 0);
    }
}
