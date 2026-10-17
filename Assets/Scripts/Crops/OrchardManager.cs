using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// A small orchard beside the open fields (or around an object named "OrchardSpot") with tree slots.
/// Saplings are bought in the Farm Shop ("Orchard" page) and planted with E. Trees age one season per in-game
/// season and fruit once a year in their fruit season once mature: date palms (Sukkari, Khalas, Ajwa) need
/// hand pollination in spring (E) for a full crop and are harvested in late summer; olives and pomegranates in
/// autumn, grapes in summer, lemons in winter. Fruit can be picked during its season and the next one, then it
/// spoils. Red palm weevil can attack palms (palm weevil trap, E). Saved under "mazraa.crops.tree.*".
/// </summary>
[MazraaSystem(31)]
public class OrchardManager : MonoBehaviour
{
    public static OrchardManager Instance;
    public int slots = 6, columns = 3;
    public float spacing = 5f;
    public readonly List<TreeSlot> Trees = new List<TreeSlot>();
    /// <summary>Sapling planted next when owned (the last one bought).</summary>
    public string PreferredSapling = "";

    private const string P = "mazraa.crops.";
    private readonly Dictionary<string, int> saplings = new Dictionary<string, int>();
    private bool built;
    private float waited;
    public const string LabelKey = "Orchard";

    private void Awake()
    {
        Instance = this;
        foreach (var t in CropsData.Data.trees) saplings[t.id] = SaveData.GetInt(P + "sapling." + t.id, 0);
        foreach (var t in CropsData.Data.trees)
        {
            var def = t;
            ShopCatalog.Register(new ShopEntry
            {
                id = "sapling_" + def.id,
                category = "Orchard",
                name = () => CropsData.Name(def),
                detail = () => "(" + (Instance != null ? Instance.Saplings(def.id) : 0) + ")",
                price = () => def.saplingPrice,
                onBought = () => { if (Instance != null) { Instance.AddSapling(def.id); Instance.PreferredSapling = def.id; } },
            });
        }
    }

    private void OnEnable()
    {
        WorldClock.DayStarted += OnDay;
        WorldClock.SeasonChanged += OnSeason;
        GameEvents.NewGameStarted += OnNewGame;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnDay;
        WorldClock.SeasonChanged -= OnSeason;
        GameEvents.NewGameStarted -= OnNewGame;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Build once the fields exist so the orchard sits next to them.
    private void Update()
    {
        if (built) return;
        waited += Time.deltaTime;
        var fm = FieldManager.Instance;
        if ((fm != null && fm.Plots.Count > 0) || waited > 2f) BuildOrchard();
    }

    public int Saplings(string id) => saplings.TryGetValue(id, out var n) ? n : 0;
    public void AddSapling(string id, int n = 1) { saplings[id] = Saplings(id) + n; SaveData.SetInt(P + "sapling." + id, saplings[id]); SaveData.Save(); }
    public bool UseSapling(string id)
    {
        if (Saplings(id) <= 0) return false;
        saplings[id]--; SaveData.SetInt(P + "sapling." + id, saplings[id]); SaveData.Save();
        return true;
    }

    /// <summary>The sapling E plants: the last one bought if still owned, else the first owned.</summary>
    public TreeDef NextSapling()
    {
        if (Saplings(PreferredSapling) > 0 && CropsData.Tree(PreferredSapling) != null) return CropsData.Tree(PreferredSapling);
        foreach (var t in CropsData.Data.trees) if (Saplings(t.id) > 0) return t;
        return null;
    }

    private void BuildOrchard()
    {
        built = true;
        Vector3 center; Quaternion rot;
        var spot = GameObject.Find("OrchardSpot");
        var fm = FieldManager.Instance;
        if (spot != null) { center = spot.transform.position; rot = spot.transform.rotation; }
        else if (fm != null && fm.Plots.Count > 0)
        {
            // To the right of the field block.
            Vector3 sum = Vector3.zero;
            rot = fm.Plots[0].transform.rotation;
            float maxRight = float.MinValue;
            foreach (var p in fm.Plots) { sum += p.transform.position; }
            Vector3 mid = sum / fm.Plots.Count;
            Vector3 right = rot * Vector3.right;
            foreach (var p in fm.Plots) maxRight = Mathf.Max(maxRight, Vector3.Dot(p.transform.position - mid, right) + fm.plotSize.x * 0.5f);
            center = mid + right * (maxRight + 4f + (columns - 1) * spacing * 0.5f + 1.5f);
        }
        else
        {
            var inputs = FindAnyObjectByType<StarterAssetsInputs>();
            Transform start = inputs != null ? inputs.transform : transform;
            Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            rot = Quaternion.LookRotation(fwd);
            center = start.position + fwd * 18f + rot * Vector3.right * 22f;
        }

        var root = new GameObject("Orchard").transform;
        root.SetPositionAndRotation(Build.OnGround(center), rot);
        int rows = Mathf.CeilToInt(slots / (float)Mathf.Max(1, columns));
        // Sandy ground patch and a sign.
        Build.Part(PrimitiveType.Cube, root, new Vector3(0, 0.02f, 0), new Vector3(columns * spacing + 1f, 0.04f, rows * spacing + 1f), Build.Mat(new Color(0.78f, 0.66f, 0.45f), 0.05f), "OrchardGround");
        Build.Label(root, new Vector3(0, 3.2f, -(rows * spacing) * 0.5f - 0.8f), LabelKey, 6f, new Color(0.95f, 0.85f, 0.5f));
        for (int i = 0; i < slots; i++)
        {
            int r = i / columns, c = i % columns;
            Vector3 local = new Vector3((c - (columns - 1) * 0.5f) * spacing, 0, (r - (rows - 1) * 0.5f) * spacing);
            var go = new GameObject("Tree slot " + (i + 1));
            go.transform.SetPositionAndRotation(Build.OnGround(root.position + rot * local), rot);
            go.transform.SetParent(root, true);
            var slot = go.AddComponent<TreeSlot>();
            slot.Setup(i);
            Trees.Add(slot);
        }
    }

    private void OnDay(int day)
    {
        var clock = WorldClock.Instance;
        float perDay = 1f / Mathf.Max(1, clock != null ? clock.daysPerSeason : 5);
        foreach (var t in Trees) if (t != null) t.DailyTick(perDay);
    }

    private void OnSeason(Season s)
    {
        foreach (var t in Trees) if (t != null) t.SeasonTick(s);
    }

    public void LocustDamage(float amount) { foreach (var t in Trees) if (t != null) t.AddDamage(amount); }

    private static string HudLine()
    {
        var me = Instance; if (me == null) return null;
        int planted = 0, ripe = 0, pollinate = 0, weevil = 0;
        foreach (var t in me.Trees)
        {
            if (t == null || t.Def == null) continue;
            planted++;
            if (t.FruitKg > 0f) ripe++;
            if (t.NeedsPollination) pollinate++;
            if (t.Pest != "") weevil++;
        }
        if (planted == 0) return null;
        string s = CropsText.R("Orchard") + ": " + planted + " " + CropsText.R("trees");
        if (ripe > 0) s += ", " + ripe + " " + CropsText.R("ready to pick");
        if (pollinate > 0) s += ", " + pollinate + " " + CropsText.R("to pollinate");
        if (weevil > 0) s += ", " + weevil + " " + CropsText.R("with weevil");
        return Loc.Display(s);
    }

    private void OnNewGame()
    {
        foreach (var t in CropsData.Data.trees) saplings[t.id] = 0;
        PreferredSapling = "";
        foreach (var t in Trees) if (t != null) t.Clear();
    }
}
