using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class QuestTemplate
{
    public string id, type;           // deliver | collectEggs | earn | harvestKg | plantField | petSaluki | flyFalcon | talkVillager
    public string[] goods;            // deliver: goods to pick from
    public int min = 1, max = 1, step = 1;
    public int rewardBase;
    public float rewardPerUnit;
    public string needsAnimal;        // only offered when this animal is owned
    public string titleEn, titleAr;   // {0} amount, {1} unit, {2} good name
    public string[] regions;          // maps with these quests (empty = the base game, Najd); packs in Resources/Data/Quests
    public bool custom;               // progress from GameEvents.Custom events named like the type (e.g. event_canal_turn, harvest_cotton)
}
[Serializable] public class QuestFile { public QuestTemplate[] templates; }
[Serializable] public class ActiveQuest { public string template, type, good; public int target, reward; public float progress; public bool done; }
[Serializable] class QuestState { public int day; public List<ActiveQuest> quests = new List<ActiveQuest>(); }

/// <summary>
/// Daily quests board at the market (E): three random tasks a day from Resources/Data/quests.json (deliver X kg of Y
/// from the farm store, collect eggs, earn N SAR, harvest, pet the saluki...). Progress comes from game events;
/// delivery quests are handed in at the board (1-3). New quests every in-game day. Saved as mazraa.cozy.quests.
/// </summary>
[MazraaSystem(62)]
public class DailyQuests : MonoBehaviour
{
    public static DailyQuests Instance;
    public CozyPanel Panel { get; private set; }
    public GameObject Board { get; private set; }

    private QuestTemplate[] templates = new QuestTemplate[0];
    private QuestTemplate[] allTemplates = new QuestTemplate[0];
    public QuestTemplate[] Templates => templates;
    private QuestState state = new QuestState();
    private float lastEggs;

    public List<ActiveQuest> Quests => state.quests;
    public int DoneCount { get { int n = 0; foreach (var q in state.quests) if (q.done) n++; return n; } }

    private void Awake()
    {
        Instance = this;
        var json = Resources.Load<TextAsset>("Data/quests");
        var all = new List<QuestTemplate>();
        try { var f = json != null ? JsonUtility.FromJson<QuestFile>(json.text) : null; if (f != null && f.templates != null) all.AddRange(f.templates); }
        catch (Exception e) { Debug.LogError("[Quests] Invalid quests.json: " + e.Message); }
        foreach (var pack in DataPacks.LoadAs<QuestFile>("Quests")) if (pack.templates != null) all.AddRange(pack.templates);   // map packs (Egypt DLC)
        allTemplates = all.ToArray();
        FilterForRegion();
        try { var s = JsonUtility.FromJson<QuestState>(SaveData.GetString("mazraa.cozy.quests", "")); if (s != null && s.quests != null) state = s; }
        catch (Exception) { }
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        Panel = new CozyPanel(transform, "Daily Quests UI", 860f);
        WorldClock.DayStarted += OnDay;
        GameEvents.Custom += OnCustom;
        GameEvents.Harvested += OnHarvest;
        GameEvents.Delivered += OnDelivered;
        GameEvents.ProductsCollected += OnCollected;
        FieldPlot.FieldPlanted += OnPlanted;
        GameEvents.NewGameStarted += OnNewGame;
        GameEvents.RegionChanged += OnRegion;
        Loc.OnLanguageChanged += Refresh;
        if (state.day != CozyFx.Today || state.quests.Count == 0) Roll(CozyFx.Today);
        Invoke(nameof(BuildBoard), 1.5f);
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.Custom -= OnCustom;
        GameEvents.Harvested -= OnHarvest;
        GameEvents.Delivered -= OnDelivered;
        GameEvents.ProductsCollected -= OnCollected;
        FieldPlot.FieldPlanted -= OnPlanted;
        GameEvents.NewGameStarted -= OnNewGame;
        GameEvents.RegionChanged -= OnRegion;
        Loc.OnLanguageChanged -= Refresh;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private static string HudLine()
    {
        var d = Instance;
        if (d == null || d.state.quests.Count == 0) return null;
        return Loc.T("Daily quests: {0}/{1}", d.DoneCount, d.state.quests.Count);
    }

    private void OnNewGame() { Roll(CozyFx.Today); }
    private void OnRegion(string regionId) { FilterForRegion(); Roll(CozyFx.Today); }

    /// <summary>Keep the current map's quest templates (Najd: the base ones; Egypt: its own, in Egyptian Arabic).</summary>
    public void FilterForRegion()
    {
        var list = new List<QuestTemplate>();
        foreach (var t in allTemplates) if (t != null && Regions.HasContent(t.regions)) list.Add(t);
        templates = list.ToArray();
    }
    private void OnDay(int day) { Roll(day); CozyFx.Alert(Loc.T("New daily quests at the market board")); }

    // ---------------- rolling ----------------

    public void Roll(int day)
    {
        state = new QuestState { day = day };
        var eco = EconomyManager.Instance;
        var pool = new List<QuestTemplate>();
        foreach (var t in templates)
        {
            if (!string.IsNullOrEmpty(t.needsAnimal) && (eco == null || eco.Owned(t.needsAnimal) <= 0)) continue;
            pool.Add(t);
        }
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            var t = pool[UnityEngine.Random.Range(0, pool.Count)];
            pool.Remove(t);
            int steps = Mathf.Max(0, (t.max - t.min) / Mathf.Max(1, t.step));
            int target = t.min + UnityEngine.Random.Range(0, steps + 1) * Mathf.Max(1, t.step);
            string good = t.goods != null && t.goods.Length > 0 ? t.goods[UnityEngine.Random.Range(0, t.goods.Length)] : null;
            if (good != null && eco != null && !eco.GoodAvailable(good)) { i--; continue; }   // never ask for a crop this map can't grow
            int reward = t.rewardBase + Mathf.RoundToInt(target * t.rewardPerUnit);
            state.quests.Add(new ActiveQuest { template = t.id, type = t.type, good = good, target = target, reward = reward });
        }
        Save();
        Refresh();
    }

