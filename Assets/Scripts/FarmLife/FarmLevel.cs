using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] class FarmLevelSave { public float xp; public int level = 1; public List<int> pending = new List<int>(); }

/// <summary>
/// Farm level and XP (like FarmVille / Hay Day): almost every action gives XP (harvests, sales, products, missions,
/// contracts, fishing, quests, friends, festivals... rates in farmlife.json "xp"). Each level unlocks farm decor and
/// roadside stand features and has a reward; a celebration card announces it and the reward is collected from the
/// card / Farm life journal ([Y] / D-pad up), so nothing is paid behind the player's back. Saved as mazraa.fl.level.
/// </summary>
[MazraaSystem(73)]
public class FarmLevel : MonoBehaviour
{
    public const string SaveKey = "mazraa.fl.level";
    public static FarmLevel Instance { get; private set; }

    private FarmLevelSave state = new FarmLevelSave();
    private readonly Dictionary<string, float> rates = new Dictionary<string, float>();

    public int Level => state.level;
    public float Xp => state.xp;
    public IReadOnlyList<int> Pending => state.pending;
    public event Action<int> LeveledUp;

    private void Awake()
    {
        Instance = this;
        foreach (var x in FarmLife.Data.xp) if (x != null && !string.IsNullOrEmpty(x.id)) rates[x.id] = x.xp;
        state = FarmLife.Load<FarmLevelSave>(SaveKey);
        if (state.level < 1) state.level = 1;
        if (state.pending == null) state.pending = new List<int>();
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void OnEnable()
    {
        GameEvents.Harvested += OnHarvested;
        GameEvents.Delivered += OnDelivered;
        GameEvents.ProductsCollected += OnProducts;
        GameEvents.AnimalBorn += OnBorn;
        GameEvents.MissionCompleted += OnMission;
        GameEvents.ContractCompleted += OnContract;
        GameEvents.UpgradeBought += OnUpgrade;
        GameEvents.TractorWorked += OnTractor;
        GameEvents.WellStarted += OnWell;
        GameEvents.Custom += OnCustom;
        GameEvents.NewGameStarted += OnNewGame;
        FieldPlot.FieldHarvested += OnFieldHarvested;
        FieldPlot.FieldPlanted += OnPlanted;
    }

    private void OnDisable()
    {
        GameEvents.Harvested -= OnHarvested;
        GameEvents.Delivered -= OnDelivered;
        GameEvents.ProductsCollected -= OnProducts;
        GameEvents.AnimalBorn -= OnBorn;
        GameEvents.MissionCompleted -= OnMission;
        GameEvents.ContractCompleted -= OnContract;
        GameEvents.UpgradeBought -= OnUpgrade;
        GameEvents.TractorWorked -= OnTractor;
        GameEvents.WellStarted -= OnWell;
        GameEvents.Custom -= OnCustom;
        GameEvents.NewGameStarted -= OnNewGame;
        FieldPlot.FieldHarvested -= OnFieldHarvested;
        FieldPlot.FieldPlanted -= OnPlanted;
    }

    private void OnDestroy()
    {
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void OnNewGame() { state = new FarmLevelSave(); Save(); }

    // ---------------- XP sources ----------------

    public float Rate(string id) => rates.TryGetValue(id, out var r) ? r : 0f;
    private void Give(string id, float units) { float r = Rate(id); if (r > 0f && units > 0f) AddXp(r * units); }

    private void OnHarvested(Cropbed b, float kg) => Give("harvestKg", kg);
    private void OnFieldHarvested(FieldPlot p, string crop, float kg) { Give("fieldHarvest", 1f); Give("fieldKg", kg); }
    private void OnPlanted(FieldPlot p) => Give("plant", 1f);
    private void OnDelivered(float kg, int money) => Give("deliverMoney", money);
    private void OnProducts() => Give("products", 1f);
    private void OnBorn(string id) => Give("animalBorn", 1f);
    private void OnMission(MissionDef m) => Give("mission", 1f);
    private void OnContract() => Give("contract", 1f);
    private void OnUpgrade(string id) => Give("upgrade", 1f);
    private void OnTractor(Implement i) => Give("tractor", 1f);
    private void OnWell(WellController w) => Give("well", 1f);
    private void OnCustom(string type, float amount) { if (type != null && rates.ContainsKey(type)) Give(type, amount); }

    // ---------------- levels ----------------

    /// <summary>Total XP needed to reach a level (level 1 = 0).</summary>
    public static float LevelXp(int level)
    {
        if (level <= 1) return 0f;
        var rows = FarmLife.Data.levels;
        float last = 0f; int lastLevel = 1, prevStep = 100;
        foreach (var r in rows)
        {
            if (r.level == level) return r.need;
            if (r.level < level && r.level > lastLevel) { prevStep = Mathf.Max(50, Mathf.RoundToInt(r.need - last)); last = r.need; lastLevel = r.level; }
        }
        float xp = last, step = prevStep;
        for (int l = lastLevel + 1; l <= level; l++) { step *= Mathf.Max(1.01f, FarmLife.Data.extraLevelGrowth); xp += Mathf.Round(step / 10f) * 10f; }
        return xp;
    }

    public static FLLevelRow Row(int level) => Array.Find(FarmLife.Data.levels, r => r.level == level);

    public float NextLevelXp => LevelXp(state.level + 1);
    /// <summary>0..1 progress to the next level.</summary>
    public float Progress { get { float a = LevelXp(state.level), b = NextLevelXp; return b > a ? Mathf.Clamp01((state.xp - a) / (b - a)) : 1f; } }

    public void AddXp(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount)) return;
        state.xp += amount;
        int guard = 0;
        while (state.xp >= NextLevelXp && guard++ < 50)
        {
            state.level++;
            state.pending.Add(state.level);
            Celebrate(state.level);
            LeveledUp?.Invoke(state.level);
        }
        Save();
    }

