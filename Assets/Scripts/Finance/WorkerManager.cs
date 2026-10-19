using System;
using System.Collections.Generic;
using UnityEngine;

public enum WorkerRole { FieldHand, GreenhouseWorker, Herder, Driver }

/// <summary>
/// Hired workers (shop page "Workers"). Each costs a daily wage in SAR, paid every in-game morning;
/// if the farm can't pay, that worker leaves with a warning. They do one job every few seconds:
///  - field hand: harvests ripe plots, waters dry ones (well running, no water ban), plows and replants if seeds are in stock
///  - greenhouse worker: harvests ripe beds, closes the vents when a dust storm is coming
///  - herder: fills the trough, cleans the pen, collects products
///  - driver: when storage has goods, takes a load to the market (timer) and pays you the sale minus a fee
/// Visible NPCs (Actor, Worker mode) walk to the task; the logic doesn't need them.
/// Saved under mazraa.fin.workers.*.
/// </summary>
[MazraaSystem(41)]
public class WorkerManager : MonoBehaviour
{
    public static WorkerManager Instance;

    public class WorkerDef { public WorkerRole role; public string id, nameKey, model; public int wage; }
    public static readonly WorkerDef[] Defs =
    {
        new WorkerDef { role = WorkerRole.FieldHand, id = "fieldhand", nameKey = "Field hand", wage = 180, model = "Characters/character-male-c" },
        new WorkerDef { role = WorkerRole.GreenhouseWorker, id = "ghworker", nameKey = "Greenhouse worker", wage = 150, model = "Characters/character-female-b" },
        new WorkerDef { role = WorkerRole.Herder, id = "herder", nameKey = "Herder", wage = 150, model = "Characters/character-male-a" },
        new WorkerDef { role = WorkerRole.Driver, id = "driver", nameKey = "Driver", wage = 220, model = "Characters/character-female-d" },
    };

    public float workInterval = 4f;          // seconds between jobs
    public float tripSeconds = 60f;          // driver round trip to the market
    public const float DriverFee = 0.15f;    // driver keeps 15% of the sale
    public const float DriverMinLoad = 20f;  // kg in storage before a trip

    private readonly HashSet<WorkerRole> hired = new HashSet<WorkerRole>();
    private readonly Dictionary<WorkerRole, float> timers = new Dictionary<WorkerRole, float>();
    private readonly Dictionary<WorkerRole, Actor> actors = new Dictionary<WorkerRole, Actor>();
    public readonly Dictionary<string, float> TripCargo = new Dictionary<string, float>();
    public float TripLeft { get; private set; }
    public int JobsDone { get; private set; }
    public int LastTripEarned { get; private set; }

    private static readonly Func<string> Line = () => Instance != null ? Instance.HudLine() : null;

    public bool IsHired(WorkerRole r) => hired.Contains(r);
    public int HiredCount => hired.Count;
    public int DailyWages { get { int w = 0; foreach (var d in Defs) if (hired.Contains(d.role)) w += d.wage; return w; } }
    public static WorkerDef Def(WorkerRole r) => Array.Find(Defs, d => d.role == r);

