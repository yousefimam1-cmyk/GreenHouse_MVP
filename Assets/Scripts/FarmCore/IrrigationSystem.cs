using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The farm's irrigation network in the scene: runs IrrigationNetwork with FarmFields' simulation steps, bills water and
/// pump energy (Ledger "utilities"), keeps the groundwater level per map, opens canal intakes on the canal turn
/// (Delta: every few days, nobat el-mayya), stops on water bans, builds simple models of the parts (well heads, tanks,
/// canal gates, pivots with a turning arm, linear carts, sprinklers, drip lines, pipes) and saves under mazraa.irr.*.
/// The irrigation planner (IrrigationPlanner) places parts through Place / Connect / Remove; co-op clients forward edits.
/// Grid-powered pumps add their load to the Utilities grid (UtilityHooks.IrrigationKw) and run at its power ratio.
/// </summary>
[MazraaSystem(26)]
public class IrrigationSystem : MonoBehaviour
{
    public static IrrigationSystem Instance { get; private set; }
    public const string P = "mazraa.irr.";

    public IrrigationNetwork Net { get; } = new IrrigationNetwork();
    public IrrReport Last { get; private set; } = new IrrReport();
    public float WaterM3Today { get; private set; }
    public float WaterM3Total { get; private set; }
    public float CostToday { get; private set; }
    public float EnergyKwhToday { get; private set; }
    public bool ShowCoverage;

    private float unpaid;
    private Transform visualsRoot;
    private int builtVersion = -1;
    private readonly Dictionary<int, Transform> arms = new Dictionary<int, Transform>();
    private float saveTimer = 10f;

    private void Awake()
    {
        Instance = this;
        Load();
    }

    private void OnEnable()
    {
        GameEvents.NewGameStarted += OnNewGame;
        GameEvents.RegionChanged += OnRegion;
        if (!HudLines.Status.Contains(hudLine)) HudLines.Status.Add(hudLine);
        MissionManager.StateMissions["installEfficientIrrigation"] = () => Instance != null && Instance.HasEfficient() ? 1f : 0f;
    }

