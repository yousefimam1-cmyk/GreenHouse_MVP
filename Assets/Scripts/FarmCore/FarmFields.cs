using System;
using System.Collections.Generic;
using System.Globalization;
using StarterAssets;
using UnityEngine;

/// <summary>What the fields show on the map and minimap.</summary>
public enum FieldMapLayer { Crop, Moisture, Ground, Yield }

/// <summary>The combine's grain tank (tractor + harvester): fills while harvesting, empties into farm storage at the loading bay.</summary>
public class HarvestTank
{
    public string crop = "";
    public float kg, capacity = 2500f;
    public float Free => Mathf.Max(0f, capacity - kg);
    public bool Full => kg >= capacity - 0.5f;
}

/// <summary>
/// FarmCore manager: every field of the farm (the farm's own fields, land office parcels, land bought in the open world) as
/// real areas of ~2 m cells, the growth simulation in in-game time (FarmSim, also over skipped hours), field work painted by
/// tractor implements and hand tools (FieldWork), seed and fertiliser supplies, the combine tank, harvest events for missions
/// (FieldEvents), soil tests, worker jobs and saving (one run-length encoded string per field, mazraa.fc.*).
/// Replaces the old FieldPlot / FieldManager / SoilManager plots (save version 1: older field saves start fresh).
/// Co-op: the host simulates and sends compact cell deltas (FarmNet); a client's field work is forwarded to the host.
/// </summary>
public class FarmFields : MonoBehaviour
{
    public static FarmFields Instance { get; private set; }
    public const string P = "mazraa.fc.";
    public const int SaveVersion = 1;
    public const float StepHours = 0.25f;
    public const string KitId = "kit";
    /// <summary>Field ids of land bought in the open world start here (WorldLayout parcels: 200+, Delta 300+).</summary>
    public const int WorldFieldBase = 200;

    public readonly List<FarmField> Fields = new List<FarmField>();
    public Vector3 GridCenter { get; private set; }
    public Quaternion GridRotation { get; private set; } = Quaternion.identity;
    public bool Ready { get; private set; }

    // ---- farm layout (two fields where the old 3 x 2 plot grid was; land office parcels extend it)
    public const float FarmFieldWidth = 10f, FarmFieldDepth = 14f, FarmFieldGap = 1.2f, OfficeGap = 1.6f;
    public static float FarmBlockWidth => FarmFieldWidth * 2f + FarmFieldGap;

    // ---- settings (Fields page)
    /// <summary>Crop the seeder sows ("" = the best seed you own).</summary>
    public string SowCrop { get; private set; } = "";
    /// <summary>Last seed bought in the shop (sown first when owned).</summary>
    public static string PreferredSeed = "";
    public string SpreadProduct { get; private set; } = "urea";
    public string SprayProduct { get; private set; } = "herbicide";
    /// <summary>Sowing density for the next plantings: 0 low, 1 normal, 2 high.</summary>
    public int Density { get; private set; } = 1;
    public FieldMapLayer MapLayer { get; private set; }

    public HarvestTank Tank { get; } = new HarvestTank();
    public float SimHours { get; private set; }
    public float LastStepMs { get; private set; }

