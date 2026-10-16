using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class StandLine { public string good; public float amount; }
[Serializable]
public class StandOrder
{
    public bool active;
    public string template, festival;
    public int nameIndex, coins, xp;
    public float arriveAt;
    public List<StandLine> lines = new List<StandLine>();
}
[Serializable] class StandSave { public List<StandOrder> slots = new List<StandOrder>(); public int filled; }

/// <summary>
/// Roadside stand by the farmhouse (like Hay Day's order board): named villagers stop by and order small baskets of
/// goods from the farm store for coins and XP. One visitor at first, more with farm level (and a fourth at 4 beauty
/// stars); a new visitor comes a few in-game hours after one is served or sent away. Festivals bring special orders
/// (iftar baskets in Ramadan, Eid sweets). Templates in farmlife.json "orders". Saved as mazraa.fl.orders.
/// </summary>
[MazraaSystem(76)]
public class RoadsideStand : MonoBehaviour
{
    public const string SaveKey = "mazraa.fl.orders";
    public const int MaxSlots = 4;
    public static RoadsideStand Instance { get; private set; }

    private StandSave state = new StandSave();
    private readonly Dictionary<int, GameObject> visitors = new Dictionary<int, GameObject>();
    private float timer;

    public GameObject Stand { get; private set; }
    public List<StandOrder> Slots => state.slots;
    public int Filled => state.filled;

    private void Awake()
    {
        Instance = this;
        state = FarmLife.Load<StandSave>(SaveKey);
        if (state.slots == null) state.slots = new List<StandOrder>();
        while (state.slots.Count < MaxSlots) state.slots.Add(new StandOrder());
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        GameEvents.NewGameStarted += OnNewGame;
        WorldClock.DayStarted += OnDay;
        Invoke(nameof(BuildStand), 1.8f);
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= OnNewGame;
        WorldClock.DayStarted -= OnDay;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void OnNewGame()
    {
        state = new StandSave();
        while (state.slots.Count < MaxSlots) state.slots.Add(new StandOrder());
        foreach (var v in visitors.Values) if (v != null) Destroy(v);
        visitors.Clear();
        Save();
        Tick();
    }

    private void OnDay(int day) => Tick();
    private void Save() => FarmLife.Store(SaveKey, state);

    /// <summary>Visitors waiting at the stand now: 1, +1 at the orders_slot2 / orders_slot3 levels, +1 at 4 beauty stars.</summary>
    public static int SlotCount
    {
        get
        {
            int n = 1;
            if (FarmLevel.IsUnlocked("orders_slot2")) n++;
            if (FarmLevel.IsUnlocked("orders_slot3")) n++;
            if (FarmBeauty.Stars >= 4) n++;
            return Mathf.Min(MaxSlots, n);
        }
    }

    public int ActiveCount { get { int n = 0; for (int i = 0; i < SlotCount; i++) if (state.slots[i].active) n++; return n; } }

    private static float RefreshDays => Mathf.Max(0.05f, FarmLife.Data.orders.refreshHours / 24f) * (FarmBeauty.Stars >= 3 ? 0.75f : 1f);

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = 1f;
        Tick();
    }

    /// <summary>Fill empty slots whose next visitor is due.</summary>
    public void Tick()
    {
        bool changed = false;
        for (int i = 0; i < SlotCount; i++)
        {
            var s = state.slots[i];
            if (!s.active && FarmLife.Now >= s.arriveAt) { state.slots[i] = Generate(); changed = true; }
        }
        if (changed) Save();
        SyncVisitors();
    }

    // ---------------- orders ----------------

    public static FLOrderTemplate Template(string id) => Array.Find(FarmLife.Data.orders.templates, t => t.id == id);