    private void Save() { SaveData.SetString("mazraa.cozy.quests", JsonUtility.ToJson(state)); SaveData.Save(); }

    // ---------------- progress ----------------

    private void Update()
    {
        if (EconomyManager.Instance != null) lastEggs = EconomyManager.Instance.Stored("eggs");
        if (Panel == null || !Panel.IsOpen) return;
        if (Panel.CloseRequested || (CozyKeys.Down("e") && Time.frameCount != Panel.OpenedFrame)) { Panel.Close(); return; }
        int d = CozyKeys.Digit();
        if (d > 0 && d <= state.quests.Count) HandIn(d - 1);
    }

    private void Add(string type, float amount, string good = null)
    {
        if (amount <= 0f) return;
        bool changed = false;
        foreach (var q in state.quests)
        {
            if (q.done || q.type != type || (good != null && q.good != good)) continue;
            q.progress = Mathf.Min(q.target, q.progress + amount);
            changed = true;
            if (q.progress >= q.target - 0.001f) Complete(q);
        }
        if (changed) { Save(); Refresh(); }
    }

    private void Complete(ActiveQuest q)
    {
        q.done = true;
        if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(q.reward);
        CozyFx.Alert(Loc.Display(Loc.Raw("Daily quest complete") + ": " + Title(q) + "  +" + q.reward + " " + Loc.Raw("SAR")));
        GameEvents.RaiseCustom("dailyQuestDone", 1f);
    }