    private void Awake()
    {
        Instance = this;
        foreach (var d in Defs) if (SaveData.GetInt("mazraa.fin.workers." + d.id, 0) == 1) hired.Add(d.role);
        TripLeft = SaveData.GetFloat("mazraa.fin.workers.trip", 0f);
        foreach (var part in SaveData.GetString("mazraa.fin.workers.cargo", "").Split(';'))
        {
            var kv = part.Split('=');
            float v;
            if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0f) TripCargo[kv[0]] = v;
        }
        foreach (var d in Defs) RegisterShop(d);
        if (!HudLines.Status.Contains(Line)) HudLines.Status.Add(Line);
    }

    private static void RegisterShop(WorkerDef d)
    {
        WorkerRole role = d.role;
        ShopCatalog.Register(new ShopEntry
        {
            id = "worker_" + d.id, category = "Workers",
            name = () => FinText.R(Instance != null && Instance.IsHired(role) ? "Dismiss" : "Hire") + ": " + FinText.R(d.nameKey),
            detail = () => "(" + FinText.Sar(d.wage) + "/" + FinText.R("day") + ")",
            // Hiring pays the first day's wage up front; dismissing is free.
            price = () => Instance != null && Instance.IsHired(role) ? 0 : d.wage,
            onBought = () => { if (Instance != null) { if (Instance.IsHired(role)) Instance.Dismiss(role, false); else Instance.Hire(role); } }
        });
    }

    private void OnEnable() { WorldClock.DayStarted += OnNewDay; GameEvents.NewGameStarted += OnNewGame; }
    private void OnDisable() { WorldClock.DayStarted -= OnNewDay; GameEvents.NewGameStarted -= OnNewGame; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------------- hiring ----------------

    public void Hire(WorkerRole r)
    {
        if (!hired.Add(r)) return;
        timers[r] = workInterval;
        Save();
        GameEvents.RaiseCustom("hireWorker", 1f);
        FinText.Alert(FinText.T("{0} hired - wage {1} SAR a day", FinText.R(Def(r).nameKey), Def(r).wage));
    }

    public void Dismiss(WorkerRole r, bool unpaid)
    {
        if (!hired.Remove(r)) return;
        Actor a;
        if (actors.TryGetValue(r, out a) && a != null) Destroy(a.gameObject);
        actors.Remove(r);
        if (r == WorkerRole.Driver) FinishTrip(); // a load on the road is still sold
        Save();
        FinText.Alert(unpaid ? FinText.T("Couldn't pay the {0} - they left the farm", FinText.R(Def(r).nameKey)) : FinText.T("{0} dismissed", FinText.R(Def(r).nameKey)));
    }

    private void OnNewDay(int day)
    {
        if (NetSession.IsClient) return; // co-op: the host runs daily costs and events
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var d in Defs)
        {
            if (!hired.Contains(d.role)) continue;
            bool paid; using (Ledger.As(Ledger.Wages)) paid = eco.Spend(d.wage);
            if (!paid) Dismiss(d.role, true);
        }
    }

    private void OnNewGame()
    {
        foreach (var a in actors.Values) if (a != null) Destroy(a.gameObject);
        actors.Clear();
        hired.Clear(); TripCargo.Clear(); TripLeft = 0f; JobsDone = 0;
        Save();
    }

    // ---------------- work ----------------

    private void Update()
    {
        if (hired.Count == 0) return;
        float dt = Time.deltaTime;
        if (TripLeft > 0f)
        {
            TripLeft -= dt;
            if (TripLeft <= 0f) FinishTrip();
        }
        foreach (var d in Defs)
        {
            if (!hired.Contains(d.role)) continue;
            EnsureActor(d);
            float t;
            timers.TryGetValue(d.role, out t);
            t -= dt;
            if (t <= 0f) { t = workInterval; if (Work(d.role)) JobsDone++; }
            timers[d.role] = t;
        }
    }

    /// <summary>Do one job for this worker. Returns true if something was done.</summary>
    public bool Work(WorkerRole r)
    {
        switch (r)
        {
            case WorkerRole.FieldHand: return FieldJob();
            case WorkerRole.GreenhouseWorker: return GreenhouseJob();
            case WorkerRole.Herder: return HerdJob();
            default: return DriverJob();
        }
    }

    private bool FieldJob()
    {
        // FarmCore: the field hand works the most useful ~6 x 6 m patch by hand (harvest, water, sow, hoe)
        var ff = FarmFields.Instance;
        if (ff == null) return false;
        Vector3 where;
        bool done = ff.WorkerJob(out where);
        GoTo(WorkerRole.FieldHand, where);
        return done;
    }

    private bool GreenhouseJob()
    {
        var gm = GameManager.Instance;
        if (gm == null) return false;
        var ed = EventDirector.Instance;
        if (ed != null && ed.StormSoon)
            foreach (var gh in gm.Greenhouses)
                if (gh != null && !ed.IsClosed(gh)) { GoTo(WorkerRole.GreenhouseWorker, gh.transform.position); ed.SetClosed(gh, true); return true; }
        foreach (var gh in gm.Greenhouses)
        {
            if (gh == null) continue;
            foreach (var bed in gh.Cropbeds)
                if (bed != null && bed.IsReadyToHarvest)
                {
                    GoTo(WorkerRole.GreenhouseWorker, bed.transform.position);
                    bed.Interact();
                    return true;
                }
        }
        return false;
    }

    private bool HerdJob()
    {
        var pen = AnimalPen.Instance;
        if (pen == null || pen.AnimalCount == 0) return false;
        GoTo(WorkerRole.Herder, pen.transform.position);
        if (pen.WaterLevel < 0.5f && WellRunning()) { pen.FillWater(); return true; }
        if (pen.Cleanliness < 0.5f) { pen.Clean(); return true; }
        foreach (var kv in pen.Waiting) if (kv.Value >= 1f) { pen.Collect(); return true; }
        return false;
    }

    private bool DriverJob()
    {
        var eco = EconomyManager.Instance;
        if (eco == null || TripLeft > 0f || eco.StorageWeight < DriverMinLoad) return false;
        float moved = eco.LoadInto(TripCargo, eco.TruckCapacity);
        if (moved <= 0.01f) return false;
        TripLeft = tripSeconds;
        Actor a;
        if (actors.TryGetValue(WorkerRole.Driver, out a) && a != null) a.gameObject.SetActive(false);
        Save();
        FinText.Alert(FinText.T("The driver took {0} kg to the market", moved.ToString("F0")));
        return true;
    }

    private void FinishTrip()
    {
        TripLeft = 0f;
        var eco = EconomyManager.Instance;
        if (eco == null || TripCargo.Count == 0) { Save(); return; }
        var notes = new List<string>();
        int earned = eco.SellAll(TripCargo, notes);
        int fee = Mathf.RoundToInt(earned * DriverFee);
        using (Ledger.As(Ledger.Wages)) eco.AddMoney(-fee);
        LastTripEarned = earned - fee;
        Actor a;
        if (actors.TryGetValue(WorkerRole.Driver, out a) && a != null) a.gameObject.SetActive(true);
        Save();
        notes.Insert(0, FinText.T("The driver sold the load: {0} SAR (fee {1} SAR)", (earned - fee).ToString("N0"), fee.ToString("N0")));
        FinText.Alert(string.Join("\n", notes));
    }

    private static bool WellRunning()
    {
        var wells = FindObjectsByType<WellController>(FindObjectsSortMode.None);
        if (wells.Length == 0) return true;
        foreach (var w in wells) if (w.IsRunning) return true;
        return false;
    }

    // ---------------- visuals ----------------

    private void EnsureActor(WorkerDef d)
    {
        if (actors.ContainsKey(d.role)) return;
        Vector3 at = Home(d.role);
        var a = Actor.Spawn(d.model, 1.5f, true, d.role == WorkerRole.Driver ? ActorMode.Stand : ActorMode.Worker, Build.OnGround(at));
        if (a != null) { a.radius = 2.5f; a.speed = 1.3f; a.name = "Worker " + d.id; }
        actors[d.role] = a;   // null when the model is missing: logic still runs
    }

    private Vector3 Home(WorkerRole r)
    {
        switch (r)
        {
            case WorkerRole.FieldHand: return FarmFields.Instance != null ? FarmFields.Instance.GridCenter : Vector3.zero;
            case WorkerRole.Herder: return AnimalPen.Instance != null ? AnimalPen.Instance.transform.position : Vector3.zero;
            case WorkerRole.Driver: return DeliveryZone.LoadingBay != null ? DeliveryZone.LoadingBay.transform.position + Vector3.right * 4f : Vector3.zero;
            default:
                var gm = GameManager.Instance;
                return gm != null && gm.Greenhouses.Count > 0 ? gm.Greenhouses[0].transform.position : Vector3.zero;
        }
    }

    private void GoTo(WorkerRole r, Vector3 pos)
    {
        Actor a;
        if (actors.TryGetValue(r, out a) && a != null) a.home = pos;
    }

    private string HudLine()
    {
        if (hired.Count == 0) return null;
        string s = FinText.R("Workers") + ": " + hired.Count + " - " + FinText.R("wages") + " " + FinText.Sar(DailyWages) + "/" + FinText.R("day");
        if (TripLeft > 0f) s += " - " + FinText.R("driver on the road");
        return Loc.Display(s);
    }

    private void Save()
    {
        foreach (var d in Defs) SaveData.SetInt("mazraa.fin.workers." + d.id, hired.Contains(d.role) ? 1 : 0);
        SaveData.SetFloat("mazraa.fin.workers.trip", TripLeft);
        var parts = new List<string>();
        foreach (var kv in TripCargo) parts.Add(kv.Key + "=" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SaveData.SetString("mazraa.fin.workers.cargo", string.Join(";", parts));
        SaveData.Save();
    }
}
