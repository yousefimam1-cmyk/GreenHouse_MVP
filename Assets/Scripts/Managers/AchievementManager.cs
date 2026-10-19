using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local achievements (24), kept per PC across every farm and new game, like store-platform achievements.
/// Progress is counted from GameEvents; unlocking shows a gold toast. The menu has an Achievements page.
/// Stored in PlayerPrefs ("mazraa.ach.&lt;id&gt;" = progress), not in the save game.
/// </summary>
[MazraaSystem(37)]
public class AchievementManager : MonoBehaviour
{
    public class Def
    {
        public string id, title, desc;
        public float target;
        public Def(string id, string title, string desc, float target) { this.id = id; this.title = title; this.desc = desc; this.target = target; }
    }

    public static AchievementManager Instance { get; private set; }

    public static readonly Def[] All =
    {
        new Def("first_harvest", "First harvest", "Harvest your first crop", 1),
        new Def("harvest_1t", "Tonne of produce", "Harvest 1,000 kg of crops", 1000),
        new Def("harvest_50t", "Breadbasket", "Harvest 50,000 kg of crops", 50000),
        new Def("first_sale", "Open for business", "Sell your first load at the market", 1),
        new Def("deliveries_50", "Market regular", "Deliver 50 loads to the market", 50),
        new Def("money_10k", "Ten thousand", "Have 10,000 SAR in the bank", 10000),
        new Def("money_100k", "Prosperous farm", "Have 100,000 SAR in the bank", 100000),
        new Def("money_1m", "Millionaire farmer", "Have 1,000,000 SAR in the bank", 1000000),
        new Def("missions_10", "Reliable farmer", "Complete 10 missions", 10),
        new Def("missions_all", "Story complete", "Complete the whole campaign", 1),
        new Def("contracts_10", "Contractor", "Complete 10 contracts", 10),
        new Def("tractor_100", "Field worker", "Work 100 field passes with the tractor", 100),
        new Def("animals_born", "New life", "Raise a newborn animal", 1),
        new Def("products_50", "Fresh every day", "Collect animal products 50 times", 50),
        new Def("upgrades_5", "Modern farm", "Buy 5 farm upgrades", 5),
        new Def("well", "Water finder", "Start a well", 1),
        new Def("plant_trees", "Palm grove", "Plant 10 palm or fruit trees", 10),
        new Def("dates", "Date harvest", "Harvest dates from your palms", 1),
        new Def("fish_20", "Angler", "Catch 20 fish", 20),
        new Def("land", "Landowner", "Buy a new piece of land", 1),
        new Def("worker", "Employer", "Hire a worker", 1),
        new Def("loan_repaid", "Debt free", "Repay a loan", 1),
        new Def("dust_storm", "Storm survivor", "Get through a dust storm", 1),
        new Def("days_100", "Seasoned farmer", "Play 100 in-game days", 100),
    };