    private void OnDisable()
    {
        GameEvents.NewGameStarted -= OnNewGame;
        GameEvents.RegionChanged -= OnRegion;
        Save();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>A drip line or a centre pivot / linear move connected to a water source.</summary>
    public bool HasEfficient()
    {
        foreach (var comp in Net.Components())
        {
            bool src = false, eff = false;
            foreach (var n in comp) { if (n.IsSource) src = true; if (n.kind == IrrKind.Drip || n.kind == IrrKind.Pivot || n.kind == IrrKind.Linear) eff = true; }
            if (src && eff) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ simulation

    /// <summary>Is the canal flowing today? Delta: on the canal turn (every few days, and while the turn event runs), never during a water ban.</summary>
    public static bool CanalOpen()
    {
        var r = FcData.CurrentRegion;
        if (!r.canal || FarmModifiers.IrrigationBanned) return false;
        var ed = EventDirector.Instance;
        if (ed != null && ed.Running && ed.Active != null && ed.Active.type == "canal") return true;
        var reg = Regions.Current;
        int every = reg != null && reg.canalEvery > 0 ? reg.canalEvery : 4;
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        return day % every == 0;
    }

    public static float Sun()
    {
        var c = WorldClock.Instance;
        if (c == null) return 1f;
        return Mathf.Clamp01(c.SunHeight) * UtilityModel.SolarWeather(c.Weather);
    }

    public void Step(float hours, FarmEnv env)
    {
        var ff = FarmFields.Instance;
        if (ff == null || Net.Nodes.Count == 0) { UtilityHooks.IrrigationKw = 0f; return; }
        if (Net.GroundwaterM <= 0f) Net.GroundwaterM = env.region.gwStart;
        bool hasGrid = UtilityGrid.Instance != null;
        float tse = hasGrid && UtilityGrid.Instance.Model.Tse ? UtilityModel.TseM3 / 24f : 0f;
        var rep = Net.Step(hours, env, ff.Fields, CanalOpen(), FarmModifiers.IrrigationBanned, Sun(), hasGrid ? UtilityHooks.PowerRatio : 1f, tse);
        Last = rep;
        UtilityHooks.IrrigationKw = rep.gridKw;
        WaterM3Today += rep.m3; WaterM3Total += rep.m3;
        EnergyKwhToday += rep.kwhGrid + rep.kwhSolar;
        float cost = rep.cost + (hasGrid ? 0f : rep.kwhGrid * UtilityModel.GridTariff);   // with Utilities the grid energy is on the SEC bill
        foreach (var kv in rep.fieldM3)
        {
            var f = ff.Find(kv.Key);
            if (f != null && rep.m3 > 0f) f.cycleCost += cost * kv.Value / rep.m3;
        }
        Charge(cost);
        if (rep.m3 > 0f) GameEvents.RaiseCustom("irrigateM3", rep.m3);
    }

    /// <summary>Water used outside the network (leaching by hand): metered and billed.</summary>
    public void BookExtraWater(float m3)
    {
        var r = FcData.CurrentRegion;
        WaterM3Today += m3; WaterM3Total += m3;
        Charge(m3 * (r.canal ? r.canalFee : r.waterCost));
    }

    private void Charge(float sar)
    {
        if (sar <= 0f) return;
        CostToday += sar;
        unpaid += sar;
        if (unpaid < 1f || EconomyManager.Instance == null || NetSession.IsClient) return;
        int pay = Mathf.FloorToInt(unpaid);
        unpaid -= pay;
        using (Ledger.As(Ledger.Utilities)) EconomyManager.Instance.AddMoney(-pay);
    }

    public void OnDay(int day)
    {
        var c = WorldClock.Instance;
        Net.Recharge(FcData.CurrentRegion, c != null && c.Weather == Weather.Rain);
        if (WaterM3Today > 0.5f && UIManager.Instance != null)
            FcText.Say(FcText.R("Irrigation yesterday") + ": " + WaterM3Today.ToString("F0") + " m3 · " + CostToday.ToString("F0") + " " + FcText.R("SAR") + " · " + FcText.R("Groundwater") + " " + Net.GroundwaterM.ToString("F1") + " m");
        WaterM3Today = 0f; CostToday = 0f; EnergyKwhToday = 0f;
        Save();
    }

    // ------------------------------------------------------------------ editing (planner)

    /// <summary>Buy and place a part. Returns the node, or null (and why) when it can't be placed.</summary>
    public IrrNode Place(IrrKind kind, Vector3 pos, float size, out string why)
    {
        why = null;
        var ff = FarmFields.Instance;
        var field = ff != null ? ff.FieldAt(pos, kind == IrrKind.Flood || kind == IrrKind.Drip ? 3f : 0f) : null;
        if ((kind == IrrKind.Drip || kind == IrrKind.Flood || kind == IrrKind.Linear || kind == IrrKind.Sensor) && field == null) { why = "Place it on a field"; return null; }
        if (kind == IrrKind.Canal && !FcData.CurrentRegion.canal) { why = "There is no canal on this map"; return null; }
        if (kind == IrrKind.Flood && field != null && Net.Nodes.Exists(n => n.kind == IrrKind.Flood && n.field == field.id)) { why = "This field already has one"; return null; }
        if (kind == IrrKind.Drip && field != null && Net.Nodes.Exists(n => n.kind == IrrKind.Drip && n.field == field.id)) { why = "This field already has one"; return null; }
        if (kind == IrrKind.Linear && field != null) size = field.width;
        int price = IrrigationNetwork.Price(kind, size > 0f ? size : FcData.Emitter(kind).size, field != null ? field.Area : 0f);
        if (NetSession.IsClient) { FarmNet.ForwardIrrigation("add", ((int)kind).ToString(), FarmNet.V(pos), size.ToString(System.Globalization.CultureInfo.InvariantCulture)); return null; }
        var eco = EconomyManager.Instance;
        if (eco != null && price > 0)
        {
            bool ok;
            using (Ledger.As(Ledger.Purchases)) ok = eco.Spend(price);
            if (!ok) { why = "Not enough money"; return null; }
        }
        var node = Net.Add(kind, pos, field != null ? field.id : -1, size);
        if (node.IsEmitter && field != null && Net.Nodes.Exists(n => n.kind == IrrKind.Sensor && n.field == field.id)) node.mode = IrrMode.Auto;
        if (kind == IrrKind.Sensor) foreach (var n in Net.Nodes) if (n.IsEmitter && n.field == node.field && n.mode == IrrMode.Schedule) n.mode = IrrMode.Auto;
        GameEvents.RaiseCustom("irrigationPart", 1f);
        if (HasEfficient()) GameEvents.RaiseCustom("installEfficientIrrigation", 1f);
        Changed();
        return node;
    }

    /// <summary>Lay a pipe between two parts (cost per metre by diameter).</summary>
    public IrrPipe Connect(int a, int b, int diameterMm, out string why)
    {
        why = null;
        var na = Net.Node(a); var nb = Net.Node(b);
        if (na == null || nb == null || a == b) { why = "Choose two different parts"; return null; }
        if (NetSession.IsClient) { FarmNet.ForwardIrrigation("pipe", a.ToString(), b.ToString(), diameterMm.ToString()); return null; }
        int price = IrrigationNetwork.PipePrice(IrrigationNetwork.Flat(na.pos - nb.pos), diameterMm);
        var eco = EconomyManager.Instance;
        if (eco != null && price > 0)
        {
            bool ok;
            using (Ledger.As(Ledger.Purchases)) ok = eco.Spend(price);
            if (!ok) { why = "Not enough money"; return null; }
        }
        var p = Net.Connect(a, b, diameterMm);
        Changed();
        return p;
    }

    /// <summary>Remove a part and its pipes (half the price back).</summary>
    public void Remove(int id)
    {
        var n = Net.Node(id);
        if (n == null) return;
        if (NetSession.IsClient) { FarmNet.ForwardIrrigation("del", id.ToString()); return; }
        var ff = FarmFields.Instance;
        var f = ff != null ? ff.Find(n.field) : null;
        int refund = IrrigationNetwork.Price(n.kind, n.size, f != null ? f.Area : 0f) / 2;
        if (EconomyManager.Instance != null && refund > 0) using (Ledger.As(Ledger.Purchases)) EconomyManager.Instance.AddMoney(refund);
        Net.Remove(id);
        Changed();
    }

    /// <summary>Change an emitter's settings (mode, schedule, revolution time, power).</summary>
    public void Configure(int id, IrrMode mode, int startHour, int hours, float revHours, IrrPower power)
    {
        var n = Net.Node(id);
        if (n == null) return;
        if (NetSession.IsClient) { FarmNet.ForwardIrrigation("set", id.ToString(), ((int)mode).ToString(), startHour.ToString(), hours.ToString(), revHours.ToString(System.Globalization.CultureInfo.InvariantCulture), ((int)power).ToString()); return; }
        n.mode = mode; n.startHour = ((startHour % 24) + 24) % 24; n.hours = Mathf.Clamp(hours, 0, 24); n.revHours = Mathf.Clamp(revHours, 2f, 96f); n.power = power;
        Changed();
    }

    /// <summary>One line per connected network: parts, what the sources can give now against what the emitters need, shortages.</summary>
    public List<string> NetworkLines()
    {
        var lines = new List<string>();
        var ff = FarmFields.Instance;
        var fields = ff != null ? ff.Fields : new List<FarmField>();
        var r = FcData.CurrentRegion;
        bool canal = CanalOpen();
        float sun = Sun(), grid = UtilityGrid.Instance != null ? UtilityHooks.PowerRatio : 1f;
        int k = 0;
        foreach (var comp in Net.Components())
        {
            k++;
            float supply = 0f, demand = 0f, got = 0f; int src = 0, emit = 0;
            string issue = "";
            foreach (var n in comp)
            {
                if (n.IsSource) { src++; supply += n.kind == IrrKind.Tank ? FcData.Source(IrrKind.Tank).capacity * (n.stored > 0.5f ? 1f : 0f) : Net.SourceFlow(n, r, canal, sun, grid, 20f, 1f); }
                if (n.IsEmitter) { emit++; demand += Net.DesignFlow(n, fields); got += n.delivered; if (n.running && n.shortage != "") issue = n.shortage; }
            }
            string line = FcText.R("Network") + " " + k + ": " + src + " " + FcText.R("sources") + ", " + emit + " " + FcText.R("emitters") + " · " + FcText.R("supply") + " " + supply.ToString("F1") + " / " + FcText.R("demand") + " " + demand.ToString("F1") + " m3/h";
            if (got > 0f) line += " · " + FcText.R("now") + " " + got.ToString("F1");
            if (issue != "") line += " · <color=#FF6A4D>" + FcText.R(issue) + "</color>";
            else if (src == 0 && emit > 0) line += " · <color=#FF6A4D>" + FcText.R("Not connected to a water source") + "</color>";
            lines.Add(line);
        }
        if (k == 0) lines.Add(FcText.R("No irrigation yet: place a well, then pipes and an emitter"));
        lines.Add(FcText.R("Groundwater") + " " + Net.GroundwaterM.ToString("F1") + " m (" + FcText.R("well limit") + " " + r.gwWellMax.ToString("F0") + " m)" + (r.canal ? " · " + FcText.R(canal ? "Canal open today" : "Canal closed today") : "")
                  + (FarmModifiers.IrrigationBanned ? " · " + FcText.R("Water restriction today") : ""));
        return lines;
    }

    public IrrNode Nearest(Vector3 p, float within)
    {
        IrrNode best = null; float bd = within;
        foreach (var n in Net.Nodes) { float d = IrrigationNetwork.Flat(n.pos - p); if (d <= bd) { bd = d; best = n; } }
        return best;
    }

    public void Changed()
    {
        Net.Changed();
        Save();
        if (NetSession.IsHost && MultiplayerManager.Instance != null) MultiplayerManager.Instance.MarkSyncDirty();
    }

    // ------------------------------------------------------------------ visuals

    private void Update()
    {
        if (Net.Version != builtVersion) BuildVisuals();
        foreach (var n in Net.Nodes)
        {
            Transform arm;
            if (!arms.TryGetValue(n.id, out arm) || arm == null) continue;
            if (n.kind == IrrKind.Pivot) arm.localRotation = Quaternion.Euler(0f, n.angle, 0f);
            else if (n.kind == IrrKind.Linear)
            {
                var f = FarmFields.Instance != null ? FarmFields.Instance.Find(n.field) : null;
                if (f != null) { float t = n.angle <= 1f ? n.angle : 2f - n.angle; arm.position = f.center + f.Rotation * new Vector3((t - 0.5f) * f.width, 0f, 0f); arm.rotation = f.Rotation; }
            }
        }
        saveTimer -= Time.deltaTime;
        if (saveTimer <= 0f) { saveTimer = 15f; Save(); }
    }

    public int VisualCount => visualsRoot != null ? visualsRoot.childCount : 0;

    public void BuildVisuals()
    {
        builtVersion = Net.Version;
        if (visualsRoot != null) Destroy(visualsRoot.gameObject);
        arms.Clear();
        visualsRoot = new GameObject("Irrigation Network").transform;
        var steel = Build.Mat(new Color(0.62f, 0.64f, 0.66f), 0.55f, 0.6f);
        var blue = Build.Mat(new Color(0.15f, 0.35f, 0.75f), 0.4f);
        var pipeMat = Build.Mat(new Color(0.12f, 0.12f, 0.13f), 0.3f);
        var concrete = Build.Mat(new Color(0.7f, 0.68f, 0.62f), 0.2f);
        var water = Build.Mat(new Color(0.25f, 0.45f, 0.55f), 0.85f);
        var ring = Build.Mat(new Color(0.3f, 0.7f, 1f), 0.2f, 0f, new Color(0.1f, 0.4f, 0.8f));
        var ff = FarmFields.Instance;
        foreach (var n in Net.Nodes)
        {
            var root = new GameObject(n.kind + " " + n.id).transform;
            root.SetParent(visualsRoot, false);
            root.position = Build.OnGround(n.pos);
            var f = ff != null ? ff.Find(n.field) : null;
            switch (n.kind)
            {
                case IrrKind.Well:
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.4f, 0.9f), concrete, "Well head");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 1f, 0), new Vector3(0.6f, 0.5f, 0.5f), blue, "Pump");
                    break;
                case IrrKind.DeepWell:
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.5f, 0), new Vector3(1.1f, 0.5f, 1.1f), concrete, "Well head");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 1.3f, 0), new Vector3(0.9f, 0.8f, 0.7f), blue, "Pump");
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 2.2f, 0), new Vector3(0.12f, 0.6f, 0.12f), steel, "Riser");
                    break;
                case IrrKind.Canal:
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.3f, 0), new Vector3(1.6f, 0.6f, 1.2f), concrete, "Intake");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.62f, 0), new Vector3(1.3f, 0.04f, 0.9f), water, "Water");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 1f, 0.5f), new Vector3(1.2f, 0.7f, 0.08f), steel, "Gate");
                    break;
                case IrrKind.Tank:
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.4f, 0), new Vector3(3f, 1.4f, 3f), steel, "Tank");
                    break;
                case IrrKind.Junction:
                    Build.Part(PrimitiveType.Sphere, root, new Vector3(0, 0.15f, 0), Vector3.one * 0.35f, pipeMat, "Junction");
                    break;
                case IrrKind.Sensor:
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.5f, 0), new Vector3(0.05f, 0.5f, 0.05f), steel, "Post");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 1.05f, 0), new Vector3(0.22f, 0.16f, 0.1f), Build.Mat(new Color(0.95f, 0.75f, 0.1f), 0.4f), "Logger");
                    break;
                case IrrKind.Pivot:
                {
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.8f, 0), new Vector3(0.35f, 1.8f, 0.35f), steel, "Tower");
                    var arm = new GameObject("Arm").transform; arm.SetParent(root, false);
                    Build.Part(PrimitiveType.Cube, arm, new Vector3(0, 3.2f, n.size * 0.5f), new Vector3(0.15f, 0.15f, n.size), steel, "Span");
                    int towers = Mathf.Max(1, Mathf.RoundToInt(n.size / 15f));
                    for (int t = 1; t <= towers; t++)
                    {
                        float z = n.size * t / towers;
                        Build.Part(PrimitiveType.Cube, arm, new Vector3(0, 1.6f, z), new Vector3(0.12f, 3.2f, 0.12f), steel, "Tower leg");
                        Build.Part(PrimitiveType.Cylinder, arm, new Vector3(0, 0.45f, z), new Vector3(0.9f, 0.12f, 0.9f), pipeMat, "Wheel", new Vector3(0, 0, 90));
                    }
                    arms[n.id] = arm;
                    if (ShowCoverage) Ring(root, n.size, ring);
                    break;
                }
                case IrrKind.Linear:
                {
                    var cart = new GameObject("Linear cart").transform; cart.SetParent(visualsRoot, false);
                    float w = f != null ? f.depth : n.size;
                    Build.Part(PrimitiveType.Cube, cart, new Vector3(0, 3f, 0), new Vector3(0.15f, 0.15f, w), steel, "Span");
                    Build.Part(PrimitiveType.Cube, cart, new Vector3(0, 1.5f, -w * 0.5f), new Vector3(0.6f, 3f, 0.6f), blue, "Cart");
                    arms[n.id] = cart;
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.6f, 0.5f), blue, "Feed");
                    break;
                }
                case IrrKind.Sprinkler:
                    Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.6f, 0), new Vector3(0.05f, 0.6f, 0.05f), steel, "Riser");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0.1f, 1.22f, 0), new Vector3(0.25f, 0.06f, 0.06f), steel, "Head");
                    if (ShowCoverage) Ring(root, n.size, ring);
                    break;
                case IrrKind.Drip:
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.25f, 0), new Vector3(0.6f, 0.5f, 0.4f), blue, "Filter");
                    if (f != null)
                    {
                        int lines = Mathf.Clamp(Mathf.RoundToInt(f.width / 1.5f), 2, 40);
                        for (int l = 0; l < lines; l++)
                        {
                            float x = (l + 0.5f) / lines * f.width - f.width * 0.5f;
                            var line = Build.Part(PrimitiveType.Cube, visualsRoot, Vector3.zero, new Vector3(0.03f, 0.03f, f.depth), pipeMat, "Drip line");
                            line.transform.position = f.center + f.Rotation * new Vector3(x, 0.08f, 0f);
                            line.transform.rotation = f.Rotation;
                        }
                    }
                    break;
                case IrrKind.Flood:
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.25f, 0), new Vector3(1f, 0.5f, 0.3f), concrete, "Gate");
                    Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.55f, 0), new Vector3(0.8f, 0.3f, 0.06f), steel, "Board");
                    break;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        }
        foreach (var p in Net.Pipes)
        {
            var a = Net.Node(p.a); var b = Net.Node(p.b);
            if (a == null || b == null) continue;
            Vector3 pa = Build.OnGround(a.pos, 0.08f), pb = Build.OnGround(b.pos, 0.08f);
            float len = Vector3.Distance(pa, pb);
            if (len < 0.05f) continue;
            float d = p.diameterMm / 1000f;
            var pipe = Build.Part(PrimitiveType.Cylinder, visualsRoot, Vector3.zero, new Vector3(d, len * 0.5f, d), pipeMat, "Pipe " + p.diameterMm + " mm");
            pipe.transform.position = (pa + pb) * 0.5f;
            pipe.transform.rotation = Quaternion.LookRotation((pb - pa).normalized) * Quaternion.Euler(90f, 0f, 0f);   // cylinder axis (up) along the pipe
        }
    }

    private static void Ring(Transform parent, float radius, Material mat)
    {
        int segs = Mathf.Clamp(Mathf.RoundToInt(radius * 1.5f), 16, 96);
        for (int s = 0; s < segs; s++)
        {
            float a = s * Mathf.PI * 2f / segs;
            Build.Part(PrimitiveType.Cube, parent, new Vector3(Mathf.Sin(a) * radius, 0.12f, Mathf.Cos(a) * radius), new Vector3(0.12f, 0.06f, radius * 6.3f / segs), mat, "Coverage", new Vector3(0, a * Mathf.Rad2Deg, 0));
        }
    }

    public void SetCoverage(bool on) { if (ShowCoverage == on) return; ShowCoverage = on; builtVersion = -1; }

    // ------------------------------------------------------------------ save

    private string GwKey => P + "gw." + (Regions.Current != null ? Regions.Current.id : Regions.DefaultId);

    public void Save()
    {
        if (NetSession.IsClient) return;
        SaveData.SetString(P + "net", Net.Encode());
        SaveData.SetFloat(GwKey, Net.GroundwaterM);
        SaveData.SetFloat(P + "total", WaterM3Total);
        SaveData.Save();
    }

    public void Load()
    {
        Net.Decode(SaveData.GetString(P + "net", ""));
        Net.GroundwaterM = SaveData.GetFloat(GwKey, FcData.CurrentRegion.gwStart);
        WaterM3Total = SaveData.GetFloat(P + "total", 0f);
    }

    /// <summary>Co-op: take the host's network (not saved here).</summary>
    public void ApplyRemote(string encoded, float groundwater)
    {
        if (encoded != Net.Encode()) Net.Decode(encoded);
        Net.GroundwaterM = groundwater;
    }

    private void OnNewGame()
    {
        Net.Clear();
        Net.GroundwaterM = FcData.CurrentRegion.gwStart;
        WaterM3Today = WaterM3Total = CostToday = EnergyKwhToday = unpaid = 0f;
        Save();
    }

    private void OnRegion(string id) { Net.Clear(); Net.GroundwaterM = SaveData.GetFloat(GwKey, FcData.CurrentRegion.gwStart); Save(); }

    // ------------------------------------------------------------------ HUD

    private static readonly Func<string> hudLine = () => Instance != null ? Instance.HudLine() : null;

    private string HudLine()
    {
        if (FarmModifiers.IrrigationBanned && Net.Nodes.Exists(n => n.IsEmitter)) return Loc.Display(FcText.R("Irrigation stopped") + ": " + FarmModifiers.BanText());
        foreach (var n in Net.Nodes)
            if (n.IsEmitter && n.running && !string.IsNullOrEmpty(n.shortage))
                return Loc.Display(FcText.R("Irrigation") + ": " + FcText.R(n.kind.ToString()) + " - " + FcText.R(n.shortage));
        return null;
    }
}
