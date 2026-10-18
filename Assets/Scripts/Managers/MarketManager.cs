using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supply-and-demand market. Data: Resources/Data/market.json (+ Resources/Data/Market/*.json packs).
/// - Selling points (central souq, village souq, grain silo, dairy co-op, fodder traders, cotton gin, sugar factory, export port...):
///   each buys some goods (the souq buys everything, so goods added later always sell). Price per good and point =
///   base price x daily trend x season curve x point multiplier x (1 - supply glut) x demand event x feature hooks.
/// - Selling a lot at a point raises its glut (the price falls while you sell); it recovers every day. Simulated AI farmers
///   add supply too (more for crops in their main season). Random demand events boost one good at one point for a few days.
/// - 30-day price history per good and point, trend arrows, best-price hint (net of transport).
/// - Truck sales (EconomyManager.SellAll) go to the chosen destination, or to the best point for each good ("Auto").
/// - Contracts board: NPC clients post offers every day (deliver goods for a fixed unit price + bonus, or tractor field
///   work for neighbours); accept up to maxActive; deliveries fill them when you sell, tractor work fills field jobs;
///   finishing pays the bonus and raises GameEvents.ContractCompleted, missing the deadline costs the penalty.
///   Other features may still add simple contracts straight into Contracts (bonus price, no penalty).
/// Saved in SaveData; reset on a new game.
/// </summary>
public class MarketManager : MonoBehaviour
{
    public static MarketManager Instance;
    public const string CentralId = "souq";
    public const int HistoryDays = 30;

    public MarketFile Cfg => Config;
    private static MarketFile config;
    /// <summary>market.json + packs (also read by DesertDressing for the map's selling-point buildings).</summary>
    public static MarketFile Config { get { if (config == null) config = LoadData(); return config; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { config = null; }
    /// <summary>Selling points that buy at least one good of this game.</summary>
    public readonly List<SellPointDef> Points = new List<SellPointDef>();
    public readonly List<Contract> Contracts = new List<Contract>();   // active (accepted) contracts
    public readonly List<Contract> Offers = new List<Contract>();      // contracts board
    public readonly List<MarketEvent> Events = new List<MarketEvent>();
    /// <summary>Where truck loads are sold: "" = best point for each good, else a point id.</summary>
    public string Destination { get; private set; } = "";
    /// <summary>Money of the last Sell that came from contract deliveries (EconomyManager books it as "contracts").</summary>
    public float LastContractEarnings { get; private set; }

    private readonly Dictionary<string, float> trend = new Dictionary<string, float>();   // daily multiplier per good
    private readonly Dictionary<string, float> glut = new Dictionary<string, float>();    // "point|good" -> supply pressure
    private readonly Dictionary<string, List<float>> history = new Dictionary<string, List<float>>();
    private int nextId = 1;
    private bool started;

    /// <summary>Feature hooks: extra price multipliers per good (seasonal events, friendships...). Return 1 for no change.</summary>
    public static readonly List<Func<string, float>> PriceMultipliers = new List<Func<string, float>>();

    private void Awake()
    {
        Instance = this;
    }

    private static string PointKey(SellPointDef p) => p.id + "|" + string.Join(",", p.regions ?? new string[0]) + "|" + string.Join(",", p.exceptRegions ?? new string[0]);

    private static MarketFile LoadData()
    {
        MarketFile Cfg = null;
        var ta = Resources.Load<TextAsset>("Data/market");
        try { if (ta != null) Cfg = JsonUtility.FromJson<MarketFile>(ta.text); }
        catch (Exception e) { Debug.LogError("[Market] Invalid market.json: " + e.Message); }
        if (Cfg == null) Cfg = new MarketFile();
        var pts = new List<SellPointDef>(Cfg.points ?? new SellPointDef[0]);
        var curves = new List<SeasonCurve>(Cfg.seasonCurves ?? new SeasonCurve[0]);
        var clients = new List<ClientDef>(Cfg.contracts?.clients ?? new ClientDef[0]);
        foreach (var pack in DataPacks.LoadAs<MarketFile>("Market"))
        {
            if (pack.points != null) foreach (var p in pack.points) { int i = pts.FindIndex(x => PointKey(x) == PointKey(p)); if (i >= 0) pts[i] = p; else pts.Add(p); }
            if (pack.seasonCurves != null) curves.AddRange(pack.seasonCurves);
            if (pack.contracts?.clients != null) clients.AddRange(pack.contracts.clients);
        }
        if (!pts.Exists(p => p.id == CentralId)) pts.Insert(0, new SellPointDef { id = CentralId, nameEn = "Central souq", nameAr = "السوق المركزي", accepts = new[] { "*" } });
        Cfg.points = pts.ToArray();
        Cfg.seasonCurves = curves.ToArray();
        if (Cfg.events == null) Cfg.events = new DemandEventCfg();
        if (Cfg.ai == null) Cfg.ai = new AiSupplyCfg();
        if (Cfg.contracts == null) Cfg.contracts = new ContractCfg();
        Cfg.contracts.clients = clients.ToArray();
        if (Cfg.contracts.clients.Length == 0) Cfg.contracts.clients = new[] { new ClientDef { en = "Riyadh Central Market", ar = "سوق الرياض المركزي" } };
        if (Cfg.contracts.fieldClients == null || Cfg.contracts.fieldClients.Length == 0) Cfg.contracts.fieldClients = new[] { new ClientDef { en = "Neighbour Abu Saleh", ar = "الجار أبو صالح" } };
        return Cfg;
    }

    /// <summary>Is a selling point on this map (regions / exceptRegions)?</summary>
    public static bool OnMap(SellPointDef p, string regionId)
    {
        if (p == null) return false;
        if (p.regions != null && p.regions.Length > 0 && Array.IndexOf(p.regions, regionId) < 0) return false;
        return p.exceptRegions == null || Array.IndexOf(p.exceptRegions, regionId) < 0;
    }

    /// <summary>Selling points built as buildings next to the market pad on a map (cotton gin, rice mill, village souq in the Delta).</summary>
    public static List<SellPointDef> Buildings(string regionId)
    {
        var list = new List<SellPointDef>();
        foreach (var p in Config.points) if (p != null && p.building && OnMap(p, regionId)) list.Add(p);
        return list;
    }

    private void OnEnable() => GameEvents.RegionChanged += OnRegion;
    private void OnDisable() => GameEvents.RegionChanged -= OnRegion;
    // A career on another map: its own selling points and clients.
    private void OnRegion(string regionId) { if (started) ResetMarket(); }

    private void Start()
    {
        BuildPoints();
        var eco = EconomyManager.Instance;
        if (eco != null)
            foreach (var g in eco.Data.goods) trend[g.id] = SaveData.GetFloat("mazraa.price." + g.id, 1f);
        LoadState();
        LoadContracts();
        WorldClock.DayStarted += OnNewDay;
        GameEvents.TractorWorked += OnTractorWorked;
        started = true;
        if (Contracts.Count == 0 && Offers.Count == 0) for (int i = 0; i < 3; i++) GenerateOffer(false);
        if (history.Count == 0) RecordHistory();
        SaveContracts();
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnNewDay;
        GameEvents.TractorWorked -= OnTractorWorked;
        if (Instance == this) Instance = null;
    }

    private void BuildPoints()
    {
        Points.Clear();
        var eco = EconomyManager.Instance;
        string region = Regions.CurrentId;
        foreach (var p in Cfg.points)
        {
            if (p == null || string.IsNullOrEmpty(p.id) || !OnMap(p, region) || Points.Exists(x => x.id == p.id)) continue;
            bool any = eco == null;
            if (eco != null) foreach (var g in eco.Data.goods) if (MarketMath.Accepts(p, g.id) && eco.GoodAvailable(g.id)) { any = true; break; }
            if (any) Points.Add(p);
        }
    }

    // ---------------- prices ----------------

    public SellPointDef Point(string id) => Points.Find(p => p.id == id);
    public static string PointName(SellPointDef p) => p == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(p.nameAr) ? p.nameAr : p.nameEn;
    public bool Accepts(string pointId, string goodId) => MarketMath.Accepts(Point(pointId), goodId) && EconomyManager.Instance?.Good(goodId) != null;
    private static string Key(string point, string good) => point + "|" + good;
    public float Glut(string point, string good) => glut.TryGetValue(Key(point, good), out var v) ? v : 0f;
    public IList<float> History(string point, string good) => history.TryGetValue(Key(point, good), out var h) ? h : (IList<float>)new float[0];

    /// <summary>Today's price per unit at the central souq (SAR).</summary>
    public float Price(string goodId) => Price(goodId, CentralId);

    /// <summary>Today's price per unit at a selling point (0 if it doesn't buy the good).</summary>
    public float Price(string goodId, string pointId)
    {
        var p = Point(pointId);
        return PriceNoGlut(goodId, p) * (1f - Glut(pointId, goodId));
    }

    private float PriceNoGlut(string goodId, SellPointDef p)
    {
        var g = EconomyManager.Instance?.Good(goodId);
        if (g == null || !MarketMath.Accepts(p, goodId)) return 0f;
        float t = trend.TryGetValue(goodId, out var v) ? v : 1f;
        float extra = FarmModifiers.Price(goodId);
        foreach (var m in PriceMultipliers) extra *= m(goodId);
        return g.price * t * SeasonFactor(goodId) * p.mul * MarketMath.GoodMul(p, goodId) * EventMul(p.id, goodId) * extra;
    }

    /// <summary>Price after transport to the point (what the farm actually gets per unit).</summary>
    public float NetPrice(string goodId, string pointId)
    {
        var p = Point(pointId); var g = EconomyManager.Instance?.Good(goodId);
        if (p == null || g == null) return 0f;
        return Mathf.Max(0f, Price(goodId, pointId) - p.haul * g.weight);
    }

    /// <summary>Selling point with the best net price for a good today.</summary>
    public string BestPoint(string goodId)
    {
        string best = CentralId; float bestP = -1f;
        foreach (var p in Points)
        {
            if (!MarketMath.Accepts(p, goodId)) continue;
            float n = NetPrice(goodId, p.id);
            if (n > bestP + 0.0001f) { bestP = n; best = p.id; }
        }
        return best;
    }

    public float EventMul(string point, string good)
    {
        float m = 1f;
        foreach (var e in Events) if (e.point == point && e.good == good) m *= e.mul;
        return m;
    }

    /// <summary>-1 / 0 / +1 price trend at a point.</summary>
    public int Trend(string goodId, string pointId) => MarketMath.Trend(History(pointId, goodId), Price(goodId, pointId));

    /// <summary>Season curve from market.json, else field crops sell cheaper in their growing season and dearer out of it.</summary>
    public float SeasonFactor(string goodId)
    {
        if (WorldClock.Instance == null) return 1f;
        int s = (int)WorldClock.Instance.Season;
        foreach (var c in Cfg.seasonCurves)
            if (c != null && c.good == goodId && c.mul != null && c.mul.Length == 4) return c.mul[s];
        var eco = EconomyManager.Instance;
        var crop = eco != null ? eco.FieldCrop(goodId) : null;
        if (crop == null || crop.seasons == null) return 1f;
        string now = WorldClock.SeasonId(WorldClock.Instance.Season);
        foreach (var x in crop.seasons) if (x == now) return 0.9f;
        return 1.3f;
    }

    private bool InSeason(string goodId)
    {
        var crop = EconomyManager.Instance?.FieldCrop(goodId);
        if (crop?.seasons == null || WorldClock.Instance == null) return false;
        string now = WorldClock.SeasonId(WorldClock.Instance.Season);
        return Array.IndexOf(crop.seasons, now) >= 0;
    }

    public void CycleDestination(int dir)
    {
        var ids = new List<string> { "" };
        foreach (var p in Points) ids.Add(p.id);
        int i = Mathf.Max(0, ids.IndexOf(Destination));
        Destination = ids[((i + dir) % ids.Count + ids.Count) % ids.Count];
        SaveState();
    }

    public void SetDestination(string pointId) { Destination = pointId == "" || Point(pointId) != null ? pointId : ""; SaveState(); }

    private string PointFor(string goodId) =>
        DirectPoint != "" && MarketMath.Accepts(Point(DirectPoint), goodId) ? DirectPoint :
        Destination != "" && MarketMath.Accepts(Point(Destination), goodId) ? Destination : BestPoint(goodId);

    // ---------------- open world: the truck sells at the selling point it drove to (World/OpenWorld) ----------------

    /// <summary>Selling point of the sale in progress at a world place ("" = none): its own prices, no transport cost
    /// (the farmer hauled it) and a small direct-delivery bonus.</summary>
    public string DirectPoint { get; private set; } = "";
    public const float DirectSaleBonus = 1.1f;

    /// <summary>Sell goods at one point (contracts first, like every sale). Goods it doesn't buy should be filtered out
    /// before (Accepts). Money, ledger and delivery events go through EconomyManager.SellAll. Returns money earned.</summary>
    public int SellDirect(string pointId, Dictionary<string, float> cargo, List<string> messages)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || Point(pointId) == null) return 0;
        DirectPoint = pointId;
        try { return eco.SellAll(cargo, messages); }
        finally { DirectPoint = ""; }
    }
    // ---------------- end open world ----------------

    // ---------------- selling ----------------

    /// <summary>Sell goods: accepted contracts first (their price), the rest at the destination (or best point). Returns SAR earned.</summary>
    public int Sell(Dictionary<string, float> cargo, List<string> messages)
    {
        float earned = 0f, fromContracts = 0f;
        var perPoint = new Dictionary<string, float>();
        foreach (var kv in new List<KeyValuePair<string, float>>(cargo))
        {
            float left = kv.Value;
            if (left <= 0f || EconomyManager.Instance?.Good(kv.Key) == null) continue;
            GameEvents.RaiseCustom("sell_" + kv.Key, kv.Value);   // e.g. sell_cotton for the Egypt story
            foreach (var c in Contracts)
            {
                if (c.Field() || c.good != kv.Key || left <= 0f) continue;
                float take = Mathf.Min(left, c.Left());
                if (take <= 0f) continue;
                c.delivered += take;
                left -= take;
                fromContracts += take * (c.unitPrice > 0f ? c.unitPrice : Price(kv.Key) * c.bonus);
            }
            if (left <= 0.0001f) continue;
            string point = PointFor(kv.Key);
            float v = SellAt(point, kv.Key, left);
            earned += v;
            perPoint[point] = (perPoint.TryGetValue(point, out var pv) ? pv : 0f) + v;
        }
        if (perPoint.Count > 1 || (perPoint.Count == 1 && !perPoint.ContainsKey(CentralId)))
            foreach (var kv in perPoint)
                messages?.Add(Loc.Display(PointName(Point(kv.Key)) + ": " + Mathf.RoundToInt(kv.Value).ToString("N0") + " " + Loc.Raw("SAR")));
        for (int i = Contracts.Count - 1; i >= 0; i--)
        {
            var c = Contracts[i];
            if (!c.Field() && c.delivered >= c.amount - 0.01f) Complete(c, messages);
        }
        LastContractEarnings = fromContracts;
        SaveState();
        SaveContracts();
        return Mathf.RoundToInt(earned + fromContracts);
    }

    /// <summary>Sell an amount at one point (no contracts): money after transport; pushes that point's price down.</summary>
    public float SellAt(string pointId, string goodId, float amount)
    {
        var p = Point(pointId); var g = EconomyManager.Instance?.Good(goodId);
        if (!MarketMath.Accepts(p, goodId) || g == null || amount <= 0f) { p = Point(CentralId); pointId = CentralId; }
        if (p == null || g == null || amount <= 0f) return 0f;
        float g0 = Glut(pointId, goodId), g1 = MarketMath.GlutAfter(p, g0, amount);
        bool direct = pointId == DirectPoint;   // open world: sold on the spot
        float value = MarketMath.SaleValue(PriceNoGlut(goodId, p), g0, g1, amount) * (direct ? DirectSaleBonus : 1f) - (direct ? 0f : amount * p.haul * g.weight);
        glut[Key(pointId, goodId)] = g1;
        return Mathf.Max(0f, value);
    }

    // ---------------- daily ----------------

    private void OnNewDay(int day)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || NetSession.IsClient) return;   // co-op: the host runs the market
        foreach (var g in eco.Data.goods)
        {
            float t = trend.TryGetValue(g.id, out var v) ? v : 1f;
            trend[g.id] = Mathf.Clamp(t * UnityEngine.Random.Range(0.88f, 1.13f) + (1f - t) * 0.15f, 0.65f, 1.5f);
        }
        // supply recovers; simulated farmers bring their own harvests to market
        var ai = Cfg.ai;
        foreach (var p in Points)
        {
            var accepted = new List<string>();
            foreach (var g in eco.Data.goods)
            {
                if (!MarketMath.Accepts(p, g.id)) continue;
                accepted.Add(g.id);
                string k = Key(p.id, g.id);
                float q = glut.TryGetValue(k, out var gv) ? gv : 0f;
                q = Mathf.Max(0f, q - Mathf.Max(0.01f, p.recovery));
                if (InSeason(g.id) && q < ai.cap) q = Mathf.Min(ai.cap, q + UnityEngine.Random.Range(0f, ai.inSeasonGlut));
                if (q > 0f) glut[k] = q; else glut.Remove(k);
            }
            if (accepted.Count > 0 && UnityEngine.Random.value < ai.dailyChance)
            {
                string k = Key(p.id, accepted[UnityEngine.Random.Range(0, accepted.Count)]);
                float q = glut.TryGetValue(k, out var gv) ? gv : 0f;
                if (q < ai.cap) glut[k] = Mathf.Min(ai.cap, q + UnityEngine.Random.Range(ai.minGlut, ai.maxGlut));
            }
        }
        // demand events
        for (int i = Events.Count - 1; i >= 0; i--) if (day > Events[i].endDay) Events.RemoveAt(i);
        if (UnityEngine.Random.value < Cfg.events.chancePerDay) RandomDemandEvent(day);
        RecordHistory();
        // contracts
        for (int i = Offers.Count - 1; i >= 0; i--) if (day > Offers[i].offerUntil) Offers.RemoveAt(i);
        for (int i = Contracts.Count - 1; i >= 0; i--)
            if (day > Contracts[i].dueDay) Fail(Contracts[i]);
        if (Offers.Count < Cfg.contracts.maxOffers && UnityEngine.Random.value < Cfg.contracts.offerChance) GenerateOffer(true);
        if (Offers.Count < 2) GenerateOffer(false);
        SavePrices();
        SaveState();
        SaveContracts();
    }

    private void RecordHistory()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var p in Points)
            foreach (var g in eco.Data.goods)
            {
                if (!MarketMath.Accepts(p, g.id)) continue;
                string k = Key(p.id, g.id);
                if (!history.TryGetValue(k, out var h)) history[k] = h = new List<float>();
                h.Add(Mathf.Round(Price(g.id, p.id) * 100f) / 100f);
                if (h.Count > HistoryDays) h.RemoveRange(0, h.Count - HistoryDays);
            }
    }

    private void RandomDemandEvent(int day)
    {
        var eco = EconomyManager.Instance;
        var goods = ContractGoods();
        for (int tries = 0; tries < 8 && goods.Count > 0; tries++)
        {
            string good = goods[UnityEngine.Random.Range(0, goods.Count)];
            var pts = Points.FindAll(x => MarketMath.Accepts(x, good));
            if (pts.Count == 0) continue;
            var p = pts[UnityEngine.Random.Range(0, pts.Count)];
            if (Events.Exists(e => e.point == p.id && e.good == good)) continue;
            float mul = Mathf.Round(UnityEngine.Random.Range(Cfg.events.minBoost, Cfg.events.maxBoost) * 20f) / 20f;
            int days = UnityEngine.Random.Range(Cfg.events.minDays, Cfg.events.maxDays + 1);
            AddDemandEvent(p.id, good, mul, days);
            return;
        }
    }

    /// <summary>Start a demand event: the good sells for 'mul' x at the point for 'days' days (today counts). Returns it.</summary>
    public MarketEvent AddDemandEvent(string pointId, string goodId, float mul, int days)
    {
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        var e = new MarketEvent { point = pointId, good = goodId, mul = mul, endDay = today + Mathf.Max(1, days) - 1 };
        Events.Add(e);
        SaveState();
        var g = EconomyManager.Instance?.Good(goodId);
        Alert(Loc.T("High demand for {0} at {1}: +{2}% for {3} days", EconomyManager.GoodName(g), PointName(Point(pointId)), Mathf.RoundToInt((mul - 1f) * 100f), Mathf.Max(1, days)));
        return e;
    }

    // ---------------- contracts ----------------

    public int ActiveBoardContracts { get { int n = 0; foreach (var c in Contracts) if (c.board) n++; return n; } }
    public int MaxActive => Mathf.Max(1, Cfg.contracts.maxActive);

    /// <summary>Goods clients ask for: field crops, animal products, processed outputs of owned buildings and anything in storage.</summary>
    public List<string> ContractGoods()
    {
        var list = new List<string>();
        var eco = EconomyManager.Instance;
        if (eco == null) return list;
        Action<string> Add = id => { if (!string.IsNullOrEmpty(id) && eco.Good(id) != null && eco.GoodAvailable(id) && !list.Contains(id)) list.Add(id); };
        foreach (var c in eco.Data.fieldCrops) if (EconomyManager.Available(c)) Add(c.id);   // nothing this map can't grow
        foreach (var a in eco.Data.animals) Add(a.product);
        Add("crops");
        foreach (var g in eco.Data.goods) if (eco.Stored(g.id) >= 1f) Add(g.id);
        var yard = ProcessingYard.Instance;
        if (yard != null) foreach (var p in yard.Plants) if (p.owned && p.def.recipes != null) foreach (var r in p.def.recipes) Add(r.output);
        return list;
    }

    private ClientDef PickClient(ClientDef[] all)
    {
        string region = Regions.Current != null ? Regions.Current.id : "";
        var ok = new List<ClientDef>();
        foreach (var c in all) if (c != null && c.region == region) ok.Add(c);              // a map with its own clients (Nile Delta) uses only those
        if (ok.Count == 0) foreach (var c in all) if (c != null && string.IsNullOrEmpty(c.region)) ok.Add(c);
        if (ok.Count == 0) ok.AddRange(all);
        return ok[UnityEngine.Random.Range(0, ok.Count)];
    }

    /// <summary>Post a new offer on the contracts board.</summary>
    public Contract GenerateOffer(bool announce)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Data.goods.Length == 0) return null;
        var cfg = Cfg.contracts;
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        Contract c;
        if (FieldManager.Instance != null && FieldManager.Instance.Plots.Count > 0 && UnityEngine.Random.value < cfg.fieldChance)
        {
            var cl = PickClient(cfg.fieldClients);
            string[] tasks = { "Plow", "Seeder", "Harvester" };
            int passes = UnityEngine.Random.Range(cfg.fieldMin, cfg.fieldMax + 1);
            int reward = Mathf.RoundToInt(passes * UnityEngine.Random.Range(cfg.fieldPayMin, cfg.fieldPayMax) / 10f) * 10;
            c = new Contract { buyerEn = cl.en, buyerAr = cl.ar, kind = "field", task = tasks[UnityEngine.Random.Range(0, tasks.Length)], good = "", amount = passes, reward = reward,
                penalty = Mathf.RoundToInt(reward * cfg.penaltyShare / 10f) * 10 };
        }
        else
        {
            var goods = ContractGoods();
            if (goods.Count == 0) foreach (var gd in eco.Data.goods) goods.Add(gd.id);
            var g = eco.Good(goods[UnityEngine.Random.Range(0, goods.Count)]);
            var cl = PickClient(cfg.clients);
            float unit = Mathf.Max(0.1f, Mathf.Round(g.price * SeasonFactor(g.id) * UnityEngine.Random.Range(cfg.minPremium, cfg.maxPremium) * 10f) / 10f);
            float amount = UnityEngine.Random.Range(cfg.minValue, cfg.maxValue) / unit;
            amount = amount >= 50f ? Mathf.Round(amount / 10f) * 10f : Mathf.Max(5f, Mathf.Round(amount));
            float value = amount * unit;
            c = new Contract { buyerEn = cl.en, buyerAr = cl.ar, kind = "deliver", good = g.id, amount = amount, unitPrice = unit,
                reward = Mathf.RoundToInt(value * cfg.rewardShare / 10f) * 10, penalty = Mathf.RoundToInt(value * cfg.penaltyShare / 10f) * 10 };
        }
        c.id = "c" + (nextId++);
        c.board = true;
        c.bonus = 1f;
        c.days = UnityEngine.Random.Range(cfg.minDays, cfg.maxDays + 1);
        c.offerUntil = today + Mathf.Max(1, cfg.offerDays);
        c.dueDay = today + c.days;
        Offers.Add(c);
        if (announce) Alert(Loc.Display(Loc.Raw("New contract") + ": " + BuyerName(c)));
        if (started) { SaveState(); SaveContracts(); }
        return c;
    }

    /// <summary>English Loc key why an offer can't be accepted, or null.</summary>
    public string AcceptBlock(Contract c)
    {
        if (c == null || !Offers.Contains(c)) return "Not available";
        if (ActiveBoardContracts >= MaxActive) return "Contract limit reached";
        return null;
    }

    public bool Accept(Contract c)
    {
        string block = AcceptBlock(c);
        if (block != null) { Alert(Loc.T(block)); return false; }
        if (NetSession.IsClient) { NetSession.RequestContract(c.id); return true; }   // co-op: the host runs the contracts board
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        Offers.Remove(c);
        c.dueDay = today + Mathf.Max(1, c.days);
        Contracts.Add(c);
        SaveContracts(); SaveState();
        GameEvents.RaiseCustom("acceptContract", 1f);
        Alert(Loc.Display(Loc.Raw("Contract accepted") + ": " + BuyerName(c)));
        return true;
    }

    private void OnTractorWorked(Implement i)
    {
        if (NetSession.IsClient) return;
        string task = i.ToString();
        for (int k = Contracts.Count - 1; k >= 0; k--)
        {
            var c = Contracts[k];
            if (!c.Field() || c.task != task) continue;
            c.delivered = Mathf.Min(c.amount, c.delivered + 1f);
            if (c.delivered >= c.amount - 0.01f) Complete(c, null);
        }
        SaveContracts();
    }

    private void Complete(Contract c, List<string> messages)
    {
        Contracts.Remove(c);
        string msg = Loc.Raw("Contract complete") + ": " + BuyerName(c);
        if (c.reward > 0 && EconomyManager.Instance != null)
        {
            using (Ledger.As(Ledger.Contracts)) EconomyManager.Instance.AddMoney(c.reward);
            msg += " (+" + c.reward.ToString("N0") + " " + Loc.Raw("SAR") + ")";
        }
        if (messages != null) messages.Add(Loc.Display(msg)); else Alert(Loc.Display(msg));
        SaveContracts();
        GameEvents.RaiseContractCompleted();
    }

    private void Fail(Contract c)
    {
        Contracts.Remove(c);
        if (c.board && c.penalty > 0)
        {
            var eco = EconomyManager.Instance;
            if (eco != null) using (Ledger.As(Ledger.Contracts)) eco.AddMoney(-c.penalty);
            Alert(Loc.Display(Loc.Raw("Contract failed") + ": " + BuyerName(c) + " - " + Loc.Raw("penalty") + " " + c.penalty.ToString("N0") + " " + Loc.Raw("SAR")));
            GameEvents.RaiseCustom("contractFailed", 1f);
        }
        else Alert(Loc.Display(Loc.Raw("Contract expired") + ": " + BuyerName(c)));
        SaveContracts();
    }

    public static string BuyerName(Contract c) => Loc.IsArabic && !string.IsNullOrEmpty(c.buyerAr) ? c.buyerAr : c.buyerEn;

    /// <summary>"120 kg Onions" / "6 x Plow".</summary>
    public static string ContractWhat(Contract c)
    {
        if (c.Field()) return Loc.Raw("Field work") + ": " + c.amount.ToString("F0") + " x " + Loc.Raw(c.task);
        var g = EconomyManager.Instance?.Good(c.good);
        return c.amount.ToString("F0") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g);
    }

    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }

    // ---------------- save ----------------

    [Serializable] private class ContractList { public List<Contract> items = new List<Contract>(); public List<Contract> offers = new List<Contract>(); }
    [Serializable] private class KeyVal { public string k; public float v; }
    [Serializable] private class KeyHist { public string k; public List<float> h = new List<float>(); }
    [Serializable] private class State { public List<KeyVal> glut = new List<KeyVal>(); public List<MarketEvent> events = new List<MarketEvent>(); public List<KeyHist> history = new List<KeyHist>(); public string dest = ""; public int nextId = 1; }

    private void SaveContracts()
    {
        SaveData.SetString("mazraa.contracts", JsonUtility.ToJson(new ContractList { items = Contracts, offers = Offers }));
        SaveData.Save();
    }

    private void LoadContracts()
    {
        try
        {
            var l = JsonUtility.FromJson<ContractList>(SaveData.GetString("mazraa.contracts", "{}"));
            if (l?.items != null) foreach (var c in l.items) if (c != null) Contracts.Add(c);
            if (l?.offers != null) foreach (var c in l.offers) if (c != null) Offers.Add(c);
        }
        catch { }
    }

    private void SaveState()
    {
        var s = new State { dest = Destination, nextId = nextId };
        foreach (var kv in glut) s.glut.Add(new KeyVal { k = kv.Key, v = kv.Value });
        s.events.AddRange(Events);
        foreach (var kv in history) s.history.Add(new KeyHist { k = kv.Key, h = kv.Value });
        SaveData.SetString("mazraa.market.state", JsonUtility.ToJson(s));
        SaveData.Save();
    }

    private void LoadState()
    {
        State s = null;
        try { s = JsonUtility.FromJson<State>(SaveData.GetString("mazraa.market.state", "")); } catch { }
        glut.Clear(); history.Clear(); Events.Clear();
        if (s == null)
        {   // older saves: one supply value per good at the market
            var eco = EconomyManager.Instance;
            if (eco != null) foreach (var g in eco.Data.goods) { float q = SaveData.GetFloat("mazraa.glut." + g.id, 0f); if (q > 0f) glut[Key(CentralId, g.id)] = q; }
            return;
        }
        if (s.glut != null) foreach (var kv in s.glut) if (kv != null && kv.k != null) glut[kv.k] = kv.v;
        if (s.events != null) foreach (var e in s.events) if (e != null) Events.Add(e);
        if (s.history != null) foreach (var h in s.history) if (h != null && h.k != null && h.h != null) history[h.k] = h.h;
        Destination = Point(s.dest ?? "") != null ? s.dest : "";
        nextId = Mathf.Max(1, s.nextId);
    }

    private void SavePrices()
    {
        foreach (var kv in trend) SaveData.SetFloat("mazraa.price." + kv.Key, kv.Value);
        SaveData.Save();
    }

    // ---------------- co-op sync (host -> clients) ----------------

    [Serializable] private class SyncState { public List<KeyVal> trend = new List<KeyVal>(); public List<KeyVal> glut = new List<KeyVal>(); public List<MarketEvent> events = new List<MarketEvent>(); public List<Contract> contracts = new List<Contract>(); public List<Contract> offers = new List<Contract>(); }

    /// <summary>Host: today's prices (trend), supply, demand events and the contracts board for the clients.</summary>
    public string EncodeSync()
    {
        var s = new SyncState();
        foreach (var kv in trend) s.trend.Add(new KeyVal { k = kv.Key, v = kv.Value });
        foreach (var kv in glut) s.glut.Add(new KeyVal { k = kv.Key, v = kv.Value });
        s.events.AddRange(Events);
        s.contracts.AddRange(Contracts);
        s.offers.AddRange(Offers);
        return NetProtocol.Make(NetProtocol.MarketSync, JsonUtility.ToJson(s));
    }

    /// <summary>Client: show the host's market (nothing is saved; ReloadLocal brings our own market back).</summary>
    public bool ApplySync(string[] parts)
    {
        SyncState s = null;
        try { s = JsonUtility.FromJson<SyncState>(NetProtocol.Field(parts, 1)); } catch { }
        if (s == null) return false;
        trend.Clear(); glut.Clear(); Events.Clear(); Contracts.Clear(); Offers.Clear();
        if (s.trend != null) foreach (var kv in s.trend) if (kv != null && kv.k != null) trend[kv.k] = Mathf.Clamp(kv.v, 0.1f, 5f);
        if (s.glut != null) foreach (var kv in s.glut) if (kv != null && kv.k != null) glut[kv.k] = Mathf.Clamp01(kv.v);
        if (s.events != null) foreach (var e in s.events) if (e != null) Events.Add(e);
        if (s.contracts != null) foreach (var c in s.contracts) if (c != null) Contracts.Add(c);
        if (s.offers != null) foreach (var c in s.offers) if (c != null) Offers.Add(c);
        return true;
    }

    /// <summary>Host: a client accepted a contract offer.</summary>
    public bool HostAccept(string contractId)
    {
        var c = Offers.Find(x => x != null && x.id == contractId);
        return c != null && Accept(c);
    }

    /// <summary>Back on our own farm after visiting: reload our own prices, market state and contracts.</summary>
    public void ReloadLocal()
    {
        var eco = EconomyManager.Instance;
        trend.Clear();
        if (eco != null) foreach (var g in eco.Data.goods) trend[g.id] = SaveData.GetFloat("mazraa.price." + g.id, 1f);
        LoadState();
        Contracts.Clear(); Offers.Clear();
        LoadContracts();
    }

    public void ResetMarket()
    {
        BuildPoints();
        Contracts.Clear(); Offers.Clear(); Events.Clear(); glut.Clear(); history.Clear();
        foreach (var k in new List<string>(trend.Keys)) trend[k] = 1f;
        Destination = ""; nextId = 1;
        for (int i = 0; i < 3; i++) GenerateOffer(false);
        RecordHistory();
        SavePrices(); SaveState(); SaveContracts();
    }
}