    private void Save() => FarmLife.Store(SaveKey, state);

    private void Celebrate(int level)
    {
        var lines = new List<string>();
        foreach (var u in UnlocksAt(level)) lines.Add("+ " + UnlockName(u));
        var row = Row(level);
        string reward = FarmLife.Describe(row != null ? row : AutoReward(level));
        if (reward != "") lines.Add(Loc.Raw("Reward") + ": " + reward);
        FarmLife.Card(Loc.Raw("Farm level up!") + "  " + Loc.Raw("Level") + " " + level, string.Join("\n", lines.ToArray()), Loc.Raw("[Y] / D-pad up: open the Farm life journal to collect"));
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
    }

    /// <summary>Reward for levels past the table: some money and feed.</summary>
    public static FLReward AutoReward(int level) => new FLReward { money = 600 + level * 50, feed = 10 };

    public static List<string> UnlocksAt(int level)
    {
        var list = new List<string>();
        var row = Row(level);
        if (row != null && row.unlocks != null) list.AddRange(row.unlocks);
        foreach (var d in FarmLife.Data.decor) if (d.minLevel == level && !list.Contains(d.id)) list.Add(d.id);
        return list;
    }

    /// <summary>Level that unlocks a decor / feature id (1 = from the start).</summary>
    public static int UnlockLevel(string id)
    {
        var d = FarmLife.Decor(id);
        if (d != null && d.minLevel > 0) return d.minLevel;
        foreach (var r in FarmLife.Data.levels) if (r.unlocks != null && Array.IndexOf(r.unlocks, id) >= 0) return r.level;
        return 1;
    }

    public static bool IsUnlocked(string id) => (Instance != null ? Instance.Level : 1) >= UnlockLevel(id);

    public static string UnlockName(string id)
    {
        var d = FarmLife.Decor(id);
        if (d != null) return FarmLife.Pick(d.en, d.ar) + " (" + Loc.Raw("Decor") + ")";
        var f = Array.Find(FarmLife.Data.features, x => x.id == id);
        return f != null ? FarmLife.Pick(f.en, f.ar) : id;
    }

    /// <summary>Collect every pending level reward. Returns the translated summary ("" when nothing was waiting).</summary>
    public string ClaimPending()
    {
        if (state.pending.Count == 0) return "";
        var parts = new List<string>();
        var levels = new List<int>(state.pending);
        state.pending.Clear();
        Save();
        foreach (var l in levels)
        {
            var row = Row(l);
            string s = FarmLife.Grant(row != null ? row : AutoReward(l));
            if (s != "") parts.Add(Loc.Raw("Level") + " " + l + ": " + s);
        }
        string text = string.Join("\n", parts.ToArray());
        CozyFx.Alert(Loc.Display(Loc.Raw("Level rewards collected")));
        return text;
    }

    private static string HudLine()
    {
        var f = Instance;
        if (f == null) return null;
        string s = Loc.Raw("Farm level") + " " + f.Level + "  (" + Mathf.FloorToInt(f.Xp) + "/" + Mathf.FloorToInt(f.NextLevelXp) + " " + Loc.Raw("XP") + ")";
        if (f.state.pending.Count > 0) s += "  - " + Loc.Raw("reward ready [Y]");
        return Loc.Display(s);
    }
}