    private static readonly System.Random shared = new System.Random();
    private static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    /// <summary>A new order from a random villager (festival orders preferred while a festival runs).</summary>
    public StandOrder Generate(int seed = 0)
    {
        var rnd = seed != 0 ? new System.Random(seed) : shared;   // own random: doesn't shift the game's weather / event rolls
        var eco = EconomyManager.Instance;
        int level = FarmLevel.Instance != null ? FarmLevel.Instance.Level : 1;
        string festival = Festivals.Instance != null && Festivals.Instance.Today != null ? Festivals.Instance.Today.id : null;
        var pool = new List<FLOrderTemplate>();
        foreach (var t in FarmLife.Data.orders.templates)
        {
            if (t.minLevel > level || t.goods == null) continue;
            if (!string.IsNullOrEmpty(t.festival)) { if (t.festival != festival) continue; pool.Add(t); pool.Add(t); pool.Add(t); }
            pool.Add(t);
        }
        var o = new StandOrder { active = true, arriveAt = FarmLife.Now };
        if (pool.Count == 0 || eco == null) { o.active = false; o.arriveAt = FarmLife.Now + RefreshDays; return o; }
        var tpl = pool[rnd.Next(pool.Count)];
        var goods = new List<string>();
        foreach (var g in tpl.goods) if (eco.Good(g) != null) { goods.Add(g); if (eco.Stored(g) > 0.5f) { goods.Add(g); goods.Add(g); } }
        if (goods.Count == 0) { o.active = false; o.arriveAt = FarmLife.Now + RefreshDays; return o; }
        int lines = Mathf.Clamp(rnd.Next(tpl.minLines, tpl.maxLines + 1), 1, 3);
        float coins = 0f;
        for (int i = 0; i < lines && goods.Count > 0; i++)
        {
            string g = goods[rnd.Next(goods.Count)];
            goods.RemoveAll(x => x == g);
            float amount = Mathf.Round(Rand(rnd, tpl.min, tpl.max));
            o.lines.Add(new StandLine { good = g, amount = Mathf.Max(1f, amount) });
            coins += Mathf.Max(1f, amount) * eco.Good(g).price * tpl.pay;
        }
        o.template = tpl.id;
        o.festival = tpl.festival;
        o.coins = Mathf.Max(20, Mathf.RoundToInt(coins / 5f) * 5);
        o.xp = tpl.xp + Mathf.RoundToInt(o.coins / 25f);
        o.nameIndex = FarmLife.Data.orders.names.Length > 0 ? rnd.Next(FarmLife.Data.orders.names.Length) : 0;
        return o;
    }

    public static string VisitorName(StandOrder o)
    {
        var n = FarmLife.Data.orders.names;
        return n.Length > 0 ? n[Mathf.Clamp(o.nameIndex, 0, n.Length - 1)].T : Loc.Raw("Visitor");
    }

    public static string TemplateName(StandOrder o) { var t = Template(o.template); return t != null ? FarmLife.Pick(t.en, t.ar) : ""; }

    /// <summary>Coins paid now, with the beauty and crop mastery bonuses.</summary>
    public static int Pay(StandOrder o)
    {
        float mastery = 0f;
        foreach (var l in o.lines) mastery = Mathf.Max(mastery, CropMastery.OrderBonus(l.good));
        return Mathf.RoundToInt(o.coins * (1f + FarmBeauty.OrdersBonus + mastery));
    }

