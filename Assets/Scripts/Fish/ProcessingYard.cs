using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Processing buildings bought in the shop (page "Processing"): date packing house, dairy, flour mill, juice press.
/// E at a building starts a batch: it takes up to inputAmount of an input good from farm storage, works for
/// 'seconds' game seconds and puts the higher-value output into storage. Recipes in Resources/Data/processing.json
/// (a recipe lists alternative inputs, e.g. any fruit for the juice press; buildings with several recipes take turns).
/// Production lines (Production page, FarmInfoScreen): each building also has an input store and an output store. Supply
/// moves inputs from farm storage into the input store; the building then runs batches by itself at the same rate into its
/// output store; Collect moves outputs to farm storage, Sell sells them straight from the building (best selling point),
/// Auto supplies and collects after every batch and each morning. A building that worked during a day costs its dailyCost
/// (booked as production costs). Buildings whose goods don't exist in this game stay locked.
/// </summary>
[MazraaSystem(62)]
public class ProcessingYard : MonoBehaviour
{
    public static ProcessingYard Instance;

    public class Plant
    {
        public ProcBuilding def;
        public bool owned, busy;
        public float timeLeft, outAmount;
        public int nextRecipe;
        public string output;
        public GameObject visual;
        // production line
        public readonly Dictionary<string, float> inStore = new Dictionary<string, float>();
        public readonly Dictionary<string, float> outStore = new Dictionary<string, float>();
        public bool auto, line, worked;
        public float InTotal { get { float s = 0f; foreach (var v in inStore.Values) s += v; return s; } }
        public float OutTotal { get { float s = 0f; foreach (var v in outStore.Values) s += v; return s; } }
    }

    public ProcFile Data { get; private set; }
    public readonly List<Plant> Plants = new List<Plant>();
    private Vector3 yard; private Quaternion yardRot = Quaternion.identity;
    private bool built;
    private float saveTimer;

    private void Awake()
    {
        Instance = this;
        var json = Resources.Load<TextAsset>("Data/processing");
        try { if (json != null) Data = JsonUtility.FromJson<ProcFile>(json.text); }
        catch (Exception e) { Debug.LogError("[Processing] Invalid processing.json: " + e.Message); }
        if (Data == null || Data.buildings == null) Data = new ProcFile { buildings = new ProcBuilding[0] };
        foreach (var b in Data.buildings)
        {
            if (b == null || string.IsNullOrEmpty(b.id)) continue;
            var p = new Plant { def = b };
            string k = Key(b.id);
            p.owned = SaveData.GetInt(k + "owned", 0) == 1;
            p.busy = SaveData.GetInt(k + "busy", 0) == 1;
            p.timeLeft = SaveData.GetFloat(k + "time", 0f);
            p.outAmount = SaveData.GetFloat(k + "out", 0f);
            p.output = SaveData.GetString(k + "output", "");
            p.nextRecipe = SaveData.GetInt(k + "next", 0);
            p.auto = SaveData.GetInt(k + "auto", 0) == 1;
            p.line = SaveData.GetInt(k + "line", 0) == 1;
            p.worked = SaveData.GetInt(k + "worked", 0) == 1;
            ReadStore(SaveData.GetString(k + "in", ""), p.inStore);
            ReadStore(SaveData.GetString(k + "outStore", ""), p.outStore);
            Plants.Add(p);
            var plant = p;
            ShopCatalog.Register(new ShopEntry
            {
                id = b.id, category = "Processing",
                name = () => FishRules.Name(plant.def.nameEn, plant.def.nameAr),
                detail = () => plant.owned ? "(" + FishText.Raw("Built") + ")" : "",
                price = () => plant.def.price,
                blockReason = () => plant.owned ? "Already built" : !Usable(plant) ? "Needs goods this map doesn't have" : null,
                onBought = () => { plant.owned = true; SavePlant(plant); if (Instance != null) Instance.BuildVisual(plant); }
            });
        }
    }

    private static string Key(string id) => "mazraa.fish.proc." + id + ".";