    private void OnCustom(string type, float amount)
    {
        if (type == "petSaluki" || type == "flyFalcon" || type == "talkVillager") Add(type, amount);
        else if (Array.Exists(templates, t => t.custom && t.type == type)) Add(type, amount);
    }
    private void OnHarvest(Cropbed b, float kg) => Add("harvestKg", kg);
    private void OnDelivered(float kg, int money) => Add("earn", money);
    private void OnPlanted(FieldPlot p) => Add("plantField", 1f);
    private void OnCollected()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        float now = eco.Stored("eggs");
        Add("collectEggs", now - lastEggs);
        lastEggs = now;
    }

    /// <summary>Hand in goods from the farm store for a delivery quest (slot 0-2). Returns the amount handed in.</summary>
    public float HandIn(int slot)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || slot < 0 || slot >= state.quests.Count) return 0f;
        var q = state.quests[slot];
        if (q.done || q.type != "deliver") return 0f;
        float need = q.target - q.progress;
        float give = Mathf.Min(need, eco.Stored(q.good));
        if (give < 0.5f) { CozyFx.Alert(Loc.T("Not enough in the farm store yet")); return 0f; }
        if (!eco.TakeGood(q.good, give)) return 0f;
        Add("deliver", give, q.good);
        return give;
    }

    public string Title(ActiveQuest q)
    {
        var t = Array.Find(templates, x => x.id == q.template);
        string fmt = t == null ? q.type : (Loc.IsArabic && !string.IsNullOrEmpty(t.titleAr) ? t.titleAr : t.titleEn);
        var g = q.good != null && EconomyManager.Instance != null ? EconomyManager.Instance.Good(q.good) : null;
        return string.Format(fmt, q.target, g != null ? EconomyManager.Unit(g) : "", g != null ? EconomyManager.GoodName(g) : "");
    }

    // ---------------- board + panel ----------------

    private void BuildBoard()
    {
        Vector3 pos = DeliveryZone.Market != null ? DeliveryZone.Market.transform.position + new Vector3(-7.5f, 0, 3f) : CozyFx.FarmStart + new Vector3(5f, 0, 8f);
        var root = new GameObject("Daily Quest Board");
        root.transform.position = Build.OnGround(pos);
        Vector3 look = (DeliveryZone.Market != null ? DeliveryZone.Market.transform.position : CozyFx.FarmStart) - root.transform.position; look.y = 0f;
        if (look.sqrMagnitude > 0.01f) root.transform.rotation = Quaternion.LookRotation(-look.normalized);
        var wood = Build.Mat(new Color(0.5f, 0.33f, 0.19f), 0.15f);
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-1.1f, 1f, 0), new Vector3(0.15f, 2f, 0.15f), wood, "Post");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(1.1f, 1f, 0), new Vector3(0.15f, 2f, 0.15f), wood, "Post");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 1.55f, 0), new Vector3(2.4f, 1.2f, 0.1f), wood, "Board");
        var paper = Build.Mat(new Color(0.96f, 0.92f, 0.8f), 0.1f);
        for (int i = 0; i < 3; i++) Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-0.7f + i * 0.7f, 1.55f, -0.07f), new Vector3(0.5f, 0.7f, 0.02f), paper, "Note");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 2.25f, 0), new Vector3(2.7f, 0.12f, 0.4f), Build.Mat(new Color(0.93f, 0.42f, 0.08f), 0.2f), "Roof");
        CozyFx.Label(root.transform, new Vector3(0, 2.8f, 0), "Daily Quests", 2.6f, Color.white);
        CozyFx.MakeInteractable(root, new Vector3(0, 1.2f, 0), new Vector3(2.6f, 2.4f, 0.6f), () => Loc.T("[E] Daily quests board"), Open);
        Board = root;
    }

    public void Open() { if (Panel == null) return; Panel.Open(); Refresh(); }

    private void Refresh()
    {
        if (Panel == null || !Panel.IsOpen) return;
        var lines = new List<string>();
        for (int i = 0; i < state.quests.Count; i++)
        {
            var q = state.quests[i];
            string status = q.done ? Loc.Raw("done") : Mathf.FloorToInt(q.progress) + "/" + q.target;
            lines.Add((i + 1) + ")  " + Title(q) + "   [" + status + "]   +" + q.reward + " " + Loc.Raw("SAR"));
            if (!q.done && q.type == "deliver" && EconomyManager.Instance != null)
                lines.Add("      " + Loc.Raw("In the farm store") + ": " + EconomyManager.Instance.Stored(q.good).ToString("F0") + "  -  " + Loc.Raw("press the number to hand in"));
        }
        Panel.Show(Loc.Raw("Daily Quests") + " - " + Loc.Raw("Day") + " " + state.day, lines, Loc.Raw("New quests every morning   [1-3] hand in   [E/Esc] close"));
    }
}