    public static bool CanFill(StandOrder o)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || o == null || !o.active) return false;
        foreach (var l in o.lines) if (eco.Stored(l.good) + 1e-3f < l.amount) return false;
        return true;
    }

    public static string Describe(StandOrder o)
    {
        var parts = new List<string>();
        var eco = EconomyManager.Instance;
        foreach (var l in o.lines)
        {
            float have = eco != null ? eco.Stored(l.good) : 0f;
            parts.Add(FarmLife.GoodName(l.good) + " " + l.amount.ToString("0") + " " + FarmLife.GoodUnit(l.good) + " (" + Mathf.FloorToInt(have) + ")");
        }
        return string.Join(", ", parts.ToArray());
    }

    /// <summary>Serve the visitor in a slot from the farm store. Returns the coins paid (0 = not enough goods).</summary>
    public int Fulfil(int slot)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || slot < 0 || slot >= SlotCount) return 0;
        var o = state.slots[slot];
        if (!o.active) return 0;
        if (!CanFill(o)) { CozyFx.Alert(Loc.T("Not enough in the farm store yet")); return 0; }
        foreach (var l in o.lines) eco.TakeGood(l.good, l.amount);
        int pay = Pay(o);
        eco.AddMoney(pay);
        if (FarmLevel.Instance != null) FarmLevel.Instance.AddXp(o.xp * Mathf.Max(1f, FarmLevel.Instance.Rate("villageOrder")));
        state.filled++;
        string name = VisitorName(o);
        string fest = o.festival;
        state.slots[slot] = new StandOrder { active = false, arriveAt = FarmLife.Now + RefreshDays };
        Save();
        if (visitors.TryGetValue(slot, out var v) && v != null) { CozyFx.Hearts(v.transform.position + Vector3.up * 2f); Destroy(v, 0.5f); }
        visitors.Remove(slot);
        CozyFx.Alert(Loc.Display(name + ": " + Loc.Raw("Shukran! Order delivered") + "  +" + pay + " " + Loc.Raw("SAR") + "  +" + o.xp + " " + Loc.Raw("XP")));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        GameEvents.RaiseCustom("standOrder", 1f);
        if (Festivals.Instance != null) Festivals.Instance.OnStandOrder(fest);
        return pay;
    }

    /// <summary>Send a visitor away politely; someone new comes sooner than after a served order.</summary>
    public bool Skip(int slot)
    {
        if (slot < 0 || slot >= SlotCount || !state.slots[slot].active) return false;
        state.slots[slot] = new StandOrder { active = false, arriveAt = FarmLife.Now + RefreshDays * 0.5f };
        Save();
        if (visitors.TryGetValue(slot, out var v) && v != null) Destroy(v);
        visitors.Remove(slot);
        return true;
    }

    /// <summary>Hours until the next visitor for an empty slot.</summary>
    public float HoursUntil(int slot) => slot < 0 || slot >= state.slots.Count ? 0f : Mathf.Max(0f, (state.slots[slot].arriveAt - FarmLife.Now) * 24f);

    private static string HudLine()
    {
        var s = Instance;
        if (s == null) return null;
        int ready = 0, waiting = s.ActiveCount;
        for (int i = 0; i < SlotCount; i++) if (CanFill(s.state.slots[i])) ready++;
        if (waiting == 0) return null;
        return Loc.Display(Loc.Raw("Roadside stand") + ": " + waiting + " " + Loc.Raw("waiting") + (ready > 0 ? ", " + ready + " " + Loc.Raw("ready to serve") : ""));
    }

    // ---------------- stand + visitors in the world ----------------

    private Vector3 StandPos()
    {
        var p = CozyFx.Player;
        Vector3 fwd = p != null ? Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return Build.OnGround(CozyFx.FarmStart + right * 7f + fwd * 4f);
    }

    private void BuildStand()
    {
        if (Stand != null) return;
        var root = new GameObject("Roadside Stand");
        root.transform.position = StandPos();
        var wood = Build.Mat(new Color(0.55f, 0.38f, 0.22f), 0.15f);
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 0.5f, 0), new Vector3(2.6f, 1f, 0.9f), wood, "Counter");
        for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(i % 2 == 0 ? -1.25f : 1.25f, 1.3f, i < 2 ? -0.4f : 0.4f), new Vector3(0.07f, 1.3f, 0.07f), wood, "Post");
        var cloth = Build.Mat(new Color(0.85f, 0.35f, 0.12f), 0.2f);
        for (int i = 0; i < 5; i++) Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-1.2f + i * 0.6f, 2.62f, 0), new Vector3(0.6f, 0.06f, 1.2f), i % 2 == 0 ? cloth : Build.Mat(new Color(0.95f, 0.9f, 0.8f), 0.2f), "Awning");
        var crate = Build.Mat(new Color(0.6f, 0.45f, 0.28f), 0.1f);
        var produce = new[] { new Color(0.85f, 0.2f, 0.15f), new Color(0.3f, 0.6f, 0.2f), new Color(0.95f, 0.75f, 0.25f) };
        for (int i = 0; i < 3; i++)
        {
            Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-0.8f + i * 0.8f, 1.12f, 0.05f), new Vector3(0.6f, 0.24f, 0.45f), crate, "Crate");
            Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-0.8f + i * 0.8f, 1.26f, 0.05f), new Vector3(0.52f, 0.06f, 0.38f), Build.Mat(produce[i], 0.4f), "Produce");
        }
        CozyFx.Label(root.transform, new Vector3(0, 3.2f, 0), "Roadside stand", 2.6f, new Color(1f, 0.9f, 0.6f));
        CozyFx.MakeInteractable(root, new Vector3(0, 1.2f, 0), new Vector3(2.8f, 2.6f, 1.2f), () => Loc.T("[E] Roadside stand orders"), OpenPanel);
        Stand = root;
        SyncVisitors();
    }

    private static void OpenPanel() { if (FarmLifeScreen.Instance != null) FarmLifeScreen.Instance.Show(FarmLifeScreen.Tab.Orders); }

    private static readonly string[] Models = { "Characters/character-female-b", "Characters/character-male-c", "Characters/character-female-d", "Characters/character-male-a" };

    private void SyncVisitors()
    {
        if (Stand == null) return;
        for (int i = 0; i < MaxSlots; i++)
        {
            bool want = i < SlotCount && state.slots[i].active;
            visitors.TryGetValue(i, out var go);
            if (want && go == null)
            {
                var o = state.slots[i];
                Vector3 pos = Build.OnGround(Stand.transform.position + Stand.transform.forward * 2.2f + Stand.transform.right * (-1.5f + i * 1f));
                string model = Models[o.nameIndex % Models.Length];
                var actor = Actor.Spawn(model, 1.55f, true, ActorMode.Wander, pos);
                if (actor != null) { go = actor.gameObject; actor.radius = 0.6f; actor.speed = 0.4f; }
                else { go = new GameObject("Visitor"); go.transform.position = pos; CozyFx.PrimitivePerson(go.transform, Appearance.ThobeColors[0]); }
                go.name = "Stand visitor " + i;
                AppearancePanel.DressNpc(go, model.Contains("female"), o.nameIndex);
                var order = o;
                CozyFx.DataLabel(go.transform, new Vector3(0, 2.15f, 0), () => VisitorName(order), 2f, new Color(1f, 0.9f, 0.7f));
                CozyFx.MakeInteractable(go, new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f), () => Loc.Display(Loc.Raw("[E] Order from") + " " + VisitorName(order)), OpenPanel);
                visitors[i] = go;
            }
            else if (!want && go != null) { Destroy(go); visitors.Remove(i); }
        }
    }
}