    private void Start()
    {
        yard = FishSite.At("ProcessingSpot", -8f, -18f);
        yardRot = FishSite.Facing();
        built = true;
        int rows = Mathf.Max(1, (Plants.Count + 3) / 4);
        var sign = new GameObject("Processing Yard");
        sign.transform.SetPositionAndRotation(yard, yardRot);
        Build.Part(PrimitiveType.Cube, sign.transform, new Vector3(0, 0.03f, -(rows - 1) * 3.75f), new Vector3(30f, 0.04f, 7f + (rows - 1) * 7.5f), Build.Mat(new Color(0.55f, 0.53f, 0.5f), 0.1f), "Yard floor");
        string label = "Processing yard";
        Build.Label(sign.transform, new Vector3(0, 5.2f, 3.6f), label, 3f, Color.white);
        foreach (var p in Plants) if (p.owned) BuildVisual(p);
        GameEvents.NewGameStarted += ResetAll;
        WorldClock.DayStarted += OnNewDay;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= ResetAll;
        WorldClock.DayStarted -= OnNewDay;
        if (Instance == this) Instance = null;
    }

    public Plant Get(string id) => Plants.Find(p => p.def.id == id);

    /// <summary>First input good of the recipe that exists and is in storage (at least 1 unit), else null.</summary>
    public static string PickInput(ProcRecipe r, Func<string, float> stored, Func<string, bool> exists)
    {
        if (r == null || r.inputs == null) return null;
        foreach (var g in r.inputs) if (exists(g) && stored(g) >= 1f) return g;
        return null;
    }

    /// <summary>Output for 'take' units of input (proportional to a full batch).</summary>
    public static float OutputFor(ProcRecipe r, float take) => r == null || r.inputAmount <= 0f ? 0f : r.outputAmount * take / r.inputAmount;