    private readonly Dictionary<string, float> seedM2 = new Dictionary<string, float>();
    private readonly Dictionary<string, float> productM2 = new Dictionary<string, float>();
    private readonly Dictionary<string, int> items = new Dictionary<string, int>();
    private class Cut { public float kg, cells; }
    private readonly Dictionary<string, Cut> pending = new Dictionary<string, Cut>();   // "fieldId|crop" -> kg and cells cut this pass
    private readonly Dictionary<int, float> lastWork = new Dictionary<int, float>();         // field id -> time of last harvest work
    private readonly Dictionary<FieldTool, float> unitM2 = new Dictionary<FieldTool, float>();
    private float hourAccum, hoursToday, saveTimer = 10f, flushTimer;
    private bool started;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        Instance = this;
        MigrateOldSaves();
        LoadGlobals();
        RegisterShop();
    }

    private void OnEnable()
    {
        WorldClock.DayStarted += OnDay;
        GameEvents.NewGameStarted += OnNewGame;
        GameEvents.RegionChanged += OnRegion;
        GameEvents.UpgradeBought += OnBought;
        if (!HudLines.Status.Contains(hudLine)) HudLines.Status.Add(hudLine);
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.NewGameStarted -= OnNewGame;
        GameEvents.RegionChanged -= OnRegion;
        GameEvents.UpgradeBought -= OnBought;
        SaveAll();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnApplicationQuit() => SaveAll();

    private void Start()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform start = inputs != null ? inputs.transform : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var spot = GameObject.Find("FieldsSpot");
        GridCenter = spot != null ? spot.transform.position : start.position + fwd * 18f;
        GridRotation = Quaternion.LookRotation(fwd);
        float off = (FarmFieldWidth + FarmFieldGap) * 0.5f;
        CreateField(1, "Field 1", Build.OnGround(GridCenter + GridRotation * new Vector3(-off, 0f, 0f)), GridRotation, FarmFieldWidth, FarmFieldDepth, "farm");
        CreateField(2, "Field 2", Build.OnGround(GridCenter + GridRotation * new Vector3(off, 0f, 0f)), GridRotation, FarmFieldWidth, FarmFieldDepth, "farm");
        FarmFieldRenderer.Ensure();
        hoursToday = WorldClock.Instance != null ? WorldClock.Instance.DayProgress * 24f : 0f;   // hours of today already behind us
        Ready = true;
        started = true;
    }

    // ------------------------------------------------------------------ fields

    public FarmField Find(int id) => Fields.Find(f => f.id == id);

    /// <summary>The field under a world point (or within pad metres of it), or null.</summary>
    public FarmField FieldAt(Vector3 p, float pad = 0f)
    {
        foreach (var f in Fields) if (f.Contains(p, pad)) return f;
        return null;
    }

    public bool OnField(Vector3 p) => FieldAt(p) != null;

    public float TotalHectares { get { float h = 0f; foreach (var f in Fields) h += f.Hectares; return h; } }

    /// <summary>Create a field (its saved cells if any, else fresh soil of the region) with its scene object.</summary>
    public FarmField CreateField(int id, string name, Vector3 center, Quaternion rot, float width, float depth, string source)
    {
        var old = Find(id);
        if (old != null) RemoveField(old, false);
        var f = new FarmField(id, name, center, rot.eulerAngles.y, width, depth) { source = source };
        if (!f.DecodeCells(SaveData.GetString(P + "f." + id, ""))) f.ResetSoil(FcData.CurrentRegion);
        else f.DecodeMeta(SaveData.GetString(P + "m." + id, ""));
        f.SaveDirty = false;
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, f.yaw, 0f));
        var view = go.AddComponent<FarmFieldObject>();
        view.Setup(f);
        f.View = view;
        Fields.Add(f);
        return f;
    }

    /// <summary>Take a field off the farm (land sold back, world rebuilt). forget = also delete its saved cells.</summary>
    public void RemoveField(FarmField f, bool forget)
    {
        if (f == null) return;
        if (!forget) SaveField(f);
        else { SaveData.DeleteKey(P + "f." + f.id); SaveData.DeleteKey(P + "m." + f.id); }
        Fields.Remove(f);
        if (f.View != null) Destroy(f.View.gameObject);
        f.View = null;
        if (FarmFieldRenderer.Instance != null) FarmFieldRenderer.Instance.Forget(f);
    }

    /// <summary>A land office parcel beside the farm block (k = 0..3; the last, large parcel is twice as deep).</summary>
    public FarmField AddOfficeField(int k, bool large)
    {
        float depth = large ? FarmFieldDepth : 6f;
        float z = FarmFieldDepth * 0.5f + OfficeGap;
        for (int j = 0; j < k; j++) z += 6f + OfficeGap;
        z += depth * 0.5f;
        Vector3 c = Build.OnGround(GridCenter + GridRotation * new Vector3(0f, 0f, z));
        return CreateField(10 + k, "Field " + (3 + k), c, GridRotation, FarmBlockWidth, depth, "office");
    }

    /// <summary>A parcel bought in the open world becomes one field covering its plots.</summary>
    public FarmField AddWorldField(LandParcel p, Vector3 groundCenter)
    {
        float w = p.cols * (p.plotSize + p.gap) - p.gap, d = p.rows * (p.plotSize + p.gap) - p.gap;
        return CreateField(p.indexBase, "Field " + (p.indexBase % 100 + (p.indexBase >= 300 ? 30 : 20)), groundCenter, p.rot, w, d, "world:" + p.id);
    }

    // ------------------------------------------------------------------ simulation

    private void Update()
    {
        if (!started) return;
        var clock = WorldClock.Instance;
        float spd = clock != null ? Mathf.Max(10f, clock.secondsPerDay) : 1440f;
        if (!NetSession.IsClient)
        {
            float dh = Time.deltaTime / spd * 24f;
            hourAccum += dh;
            if (hourAccum >= StepHours) { float h = hourAccum; hourAccum = 0f; Simulate(h); hoursToday += h; }
            flushTimer -= Time.deltaTime;
            if (flushTimer <= 0f) { flushTimer = 0.5f; FlushHarvests(false); }
            AutoUnload();
        }
        saveTimer -= Time.deltaTime;
        if (saveTimer <= 0f) { saveTimer = 10f; SaveDirty(); }
    }

    /// <summary>Advance every field by some in-game hours: irrigation first (network water reaches the cells), then growth.</summary>
    public void Simulate(float hours)
    {
        if (hours <= 0f) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var env = FarmEnv.Now();
        if (IrrigationSystem.Instance != null) IrrigationSystem.Instance.Step(hours, env);
        foreach (var f in Fields) FarmSim.Step(f, hours, env, FieldPests.GrowthMultiplier(f));
        SimHours += hours;
        sw.Stop();
        LastStepMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Simulate a longer stretch in coarse steps (a skipped day, sleeping).</summary>
    public void SimulateLong(float hours, float step = 2f)
    {
        while (hours > 0.001f) { float h = Mathf.Min(step, hours); Simulate(h); hours -= h; }
    }

    private void OnDay(int day)
    {
        if (NetSession.IsClient) return;
        // a day skipped (NextDay, sleeping): grow the hours that were not simulated
        float missing = 24f - hoursToday - hourAccum;
        if (started && missing > 1f) SimulateLong(missing);
        hourAccum = 0f;
        hoursToday = WorldClock.Instance != null ? WorldClock.Instance.DayProgress * 24f : 0f;
        var env = FarmEnv.Now();
        foreach (var f in Fields) FarmSim.Daily(f, env);
        FieldPests.OnDay(this);
        if (IrrigationSystem.Instance != null) IrrigationSystem.Instance.OnDay(day);
        SaveDirty();
    }

    // ------------------------------------------------------------------ field work

    /// <summary>A fresh work context with the farm's settings and supplies.</summary>
    public WorkContext NewContext(FieldTool tool, bool byHand)
    {
        var c = WorldClock.Instance;
        return new WorkContext
        {
            tool = tool, byHand = byHand, machine = !byHand,
            hot = c != null && (c.Season == Season.Summer || c.Weather == Weather.Heatwave),
            crop = tool == FieldTool.Seeder || tool == FieldTool.HandSow ? CropToSow() : "",
            product = tool == FieldTool.Sprayer ? SprayProduct : tool == FieldTool.Spreader ? SpreadProduct : "",
            density = Density,
            plowingRequired = GameSettings.PlowingRequired,
            takeSeed = TakeSeed,
            takeProduct = TakeProduct,
        };
    }

    /// <summary>
    /// Work the strip from a to b with a tool (one frame of a tractor pass, or a hand job). Changes every field the strip touches,
    /// raises the field events, fills the combine tank (tractor) or the farm storage (by hand). On a co-op client the work is sent
    /// to the host instead.
    /// </summary>
    public WorkContext Work(FieldTool tool, Vector3 a, Vector3 b, float width, bool byHand, bool tractor, WorkContext ctx = null)
    {
        ctx = ctx ?? NewContext(tool, byHand);
        if (NetSession.IsClient) { FarmNet.ForwardWork(tool, a, b, width, byHand, tractor, ctx); return ctx; }
        if (tool == FieldTool.Harvester && tractor)
        {
            ctx.capacityKg = Tank.Free;
            if (Tank.kg > 0.5f && !string.IsNullOrEmpty(Tank.crop))
            {
                // one crop per tank: a different ripe crop needs an empty tank
                var f0 = FieldAt(b, width);
                int i0 = f0 != null ? f0.CellAt(b) : -1;
                if (i0 >= 0 && FarmSim.Ripe(f0, i0) && f0.CropId(i0) != Tank.crop) { ctx.blocked = "Unload the tank first: it holds another crop"; return ctx; }
            }
        }
        foreach (var f in Fields.ToArray())
        {
            if (!f.Contains(a, width + f.cellSize) && !f.Contains(b, width + f.cellSize)) continue;
            int before = ctx.cells;
            var cutBefore = ctx.harvested.Count > 0 ? new Dictionary<string, float>(ctx.harvested) : null;
            FieldWork.Paint(f, a, b, width, ctx);
            int changed = ctx.cells - before;
            if (changed > 0) AfterWork(f, tool, changed, ctx, tractor, Diff(ctx.harvested, cutBefore));
        }
        StoreHarvest(ctx, tractor);
        return ctx;
    }

    /// <summary>What a context cut on one field: its harvest now minus before the field was painted (a strip can cross two fields).</summary>
    private static Dictionary<string, float> Diff(Dictionary<string, float> now, Dictionary<string, float> before)
    {
        if (before == null) return now;
        var d = new Dictionary<string, float>();
        foreach (var kv in now) { float b; before.TryGetValue(kv.Key, out b); if (kv.Value - b > 1e-5f) d[kv.Key] = kv.Value - b; }
        return d;
    }

    private void AfterWork(FarmField f, FieldTool tool, int changed, WorkContext ctx, bool tractor, Dictionary<string, float> cut = null)
    {
        cut = cut ?? ctx.harvested;
        if ((tool == FieldTool.Seeder || tool == FieldTool.HandSow) && ctx.sown.Count > 0)
            foreach (var crop in ctx.sown.Keys) if (f.plantedCrop != crop && SownOn(f, crop)) Planted(f, crop);
        if (cut.Count > 0)
        {
            lastWork[f.id] = Time.time;
            float total = 0f; foreach (var v0 in cut.Values) total += v0;
            total = Mathf.Max(0.001f, total);
            foreach (var kv in cut)
            {
                string k = f.id + "|" + kv.Key; Cut v;
                if (!pending.TryGetValue(k, out v)) pending[k] = v = new Cut();
                v.kg += kv.Value; v.cells += changed * kv.Value / total;
            }
        }
        if (ctx.pestCured) FieldPests.Cured(f, ctx.product);
        FieldEvents.RaiseWorked(f, tool, changed);
        if (tractor)
        {
            float m2; unitM2.TryGetValue(tool, out m2);
            m2 += changed * f.CellArea;
            var imp = FarmTools.ImplementOf(tool);
            while (m2 >= FieldEvents.WorkUnitM2) { m2 -= FieldEvents.WorkUnitM2; if (imp != Implement.None) GameEvents.RaiseTractorWorked(imp); }
            unitM2[tool] = m2;
        }
        if (NetSession.IsHost && MultiplayerManager.Instance != null) MultiplayerManager.Instance.MarkSyncDirty();
    }

    private static bool SownOn(FarmField f, string crop)
    {
        byte ci = FcData.CropIndex(crop);
        for (int i = 0; i < f.Cells; i++) if (f.crop[i] == ci && f.ground[i] == (byte)Ground.Sown) return true;
        return false;
    }

    /// <summary>A field got its first cells of a crop this cycle: crop rotation, pests reset, mission events.</summary>
    private void Planted(FarmField f, string crop)
    {
        string fam = FcData.Family(crop), prevFam = string.IsNullOrEmpty(f.lastCrop) ? "" : FcData.Family(f.lastCrop);
        f.plantedCrop = crop;
        f.rotation = FcData.Rotation(prevFam, fam);
        f.density = Density;
        f.cuts = 0; f.pest = ""; f.pestDamage = 0f; f.pestSprayedCells = 0; f.cycleWaterM3 = 0f; f.cycleCost = 0f;
        bool rotated = prevFam != "" && prevFam != fam && f.rotation > 1.001f;
        if (rotated) { GameEvents.RaiseCustom("cropsRotate", 1f); FcText.Say(FcText.R("Crop rotation") + ": " + RotationText(f.rotation) + " " + FcText.R("yield")); }
        else if (f.rotation < 0.999f) FcText.Say(FcText.R("Same crop family again: soil-borne disease") + " " + RotationText(f.rotation) + " " + FcText.R("yield"));
        if (crop == "alfalfa") GameEvents.RaiseCustom("cropsPlantAlfalfa", 1f);
        FieldEvents.RaisePlanted(f, crop);
    }

    public static string RotationText(float mult)
    {
        int pct = Mathf.RoundToInt((mult - 1f) * 100f);
        return (pct >= 0 ? "+" : "") + pct + "%";
    }

    private void StoreHarvest(WorkContext ctx, bool tractor)
    {
        if (ctx.harvested.Count == 0) return;
        var eco = EconomyManager.Instance;
        if (ctx.feed > 0f && eco != null) eco.AddFeed(ctx.feed);
        bool toTank = tractor && ctx.tool == FieldTool.Harvester;
        foreach (var kv in ctx.harvested)
        {
            if (toTank) { Tank.crop = kv.Key; Tank.kg += kv.Value; }
            else if (eco != null) eco.AddGood(kv.Key, kv.Value);
        }
        if (toTank && Tank.Full) FcText.Say(FcText.R("The harvester tank is full - unload it at the loading bay"));
    }

    /// <summary>Raise the harvest event of every finished pass (force = all pending, e.g. hand jobs and tests).</summary>
    public void FlushHarvests(bool force)
    {
        if (pending.Count == 0) return;
        foreach (var key in new List<string>(pending.Keys))
        {
            int bar = key.IndexOf('|');
            int id = int.Parse(key.Substring(0, bar), CultureInfo.InvariantCulture);
            string crop = key.Substring(bar + 1);
            var f = Find(id);
            float last;
            bool idle = !lastWork.TryGetValue(id, out last) || Time.time - last > 1.5f;
            if (!force && f != null && !idle && RipeCells(f, crop) > 0) continue;
            Cut cut = pending[key];
            pending.Remove(key);
            if (cut.kg <= 0.01f) continue;
            if (f != null) Harvested(f, crop, cut.kg, cut.cells);
        }
    }

    private static int RipeCells(FarmField f, string crop)
    {
        byte ci = FcData.CropIndex(crop); int n = 0;
        for (int i = 0; i < f.Cells; i++) if (f.crop[i] == ci && FarmSim.Ripe(f, i)) n++;
        return n;
    }

    private void Harvested(FarmField f, string crop, float kg, float cells)
    {
        var c = FcData.Crop(crop);
        bool multi = c != null && c.cuts > 1;
        if (multi) f.cuts++;
        bool cleared = true;
        byte ci = FcData.CropIndex(crop);
        for (int i = 0; i < f.Cells; i++) if (f.crop[i] == ci) { cleared = false; break; }
        var s = FarmSim.Summarize(f);
        float potential = c != null ? c.yieldT * 1000f * f.CellHa * Mathf.Max(1f, cells) : kg;
        int pct = potential > 0.01f ? Mathf.RoundToInt(kg / potential * 100f) : 100;
        var clock = WorldClock.Instance;
        AddHistory(f, (clock != null ? clock.Day : 0) + "|" + crop + "|" + kg.ToString("F0", CultureInfo.InvariantCulture) + "|" + Mathf.Clamp(pct, 0, 999) + "|" +
                      Mathf.RoundToInt(s.n) + "|" + f.cycleWaterM3.ToString("F1", CultureInfo.InvariantCulture) + "|" + Mathf.RoundToInt(f.cycleCost) + "|" + s.limiting);
        if (cleared) { f.lastCrop = crop; f.plantedCrop = ""; f.cuts = 0; f.pest = ""; f.pestDamage = 0f; f.rotation = 1f; }
        f.SaveDirty = true;
        if (GameManager.Instance != null) GameManager.Instance.RegisterFieldHarvest(kg);
        if (f.View != null) FeedbackFX.Float(f.View.transform.position + Vector3.up * 1.6f, "+" + kg.ToString("F0") + " kg", new Color(0.95f, 0.85f, 0.4f));
        FcText.Say(FcText.R("Harvested") + " " + kg.ToString("F0") + " " + FcText.R("kg") + " " + FcText.CropName(crop) + " - " + FieldName(f));
        FieldEvents.RaiseHarvested(f, crop, kg);
    }

    public const int HistoryMax = 6;

    private static void AddHistory(FarmField f, string rec)
    {
        var list = new List<string>(string.IsNullOrEmpty(f.history) ? new string[0] : f.history.Split(';'));
        list.Add(rec);
        while (list.Count > HistoryMax) list.RemoveAt(0);
        f.history = string.Join(";", list.ToArray());
    }

    // ------------------------------------------------------------------ combine tank

    /// <summary>Empty the combine tank into farm storage. Returns kg moved.</summary>
    public float UnloadTank()
    {
        if (Tank.kg <= 0.01f) return 0f;
        float kg = Tank.kg;
        var eco = EconomyManager.Instance;
        if (eco != null && !string.IsNullOrEmpty(Tank.crop)) eco.AddGood(Tank.crop, kg);
        string crop = Tank.crop;
        Tank.kg = 0f; Tank.crop = "";
        FcText.Say(FcText.R("Harvester tank unloaded") + ": " + kg.ToString("F0") + " " + FcText.R("kg") + " " + FcText.CropName(crop));
        SaveGlobals();
        return kg;
    }

    private void AutoUnload()
    {
        if (Tank.kg <= 0.01f) return;
        var t = Tractor.Instance;
        var bay = DeliveryZone.LoadingBay;
        if (t == null || bay == null) return;
        Vector3 d = t.transform.position - bay.transform.position; d.y = 0f;
        if (d.magnitude < bay.radius + 6f) UnloadTank();
    }

    // ------------------------------------------------------------------ hand work (E on a field, field hands)

    public enum HandAction { None, Harvest, Water, Sow, Hoe, Sample, Spread, Leach, Treat }

    public struct HandPlan { public HandAction action; public string detail; }

    public const float HandRadius = 3.2f;

    /// <summary>What a hand job at this point would do (the prompt of the field). Workers skip the soil actions.</summary>
    public HandPlan PlanHand(FarmField f, Vector3 at, bool worker)
    {
        var plan = new HandPlan();
        if (f == null) return plan;
        int ripe = 0, dry = 0, sowable = 0, rough = 0, sown = 0;
        bool plowing = GameSettings.PlowingRequired;
        foreach (int i in CellsNear(f, at, HandRadius))
        {
            var g = (Ground)f.ground[i];
            if (FarmSim.Ripe(f, i)) { var c = FarmSim.CropOf(f.crop[i]); if (c != null) ripe++; }
            if (g == Ground.Sown && !f.Has(i, CellFlags.Withered)) { sown++; if (f.Get(FieldLayer.Moisture, i) < 0.4f) dry++; }
            if (g == Ground.Cultivated || g == Ground.Ploughed || (!plowing && (g == Ground.Untilled || g == Ground.Stubble))) sowable++;
            if (g == Ground.Untilled || g == Ground.Stubble || (g == Ground.Sown && f.Has(i, CellFlags.Withered))) rough++;
        }
        if (!worker && FieldPests.OwnedCure(f) != null) return new HandPlan { action = HandAction.Treat, detail = FieldPests.OwnedCure(f).id };
        if (ripe > 0) return new HandPlan { action = HandAction.Harvest };
        if (!worker && Items(KitId) > 0 && !Sampled(f) && sown == 0) return new HandPlan { action = HandAction.Sample };
        if (dry > 0 && !FarmModifiers.IrrigationBanned && WellRunning()) return new HandPlan { action = HandAction.Water };   // water from the well by hand
        if (sowable > 0 && !string.IsNullOrEmpty(CropToSow())) return new HandPlan { action = HandAction.Sow, detail = CropToSow() };
        if (!worker)
        {
            var a = HandAmendment(f);
            if (a != null && sown == 0) return new HandPlan { action = HandAction.Spread, detail = a.id };
            if (Sampled(f) && FarmSim.Summarize(f).ec > 4f && sown == 0 && !FarmModifiers.IrrigationBanned) return new HandPlan { action = HandAction.Leach };
        }
        if (rough > 0 && (worker ? HasAnySeed() : true)) return new HandPlan { action = HandAction.Hoe };
        return plan;
    }

    /// <summary>Do the next job by hand on the cells around a point (~6 x 6 m, the size of an old plot). Returns the translated result.</summary>
    public string HandJob(Vector3 at, bool worker = false)
    {
        var f = FieldAt(at, 0.5f);
        if (f == null) return FcText.T("Nothing to do here");
        var plan = PlanHand(f, at, worker);
        WorkContext ctx;
        switch (plan.action)
        {
            case HandAction.Harvest: ctx = HandWork(f, at, FieldTool.HandHarvest); FlushHarvests(true); return ctx.cells > 0 ? null : Blocked(ctx);
            case HandAction.Water: ctx = HandWork(f, at, FieldTool.WateringCan, 25f); return ctx.cells > 0 ? FcText.T("Field watered") : Blocked(ctx);
            case HandAction.Sow: ctx = HandWork(f, at, FieldTool.HandSow); return ctx.cells > 0 ? Loc.Display(FcText.R("Planted") + " " + FcText.CropName(plan.detail) + (InSeasonNow(plan.detail) ? "" : " - " + FcText.R("Out of season - slow"))) : Blocked(ctx);
            case HandAction.Hoe: ctx = HandWork(f, at, FieldTool.Hoe); return ctx.cells > 0 ? FcText.T("Field plowed by hand") : Blocked(ctx);
            case HandAction.Sample: return TakeSample(f) ? null : FcText.T("Nothing to do here");
            case HandAction.Spread: ctx = HandWork(f, at, FieldTool.Spreader, 0f, plan.detail); if (ctx.cells > 0) { GameEvents.RaiseCustom("soilAmend", 1f); return Loc.Display(FcData.AmendName(FcData.Amendment(plan.detail)) + " - " + FieldName(f)); } return Blocked(ctx);
            case HandAction.Leach: return LeachSalts(f) ? null : FcText.T("Nothing to do here");
            case HandAction.Treat: return FieldPests.TreatByHand(f) ? null : FcText.T("Nothing to do here");
        }
        return FcText.T("Nothing to do here");
    }

    private static string Blocked(WorkContext ctx) => FcText.T(ctx.blocked ?? "Nothing to do here");

    /// <summary>A hand tool (hoe, watering can, sowing, hand harvest) on the cells around a point.</summary>
    public WorkContext HandTool(FieldTool tool, Vector3 at, float waterMm = 0f)
    {
        var f = FieldAt(at, 0.5f);
        if (f == null) return NewContext(tool, true);
        var ctx = HandWork(f, at, tool, waterMm);
        if (tool == FieldTool.HandHarvest) FlushHarvests(true);
        return ctx;
    }

    private WorkContext HandWork(FarmField f, Vector3 at, FieldTool tool, float waterMm = 0f, string product = null)
    {
        var ctx = NewContext(tool, true);
        if (waterMm > 0f) ctx.waterMm = waterMm;
        if (product != null) ctx.product = product;
        if (NetSession.IsClient) { FarmNet.ForwardWork(tool, at, at, HandRadius * 2f, true, false, ctx); ctx.cells = 1; return ctx; }
        foreach (int i in CellsNear(f, at, HandRadius))
            if (FieldWork.ApplyCell(f, i, ctx)) { ctx.cells++; f.Touch(i); }
        if (ctx.cells > 0) AfterWork(f, tool, ctx.cells, ctx, false);
        StoreHarvest(ctx, false);
        return ctx;
    }

    /// <summary>Cells whose centre is within r metres of a point.</summary>
    public static List<int> CellsNear(FarmField f, Vector3 at, float r)
    {
        var list = new List<int>();
        Vector3 l = f.ToLocal(at);
        int cx0 = Mathf.Max(0, Mathf.FloorToInt((l.x - r + f.width * 0.5f) / f.cellSize)), cx1 = Mathf.Min(f.cols - 1, Mathf.FloorToInt((l.x + r + f.width * 0.5f) / f.cellSize));
        int cz0 = Mathf.Max(0, Mathf.FloorToInt((l.z - r + f.depth * 0.5f) / f.cellSize)), cz1 = Mathf.Min(f.rows - 1, Mathf.FloorToInt((l.z + r + f.depth * 0.5f) / f.cellSize));
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                Vector3 c = f.LocalCellCenter(cz * f.cols + cx);
                float dx = c.x - l.x, dz = c.z - l.z;
                if (dx * dx + dz * dz <= r * r) list.Add(cz * f.cols + cx);
            }
        return list;
    }

    /// <summary>A hired field hand's job: the most useful patch on any field. True when something was done.</summary>
    public bool WorkerJob(out Vector3 where)
    {
        where = GridCenter;
        FarmField best = null; Vector3 bestAt = Vector3.zero; int bestRank = 0;
        foreach (var f in Fields)
        {
            for (int i = 0; i < f.Cells; i += 3)
            {
                int rank = 0;
                var g = (Ground)f.ground[i];
                if (FarmSim.Ripe(f, i)) rank = 4;
                else if (g == Ground.Sown && !f.Has(i, CellFlags.Withered) && f.Get(FieldLayer.Moisture, i) < 0.35f && !FarmModifiers.IrrigationBanned && WellRunning()) rank = 3;
                else if ((g == Ground.Cultivated || g == Ground.Ploughed) && HasAnySeed()) rank = 2;
                else if ((g == Ground.Untilled || g == Ground.Stubble) && HasAnySeed()) rank = 1;
                if (rank > bestRank) { bestRank = rank; best = f; bestAt = f.CellCenter(i); }
            }
        }
        if (best == null) return false;
        where = bestAt;
        int before = Total(best);
        HandJob(bestAt, true);
        return Total(best) != before || bestRank >= 3;
    }

    private static int Total(FarmField f) { int v = f.Version; return v; }

    private static bool WellRunning()
    {
        var wells = FindObjectsByType<WellController>(FindObjectsSortMode.None);
        if (wells.Length == 0) return true;
        foreach (var w in wells) if (w.IsRunning) return true;
        return false;
    }

    // ------------------------------------------------------------------ soil actions

    public bool Sampled(FarmField f)
    {
        var c = WorldClock.Instance;
        if (c == null) return f.sampledDay > -1000;
        return c.Day - f.sampledDay < Mathf.Max(1, c.daysPerSeason);
    }

    /// <summary>Take a soil sample (uses a kit): the field's soil shows on the overlay and the Fields page for the rest of the season.</summary>
    public bool TakeSample(FarmField f)
    {
        if (f == null || !UseItem(KitId)) return false;
        f.sampledDay = WorldClock.Instance != null ? WorldClock.Instance.Day : 0;
        f.SaveDirty = true;
        GameEvents.RaiseCustom("soilSample", 1f);
        var s = FarmSim.Summarize(f);
        FcText.Say(FieldName(f) + " - " + FcText.R("Soil test") + ": " + TestLine(s));
        return true;
    }

    public bool LeachSalts(FarmField f)
    {
        if (f == null || FarmModifiers.IrrigationBanned) return false;
        float before = FarmSim.Summarize(f).ec;
        for (int i = 0; i < f.Cells; i++) { FarmAgronomy.Leach(f, i); f.Touch(i); }
        float m3 = FcData.File.leachMm * f.Area / 1000f;
        f.cycleWaterM3 += m3;
        if (IrrigationSystem.Instance != null) IrrigationSystem.Instance.BookExtraWater(m3);
        GameEvents.RaiseCustom("soilLeach", 1f);
        FcText.Say(FcText.R("Salts leached") + ": EC " + before.ToString("F1") + " -> " + FarmSim.Summarize(f).ec.ToString("F1") + " dS/m");
        return true;
    }

    private FcAmendDef HandAmendment(FarmField f)
    {
        bool sampled = Sampled(f);
        var s = sampled ? FarmSim.Summarize(f) : default(FieldSummary);
        foreach (var a in FcData.File.amendments)
        {
            if (!a.spread || Bags(a.id) <= 0) continue;
            if (a.mulch || a.herbicide || a.pesticide) continue;
            if (!sampled) return a;
            if ((a.n > 0f && s.n < 150f) || (a.p > 0f && s.p < 45f) || (a.k > 0f && s.k < 180f) || (a.om > 0f && f.om < 2f) || (a.ec < 0f && s.ec > 2.5f) || (a.ph < 0f && s.ph > 7.6f) || (a.ph > 0f && s.ph < 6f)) return a;
        }
        return null;
    }

    public static string TestLine(FieldSummary s)
    {
        return "N " + s.n.ToString("F0") + " · P " + s.p.ToString("F0") + " · K " + s.k.ToString("F0") + " kg/ha · pH " + s.ph.ToString("F1") + " · EC " + s.ec.ToString("F1") + " dS/m · " +
               FcText.R("Compaction") + " " + FcText.Pct(s.compaction);
    }

    // ------------------------------------------------------------------ supplies: seeds, products, soil items

    public static bool InSeasonNow(string crop) => WorldClock.Instance == null || FcData.InSowingWindow(crop, WorldClock.Instance.Season);

    /// <summary>The crop the seeder sows now: the one chosen on the Fields page, else the last seed bought, else the first in-season seed owned.</summary>
    public string CropToSow()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return "";
        if (!string.IsNullOrEmpty(SowCrop) && (eco.Seeds(SowCrop) > 0 || SeedLeft(SowCrop) > 0f)) return SowCrop;
        if (!string.IsNullOrEmpty(PreferredSeed) && (eco.Seeds(PreferredSeed) > 0 || SeedLeft(PreferredSeed) > 0f) && EconomyManager.Available(eco.FieldCrop(PreferredSeed))) return PreferredSeed;
        string fallback = "";
        foreach (var c in eco.Data.fieldCrops)
        {
            if ((eco.Seeds(c.id) <= 0 && SeedLeft(c.id) <= 0f) || !EconomyManager.Available(c)) continue;
            if (InSeasonNow(c.id)) return c.id;
            if (fallback == "") fallback = c.id;
        }
        return fallback;
    }

    public bool HasAnySeed() => !string.IsNullOrEmpty(CropToSow());

    public float SeedLeft(string crop) { float v; return seedM2.TryGetValue(crop ?? "", out v) ? v : 0f; }
    public static float BagM2(string crop) { var c = FcData.Crop(crop); return (c != null ? c.seedBagHa : 0.01f) * 10000f; }

    /// <summary>Seed for m2 of a crop: from the open bag, opening a new one (economy seed bags) when it runs out.</summary>
    public bool TakeSeed(string crop, float m2)
    {
        var eco = EconomyManager.Instance;
        float left = SeedLeft(crop);
        while (left < m2 - 0.001f)
        {
            if (eco == null || !eco.UseSeed(crop)) return false;
            left += BagM2(crop);
        }
        seedM2[crop] = left - m2;
        return true;
    }

    public float ProductLeft(string id) { float v; return productM2.TryGetValue(id ?? "", out v) ? v : 0f; }

    /// <summary>Product for m2: from the opened bag, opening another one you own when it runs out.</summary>
    public bool TakeProduct(string id, float m2)
    {
        var a = FcData.Amendment(id);
        if (a == null) return false;
        float left = ProductLeft(id);
        while (left < m2 - 0.001f)
        {
            if (!UseBag(id)) return false;
            left += Mathf.Max(0.001f, a.bagHa) * 10000f;
        }
        productM2[id] = left - m2;
        return true;
    }

    /// <summary>Bags of a product owned (crop care items for fertilizer, manure and pest sprays; soil items otherwise).</summary>
    public int Bags(string id)
    {
        var a = FcData.Amendment(id);
        if (a != null && !string.IsNullOrEmpty(a.careId)) return CropCareManager.Instance != null ? CropCareManager.Instance.Items(a.careId) : 0;
        return Items(id);
    }

    public bool UseBag(string id)
    {
        var a = FcData.Amendment(id);
        if (a != null && !string.IsNullOrEmpty(a.careId)) return CropCareManager.Instance != null && CropCareManager.Instance.UseItem(a.careId);
        return UseItem(id);
    }

    public int Items(string id) { int n; return items.TryGetValue(id, out n) ? n : 0; }
    public void AddItem(string id, int n = 1) { items[id] = Items(id) + n; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save(); }
    public bool UseItem(string id)
    {
        if (Items(id) <= 0) return false;
        items[id]--; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save();
        return true;
    }

    private void RegisterShop()
    {
        ShopCatalog.Register(new ShopEntry
        {
            id = "soil_" + KitId, category = "Soil",
            name = () => FcText.R("Soil sample kit (lab test)"),
            detail = () => "(" + (Instance != null ? Instance.Items(KitId) : 0) + ")",
            price = () => FcData.File.kitPrice,
            onBought = () => { if (Instance != null) Instance.AddItem(KitId); },
        });
        foreach (var amend in FcData.File.amendments)
        {
            if (!amend.shop) continue;
            var a = amend;
            ShopCatalog.Register(new ShopEntry
            {
                id = "soil_" + a.id, category = "Soil",
                name = () => FcData.AmendName(a),
                detail = () => "(" + (Instance != null ? Instance.Items(a.id) : 0) + ") " + FcText.Area(a.bagHa * 10000f) + (a.manureKg > 0f ? " · " + a.manureKg.ToString("F0") + " kg " + FcText.R("manure") : ""),
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

    private void OnBought(string id)
    {
        if (id != null && id.StartsWith("seed_")) PreferredSeed = id.Substring(5);
    }

    // ------------------------------------------------------------------ settings (Fields page)

    public List<string> SowOptions()
    {
        var list = new List<string> { "" };
        var eco = EconomyManager.Instance;
        if (eco != null) foreach (var c in eco.Data.fieldCrops) if (EconomyManager.Available(c)) list.Add(c.id);
        return list;
    }

    public void CycleSowCrop(int dir) { var o = SowOptions(); SowCrop = o[(Mathf.Max(0, o.IndexOf(SowCrop)) + dir + o.Count) % o.Count]; SaveGlobals(); }

    public List<string> ProductOptions(bool spray)
    {
        var list = new List<string>();
        foreach (var a in FcData.File.amendments) if (spray ? a.spray : a.spread) list.Add(a.id);
        return list;
    }

    public void CycleProduct(bool spray, int dir)
    {
        var o = ProductOptions(spray);
        if (o.Count == 0) return;
        string cur = spray ? SprayProduct : SpreadProduct;
        string next = o[(Mathf.Max(0, o.IndexOf(cur)) + dir + o.Count) % o.Count];
        if (spray) SprayProduct = next; else SpreadProduct = next;
        SaveGlobals();
    }

    public void SetProduct(bool spray, string id) { if (spray) SprayProduct = id; else SpreadProduct = id; SaveGlobals(); }
    public void SetSowCrop(string id) { SowCrop = id ?? ""; SaveGlobals(); }

    public void CycleDensity(int dir)
    {
        int d = (Density + dir + 3) % 3;
        if (NetSession.IsClient) { FarmNet.ForwardSetting("d", d.ToString(CultureInfo.InvariantCulture)); Density = d; return; }
        SetDensity(d);
    }

    public void SetDensity(int d) { Density = Mathf.Clamp(d, 0, 2); SaveGlobals(); }
    /// <summary>Co-op client: the host's density (not saved).</summary>
    public void SetDensityLocal(int d) => Density = Mathf.Clamp(d, 0, 2);

    public void CycleMapLayer(int dir)
    {
        MapLayer = (FieldMapLayer)(((int)MapLayer + dir + 4) % 4);
        foreach (var f in Fields) if (f.View != null) f.View.RefreshIcon();
        SaveGlobals();
    }

    public static string DensityText(int d) => FcText.R(FarmAgronomy.DensityNames[Mathf.Clamp(d, 0, 2)]) + " (" + FcText.R("seed") + " " + Mathf.RoundToInt(FarmAgronomy.DensitySeed[Mathf.Clamp(d, 0, 2)] * 100f) + "%)";
    public static string FieldName(FarmField f) => f == null ? "" : FcText.R("Field") + " " + f.name.Replace("Field ", "");

    // ------------------------------------------------------------------ save

    public void SaveField(FarmField f)
    {
        if (f == null || NetSession.IsClient) return;
        SaveData.SetString(P + "f." + f.id, f.EncodeCells());
        SaveData.SetString(P + "m." + f.id, f.EncodeMeta());
        f.SaveDirty = false;
        SaveData.Save();
    }

    public void SaveDirty()
    {
        foreach (var f in Fields) if (f.SaveDirty) SaveField(f);
        SaveGlobals();
    }

    public void SaveAll()
    {
        foreach (var f in Fields) SaveField(f);
        SaveGlobals();
    }

    private static string Pack(Dictionary<string, float> d)
    {
        var parts = new List<string>();
        foreach (var kv in d) if (kv.Value > 0.001f) parts.Add(kv.Key + "=" + kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        return string.Join(",", parts.ToArray());
    }

    private static void Unpack(string s, Dictionary<string, float> d)
    {
        d.Clear();
        if (string.IsNullOrEmpty(s)) return;
        foreach (var kv in s.Split(','))
        {
            int eq = kv.IndexOf('=');
            float v;
            if (eq > 0 && float.TryParse(kv.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) d[kv.Substring(0, eq)] = v;
        }
    }

    private void SaveGlobals()
    {
        if (NetSession.IsClient) return;
        SaveData.SetInt(P + "version", SaveVersion);
        SaveData.SetString(P + "sow", SowCrop);
        SaveData.SetString(P + "spread", SpreadProduct);
        SaveData.SetString(P + "spray", SprayProduct);
        SaveData.SetInt(P + "density", Density);
        SaveData.SetInt(P + "layer", (int)MapLayer);
        SaveData.SetString(P + "seedM2", Pack(seedM2));
        SaveData.SetString(P + "productM2", Pack(productM2));
        SaveData.SetString(P + "tank", Tank.crop + "=" + Tank.kg.ToString("0.##", CultureInfo.InvariantCulture));
        SaveData.Save();
    }

    private void LoadGlobals()
    {
        items.Clear();
        items[KitId] = SaveData.GetInt(P + "item." + KitId, 0);
        foreach (var a in FcData.File.amendments) if (a.shop) items[a.id] = SaveData.GetInt(P + "item." + a.id, 0);
        SowCrop = SaveData.GetString(P + "sow", "");
        SpreadProduct = SaveData.GetString(P + "spread", "urea");
        SprayProduct = SaveData.GetString(P + "spray", "herbicide");
        Density = Mathf.Clamp(SaveData.GetInt(P + "density", 1), 0, 2);
        MapLayer = (FieldMapLayer)Mathf.Clamp(SaveData.GetInt(P + "layer", 0), 0, 3);
        Unpack(SaveData.GetString(P + "seedM2", ""), seedM2);
        Unpack(SaveData.GetString(P + "productM2", ""), productM2);
        var tank = new Dictionary<string, float>();
        Unpack(SaveData.GetString(P + "tank", ""), tank);
        Tank.crop = ""; Tank.kg = 0f;
        foreach (var kv in tank) { Tank.crop = kv.Key; Tank.kg = kv.Value; }
        Tank.capacity = Mathf.Max(100f, FcData.Tool(FieldTool.Harvester).tankKg > 0f ? FcData.Tool(FieldTool.Harvester).tankKg : 2500f);
    }

    /// <summary>Beta: field saves from before FarmCore (plots, plot soil, plot crop care) are dropped; the new fields start fresh.</summary>
    private static void MigrateOldSaves()
    {
        if (SaveData.GetInt(P + "version", 0) >= SaveVersion) return;
        string[] plotKeys = { "state", "crop", "growth", "water" };
        string[] careKeys = { "fert", "dmg", "last", "pest", "rot", "prot", "cuts", "rotm" };
        for (int i = 0; i < 460; i++)
        {
            if (i > 40 && i < 200) continue;
            foreach (var k in plotKeys) if (SaveData.HasKey("mazraa.plot." + i + "." + k)) SaveData.DeleteKey("mazraa.plot." + i + "." + k);
            foreach (var k in new[] { "p.", "h.", "c." }) if (SaveData.HasKey("mazraa.soil." + k + i)) SaveData.DeleteKey("mazraa.soil." + k + i);
            foreach (var k in careKeys) if (SaveData.HasKey("mazraa.crops.plot." + i + "." + k)) SaveData.DeleteKey("mazraa.crops.plot." + i + "." + k);
        }
        SaveData.SetInt(P + "version", SaveVersion);
    }

    // ------------------------------------------------------------------ new game / region

    private void ClearState()
    {
        foreach (var k in new List<string>(items.Keys)) { items[k] = 0; SaveData.SetInt(P + "item." + k, 0); }
        seedM2.Clear(); productM2.Clear(); pending.Clear(); lastWork.Clear(); unitM2.Clear();
        Tank.kg = 0f; Tank.crop = "";
        SowCrop = ""; SpreadProduct = "urea"; SprayProduct = "herbicide"; Density = 1; MapLayer = FieldMapLayer.Crop;
        PreferredSeed = "";
        hourAccum = 0f; hoursToday = 0f;
    }

    private void OnNewGame()
    {
        ClearState();
        // land office and open-world fields go back on sale; the farm's own fields start with fresh soil
        foreach (var f in Fields.ToArray()) if (f.source != "farm") RemoveField(f, true);
        foreach (var f in Fields) { f.ResetSoil(FcData.CurrentRegion); SaveField(f); }
        SaveGlobals();
    }

    // A new career on another map: its own starting soil (Delta clay, Najd sand) and climate.
    private void OnRegion(string regionId)
    {
        foreach (var f in Fields) { f.ResetSoil(FcData.CurrentRegion); SaveField(f); }
    }

    /// <summary>Co-op client left the host's farm: back to the own fields from the own save.</summary>
    public void ReloadLocal()
    {
        foreach (var f in Fields)
        {
            if (!f.DecodeCells(SaveData.GetString(P + "f." + f.id, ""))) f.ResetSoil(FcData.CurrentRegion);
            else f.DecodeMeta(SaveData.GetString(P + "m." + f.id, ""));
        }
        LoadGlobals();
    }

    // ------------------------------------------------------------------ HUD

    private static readonly Func<string> hudLine = () => Instance != null ? Instance.HudLine() : null;

    private string HudLine()
    {
        if (Tank.kg <= 0.5f || !(Vehicle.Current is Tractor)) return null;
        return Loc.Display(FcText.R("Harvester tank") + ": " + Tank.kg.ToString("F0") + " / " + Tank.capacity.ToString("F0") + " " + FcText.R("kg") + " " + FcText.CropName(Tank.crop));
    }
}