    public event Action<Def> Unlocked;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        GameEvents.Harvested += OnHarvested;
        GameEvents.Delivered += OnDelivered;
        GameEvents.MoneyChanged += OnMoney;
        GameEvents.MissionCompleted += OnMission;
        GameEvents.ContractCompleted += OnContract;
        GameEvents.TractorWorked += OnTractor;
        GameEvents.AnimalBorn += OnBorn;
        GameEvents.ProductsCollected += OnProducts;
        GameEvents.UpgradeBought += OnUpgrade;
        GameEvents.WellStarted += OnWell;
        GameEvents.Custom += OnCustom;
        WorldClock.DayStarted += OnDay;
    }

    private void OnDisable()
    {
        GameEvents.Harvested -= OnHarvested;
        GameEvents.Delivered -= OnDelivered;
        GameEvents.MoneyChanged -= OnMoney;
        GameEvents.MissionCompleted -= OnMission;
        GameEvents.ContractCompleted -= OnContract;
        GameEvents.TractorWorked -= OnTractor;
        GameEvents.AnimalBorn -= OnBorn;
        GameEvents.ProductsCollected -= OnProducts;
        GameEvents.UpgradeBought -= OnUpgrade;
        GameEvents.WellStarted -= OnWell;
        GameEvents.Custom -= OnCustom;
        WorldClock.DayStarted -= OnDay;
    }

    private void OnHarvested(Cropbed b, float kg) { Add("first_harvest", 1); Add("harvest_1t", kg); Add("harvest_50t", kg); }
    private void OnDelivered(float kg, int money) { if (money > 0) { Add("first_sale", 1); Add("deliveries_50", 1); } }
    private void OnMoney(int balance) { Max("money_10k", balance); Max("money_100k", balance); Max("money_1m", balance); }
    private void OnMission(MissionDef m)
    {
        Add("missions_10", 1);
        if (MissionManager.Instance != null && MissionManager.Instance.AllDone) Add("missions_all", 1);
    }
    private void OnContract() => Add("contracts_10", 1);
    private void OnTractor(Implement i) => Add("tractor_100", 1);
    private void OnBorn(string id) => Add("animals_born", 1);
    private void OnProducts() => Add("products_50", 1);
    private void OnUpgrade(string id) => Add("upgrades_5", 1);
    private void OnWell(WellController w) => Add("well", 1);
    private void OnDay(int day) => Max("days_100", day);

    private void OnCustom(string type, float amount)
    {
        switch (type)
        {
            case "harvestTankKg": Add("first_harvest", 1); Add("harvest_1t", amount); Add("harvest_50t", amount); break;
            case "cropsPlantTree": Add("plant_trees", 1); break;
            case "cropsDateHarvest": Add("dates", 1); break;
            case "catchFish": case "catchCoastFish": Add("fish_20", 1); break;
            case "buyLand": Add("land", 1); break;
            case "hireWorker": Add("worker", 1); break;
            case "repayLoan": Add("loan_repaid", 1); break;
            case "surviveDustStorm": Add("dust_storm", 1); break;
        }
    }

    // ------------------------------------------------------------------ progress

    private static string Key(string id) => "mazraa.ach." + id;
    public static Def Find(string id) { foreach (var d in All) if (d.id == id) return d; return null; }
    public static float Progress(string id) => PlayerPrefs.GetFloat(Key(id), 0f);
    public static bool IsUnlocked(string id) { var d = Find(id); return d != null && Progress(id) >= d.target; }
    public static int UnlockedCount { get { int n = 0; foreach (var d in All) if (Progress(d.id) >= d.target) n++; return n; } }

    /// <summary>Clear every achievement (tests / settings).</summary>
    public static void ResetAll() { foreach (var d in All) PlayerPrefs.DeleteKey(Key(d.id)); PlayerPrefs.Save(); }

    public void Add(string id, float amount) { var d = Find(id); if (d != null && amount > 0f) Set(d, Progress(id) + amount); }
    private void Max(string id, float value) { var d = Find(id); if (d != null && value > Progress(id)) Set(d, value); }

    private void Set(Def d, float value)
    {
        float before = Progress(d.id);
        if (before >= d.target) return;                 // already unlocked: keep it
        value = Mathf.Min(value, d.target);
        PlayerPrefs.SetFloat(Key(d.id), value);
        if (value >= d.target)
        {
            PlayerPrefs.Save();
            if (UIManager.Instance != null)
                UIManager.Instance.ShowAlertMessage("<color=#FAC240>" + Loc.T("Achievement unlocked") + ":</color> " + Loc.T(d.title));
            if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
            Unlocked?.Invoke(d);
        }
    }

    /// <summary>One menu line: tick or progress, title and description.</summary>
    public static string Line(Def d)
    {
        float p = Progress(d.id);
        bool done = p >= d.target;
        string state = done ? "<color=#9BE07A>" + Loc.T("Done") + "</color>"
            : d.target > 1f ? "<color=#FAC240>" + Mathf.FloorToInt(Mathf.Min(p, d.target)).ToString("N0") + " / " + d.target.ToString("N0") + "</color>"
            : "<color=#FFFFFF66>" + Loc.T("Locked") + "</color>";
        return (done ? "<b>" : "<color=#FFFFFFAA>") + Loc.T(d.title) + (done ? "</b>" : "</color>") + "  <size=80%>" + Loc.T(d.desc) + "</size>   " + state;
    }
}