    /// <summary>E at a building: start a batch, or report progress. Returns the localized message.</summary>
    public string Use(string id)
    {
        var p = Get(id);
        string msg = p == null ? "" : StartBatch(p);
        if (!string.IsNullOrEmpty(msg) && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    private string StartBatch(Plant p)
    {
        if (!p.owned) return FishText.T("Buy this building at the shop first");
        if (p.busy) return FishText.T("Working... {0} s left", Mathf.CeilToInt(p.timeLeft));
        var eco = EconomyManager.Instance;
        if (eco == null || p.def.recipes == null || p.def.recipes.Length == 0) return "";
        int n = p.def.recipes.Length;
        for (int k = 0; k < n; k++)
        {
            int i = (p.nextRecipe + k) % n;
            var r = p.def.recipes[i];
            if (eco.Good(r.output) == null) continue;
            string input = PickInput(r, eco.Stored, g => eco.Good(g) != null);
            if (input == null) continue;
            float take = Mathf.Min(eco.Stored(input), r.inputAmount);
            if (!eco.TakeGood(input, take)) continue;
            p.busy = true; p.worked = true; p.line = false;
            p.timeLeft = Mathf.Max(1f, p.def.seconds);
            p.output = r.output;
            p.outAmount = OutputFor(r, take);
            p.nextRecipe = (i + 1) % n;
            SavePlant(p);
            var inGood = eco.Good(input); var outGood = eco.Good(r.output);
            return Loc.Display(FishText.Raw("Processing started") + ": " + take.ToString("F0") + " " + EconomyManager.Unit(inGood) + " " +
                EconomyManager.GoodName(inGood) + " -> " + EconomyManager.GoodName(outGood));
        }
        return Loc.Display(FishText.Raw("Needs in storage") + ": " + NeedsText(p));
    }

    private string NeedsText(Plant p)
    {
        var eco = EconomyManager.Instance;
        var names = new List<string>();
        if (eco != null && p.def.recipes != null)
            foreach (var r in p.def.recipes)
                if (r.inputs != null)
                    foreach (var g in r.inputs)
                    {
                        var gd = eco.Good(g);
                        if (gd != null && !names.Contains(EconomyManager.GoodName(gd))) names.Add(EconomyManager.GoodName(gd));
                    }
        return string.Join(" / ", names.ToArray());
    }

    private void Update()
    {
        bool any = false;
        foreach (var p in Plants)
        {
            if (!p.busy && p.owned && p.inStore.Count > 0) StartLine(p);
            if (!p.busy) continue;
            any = true;
            p.timeLeft -= Time.deltaTime;
            if (p.timeLeft <= 0f) Finish(p);
        }
        if (any && (saveTimer -= Time.deltaTime) <= 0f)
        {
            saveTimer = 5f;
            foreach (var p in Plants) if (p.busy) SavePlant(p);
        }
    }

    private void Finish(Plant p)
    {
        p.busy = false; p.timeLeft = 0f;
        var eco = EconomyManager.Instance;
        float amount = Mathf.Round(p.outAmount * 10f) / 10f;
        bool line = p.line;
        p.line = false;
        if (line && amount > 0f) p.outStore[p.output] = Stored(p.outStore, p.output) + amount;
        else if (eco != null && amount > 0f) eco.AddGood(p.output, amount);
        var g = eco != null ? eco.Good(p.output) : null;
        SavePlant(p);
        GameEvents.RaiseCustom("processBatch", 1f);
        GameEvents.RaiseCustom("process_" + p.output, amount);
        if (line) { if (p.auto) { Collect(p); Supply(p); } return; }   // production line: quiet, see the Production page
        if (UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(Loc.Display(FishRules.Name(p.def.nameEn, p.def.nameAr) + ": +" + amount.ToString("F0") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g)));
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
    }

    private string Prompt(Plant p)
    {
        if (p.busy) return FishText.T("Working... {0} s left", Mathf.CeilToInt(p.timeLeft));
        return Loc.Display(FishText.Raw("[E] Start processing") + " - " + FishRules.Name(p.def.nameEn, p.def.nameAr));
    }

    private static string HudLine()
    {
        var y = Instance;
        if (y == null) return null;
        var parts = new List<string>();
        foreach (var p in y.Plants) if (p.busy) parts.Add(FishRules.Name(p.def.nameEn, p.def.nameAr) + " " + Mathf.CeilToInt(p.timeLeft) + "s");
        if (parts.Count == 0) return null;
        return Loc.Display(FishText.Raw("Processing") + ": " + string.Join(", ", parts.ToArray()));
    }

    // ---------------- production lines ----------------

    /// <summary>True if at least one recipe's output and one of its inputs exist in this game.</summary>
    public static bool Usable(Plant p)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || p?.def?.recipes == null) return false;
        foreach (var r in p.def.recipes)
            if (r != null && eco.Good(r.output) != null && r.inputs != null)
                foreach (var g in r.inputs) if (eco.Good(g) != null) return true;
        return false;
    }

    /// <summary>Input goods of a building that exist in this game.</summary>
    public static List<string> Inputs(Plant p)
    {
        var list = new List<string>();
        var eco = EconomyManager.Instance;
        if (eco == null || p?.def?.recipes == null) return list;
        foreach (var r in p.def.recipes)
            if (r?.inputs != null && eco.Good(r.output) != null)
                foreach (var g in r.inputs) if (eco.Good(g) != null && !list.Contains(g)) list.Add(g);
        return list;
    }

    /// <summary>Units of input per minute of work (first recipe).</summary>
    public static float RatePerMinute(Plant p) => p?.def?.recipes == null || p.def.recipes.Length == 0 ? 0f : p.def.recipes[0].inputAmount * 60f / Mathf.Max(1f, p.def.seconds);

    private static float Stored(Dictionary<string, float> d, string id) => d.TryGetValue(id, out var v) ? v : 0f;

    /// <summary>Move inputs from farm storage into the building (up to its input capacity). Returns the amount moved.</summary>
    public float Supply(Plant p)
    {
        var eco = EconomyManager.Instance;
        if (p == null || !p.owned || eco == null) return 0f;
        float moved = 0f;
        foreach (var g in Inputs(p))
        {
            float room = Mathf.Max(0f, p.def.inputCapacity - p.InTotal);
            float take = Mathf.Min(room, Mathf.Floor(eco.Stored(g) * 10f) / 10f);
            if (take <= 0f || !eco.TakeGood(g, take)) continue;
            p.inStore[g] = Stored(p.inStore, g) + take;
            moved += take;
        }
        SavePlant(p);
        return moved;
    }

    /// <summary>Move finished goods from the building to farm storage. Returns the amount moved.</summary>
    public float Collect(Plant p)
    {
        var eco = EconomyManager.Instance;
        if (p == null || eco == null) return 0f;
        float moved = 0f;
        foreach (var kv in new List<KeyValuePair<string, float>>(p.outStore))
        {
            if (kv.Value > 0f && eco.Good(kv.Key) != null) { eco.AddGood(kv.Key, kv.Value); moved += kv.Value; }
            p.outStore.Remove(kv.Key);
        }
        SavePlant(p);
        return moved;
    }

    /// <summary>Sell the building's output store straight away (best selling point, contracts first). Returns SAR earned.</summary>
    public int SellOutputs(Plant p, List<string> notes = null)
    {
        var eco = EconomyManager.Instance;
        if (p == null || eco == null || p.outStore.Count == 0) return 0;
        var cargo = new Dictionary<string, float>(p.outStore);
        p.outStore.Clear();
        SavePlant(p);
        return eco.SellAll(cargo, notes ?? new List<string>());
    }

    public void SetAuto(Plant p, bool on)
    {
        if (p == null) return;
        p.auto = on;
        if (on) { Collect(p); Supply(p); }
        SavePlant(p);
    }

    /// <summary>Production line: start a batch from the building's own input store.</summary>
    private void StartLine(Plant p)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || p.def.recipes == null || p.def.recipes.Length == 0 || p.OutTotal >= p.def.outputCapacity) return;
        int n = p.def.recipes.Length;
        for (int k = 0; k < n; k++)
        {
            int i = (p.nextRecipe + k) % n;
            var r = p.def.recipes[i];
            if (eco.Good(r.output) == null) continue;
            string input = PickInput(r, g => Stored(p.inStore, g), g => eco.Good(g) != null);
            if (input == null) continue;
            float take = Mathf.Min(Stored(p.inStore, input), r.inputAmount);
            float left = Stored(p.inStore, input) - take;
            if (left < 0.05f) p.inStore.Remove(input); else p.inStore[input] = left;
            p.busy = true; p.line = true; p.worked = true;
            p.timeLeft = Mathf.Max(1f, p.def.seconds);
            p.output = r.output;
            p.outAmount = OutputFor(r, take);
            p.nextRecipe = (i + 1) % n;
            SavePlant(p);
            return;
        }
        // leftovers under one unit can't start a batch: give them back to the farm
        foreach (var kv in new List<KeyValuePair<string, float>>(p.inStore))
            if (kv.Value < 1f) { p.inStore.Remove(kv.Key); eco.AddGood(kv.Key, kv.Value); }
        SavePlant(p);
    }

    private void OnNewDay(int day)
    {
        if (NetSession.IsClient) return;   // co-op: the host runs daily costs
        var eco = EconomyManager.Instance;
        foreach (var p in Plants)
        {
            if (!p.owned) continue;
            if (p.worked && p.def.dailyCost > 0 && eco != null)
                using (Ledger.As(Ledger.Production)) eco.AddMoney(-p.def.dailyCost);
            p.worked = p.busy;
            if (p.auto) { Collect(p); Supply(p); }
            SavePlant(p);
        }
    }

    private static void ReadStore(string s, Dictionary<string, float> d)
    {
        d.Clear();
        if (string.IsNullOrEmpty(s)) return;
        foreach (var part in s.Split(';'))
        {
            int i = part.LastIndexOf(':');
            if (i <= 0) continue;
            if (float.TryParse(part.Substring(i + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0f)
                d[part.Substring(0, i)] = v;
        }
    }

    private static string WriteStore(Dictionary<string, float> d)
    {
        var parts = new List<string>();
        foreach (var kv in d) parts.Add(kv.Key + ":" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(";", parts.ToArray());
    }

    // ---------------- world ----------------

    private void BuildVisual(Plant p)
    {
        if (!built || p.visual != null) return;
        int index = Plants.IndexOf(p);
        var root = new GameObject("Processing " + p.def.id);
        Vector3 local = new Vector3(-10.5f + (index % 4) * 7f, 0, -(index / 4) * 7.5f);   // rows of four
        root.transform.SetPositionAndRotation(Build.OnGround(yard + yardRot * local), yardRot);
        p.visual = root;
        var walls = Build.Mat(new Color(p.def.r, p.def.g, p.def.b), 0.2f);
        var roof = Build.Mat(new Color(0.3f, 0.29f, 0.3f), 0.4f, 0.3f);
        var door = Build.Mat(new Color(0.35f, 0.22f, 0.12f), 0.2f);
        var steel = Build.Mat(new Color(0.6f, 0.61f, 0.63f), 0.6f, 0.6f);
        var plant = p;
        var spot = FishSpot.Create("Processing Door", root.transform, Vector3.zero, new Vector3(0, 1.5f, 0), new Vector3(5f, 3f, 4.5f), () => Prompt(plant), () => Use(plant.def.id));
        Build.Part(PrimitiveType.Cube, spot.transform, new Vector3(0, 1.5f, 0), new Vector3(5f, 3f, 4.5f), walls, "Walls");
        Build.Part(PrimitiveType.Cube, spot.transform, new Vector3(0, 3.1f, 0), new Vector3(5.4f, 0.2f, 4.9f), roof, "Roof");
        Build.Part(PrimitiveType.Cube, spot.transform, new Vector3(0, 1.1f, 2.27f), new Vector3(1.6f, 2.2f, 0.05f), door, "Door");
        switch (p.def.id)
        {
            case "proc_mill":
                Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(1.6f, 4.2f, -1f), new Vector3(1.2f, 1.1f, 1.2f), steel, "Silo");
                for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-1.2f, 4.6f, 2.3f), new Vector3(0.25f, 3f, 0.05f), door, "Sail", new Vector3(0, 0, i * 45f));
                break;
            case "proc_dairy":
                Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(-2.8f, 1.3f, 0), new Vector3(1f, 1.3f, 1f), steel, "Milk tank");
                break;
            case "proc_juice":
                for (int i = 0; i < 3; i++) Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(-1.2f + i * 0.4f, 0.25f, 2.7f), Vector3.one * 0.4f, Build.Mat(new Color(0.95f, 0.55f, 0.1f), 0.5f), "Fruit");
                break;
            default:
                for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-1.6f + i * 0.5f, 0.2f, 2.7f), new Vector3(0.45f, 0.4f, 0.35f), Build.Mat(new Color(0.62f, 0.45f, 0.26f), 0.1f), "Date box");
                break;
        }
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(1.8f, 3.8f, 1.2f), new Vector3(0.3f, 0.7f, 0.3f), steel, "Chimney");
        FishSite.SetLayer(spot.gameObject, 7);
        Build.Label(root.transform, new Vector3(0, 4.3f, 0), p.def.nameEn, 2.2f, Color.white);
    }

    private static void SavePlant(Plant p)
    {
        string k = Key(p.def.id);
        SaveData.SetInt(k + "owned", p.owned ? 1 : 0);
        SaveData.SetInt(k + "busy", p.busy ? 1 : 0);
        SaveData.SetFloat(k + "time", p.timeLeft);
        SaveData.SetFloat(k + "out", p.outAmount);
        SaveData.SetString(k + "output", p.output ?? "");
        SaveData.SetInt(k + "next", p.nextRecipe);
        SaveData.SetInt(k + "auto", p.auto ? 1 : 0);
        SaveData.SetInt(k + "line", p.line ? 1 : 0);
        SaveData.SetInt(k + "worked", p.worked ? 1 : 0);
        SaveData.SetString(k + "in", WriteStore(p.inStore));
        SaveData.SetString(k + "outStore", WriteStore(p.outStore));
        SaveData.Save();
    }

    public void ResetAll()
    {
        foreach (var p in Plants)
        {
            p.owned = false; p.busy = false; p.timeLeft = 0f; p.outAmount = 0f; p.output = ""; p.nextRecipe = 0;
            p.auto = false; p.line = false; p.worked = false; p.inStore.Clear(); p.outStore.Clear();
            if (p.visual != null) { Destroy(p.visual); p.visual = null; }
            SavePlant(p);
        }
    }
}
