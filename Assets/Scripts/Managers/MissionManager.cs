using System;
using UnityEngine;

[Serializable]
public class MissionDef
{
    public string id, type, titleEn, titleAr, descEn, descAr;
    public float target = 1f;
    public int reward;
}

[Serializable] public class MissionFile { public MissionDef[] missions; }

/// <summary>
/// Story mission chain from Resources/Data/missions.json. Listens to GameEvents only (no direct references),
/// pays rewards through the economy and saves the current mission.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance;
    private const string IndexKey = "mazraa.mission";

    public MissionDef[] Missions { get; private set; } = new MissionDef[0];
    public int Index { get; private set; }
    public float Progress { get; private set; }
    public MissionDef Current => Index < Missions.Length ? Missions[Index] : null;
    public bool AllDone => Index >= Missions.Length;

    private void Awake()
    {
        Instance = this;
        var json = Resources.Load<TextAsset>("Data/missions");
        try { if (json != null) Missions = JsonUtility.FromJson<MissionFile>(json.text)?.missions ?? Missions; }
        catch (Exception e) { Debug.LogError("[Missions] Invalid missions.json: " + e.Message); }
        Index = Mathf.Clamp(SaveData.GetInt(IndexKey, 0), 0, Missions.Length);
    }

    private void OnEnable()
    {
        GameEvents.WellStarted += OnWell;
        GameEvents.ModuleOptimized += OnOptimized;
        GameEvents.Harvested += OnHarvest;
        GameEvents.VehicleChanged += OnVehicle;
        GameEvents.CargoLoaded += OnLoaded;
        GameEvents.Delivered += OnDelivered;
        GameEvents.UpgradeBought += OnBought;
        GameEvents.ProductsCollected += OnCollected;
        FieldPlot.FieldPlanted += OnPlanted;
        GameEvents.AnimalBorn += OnBorn;
        GameEvents.PenCleaned += OnCleaned;
        FieldPlot.FieldHarvested += OnFieldHarvest;
    }

    private void OnDisable()
    {
        GameEvents.WellStarted -= OnWell;
        GameEvents.ModuleOptimized -= OnOptimized;
        GameEvents.Harvested -= OnHarvest;
        GameEvents.VehicleChanged -= OnVehicle;
        GameEvents.CargoLoaded -= OnLoaded;
        GameEvents.Delivered -= OnDelivered;
        GameEvents.UpgradeBought -= OnBought;
        GameEvents.ProductsCollected -= OnCollected;
        FieldPlot.FieldPlanted -= OnPlanted;
        GameEvents.AnimalBorn -= OnBorn;
        GameEvents.PenCleaned -= OnCleaned;
        FieldPlot.FieldHarvested -= OnFieldHarvest;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        if (Current != null) GameEvents.RaiseMissionStarted(Current);
        // The well isn't saved between sessions, so this mission re-checks the scene.
        InvokeRepeating(nameof(CheckState), 2f, 1f);
    }

    private bool Is(string type) => Current != null && string.Equals(Current.type, type, StringComparison.OrdinalIgnoreCase);
    private void Add(string type, float amount) { if (Is(type)) { Progress += amount; CheckDone(); } }

    private void OnWell(WellController w) => Add("startWell", 1f);
    private void OnOptimized(GreenhouseManager g, ModuleType t) => Add("optimize", 1f);
    private void OnHarvest(Cropbed b, float kg) => Add("harvestKg", kg);
    private void OnVehicle(bool entered) { if (entered) Add("enterVehicle", 1f); }
    private void OnLoaded(float kg) => Add("loadKg", kg);
    private void OnDelivered(float kg, int money) { Add("deliverKg", kg); Add("earnMoney", money); }
    private void OnCollected() => Add("collectProducts", 1f);
    private void OnPlanted(FieldPlot p) => Add("plantField", 1f);
    private void OnBorn(string id) => Add("animalBorn", 1f);
    private void OnCleaned() => Add("cleanPen", 1f);
    private void OnFieldHarvest(FieldPlot p, string crop, float kg) => Add("harvestField", kg);
    private void OnBought(string id)
    {
        if (id == "feed") Add("buyFeed", 1f);
        else if (id == "capacity" || id == "growth") Add("buyUpgrade", 1f);
        else Add("buyAnimal", 1f);
    }

    // State-based missions
    private void CheckState()
    {
        if (Current == null) return;
        var gm = GameManager.Instance;
        if (Is("startWell"))
        {
            foreach (var w in FindObjectsByType<WellController>(FindObjectsSortMode.None))
                if (w.IsRunning) { Progress = Current.target; CheckDone(); return; }
        }
        if (gm == null) return;
        if (Is("healthyGreenhouse"))
        {
            foreach (var gh in gm.Greenhouses)
                if (gh != null && gh.Cropbeds.Count > 0 && gh.HealthyCount == gh.Cropbeds.Count) { Progress = Current.target; CheckDone(); return; }
        }
        else if (Is("allHealthy"))
        {
            int total = 0, healthy = 0;
            foreach (var gh in gm.Greenhouses) if (gh != null) { total += gh.Cropbeds.Count; healthy += gh.HealthyCount; }
            if (total > 0 && healthy == total) { Progress = Current.target; CheckDone(); }
        }
    }

    private void CheckDone()
    {
        var m = Current;
        if (m == null || Progress < m.target - 0.001f) return;
        if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(m.reward);
        if (UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Mission complete") + ": " + Title(m) + "  +" + m.reward.ToString("N0") + " " + Loc.Raw("EGP")));
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        GameEvents.RaiseMissionCompleted(m);
        Index++;
        Progress = 0f;
        SaveData.SetInt(IndexKey, Index);
        SaveData.Save();
        if (Current != null) GameEvents.RaiseMissionStarted(Current);
    }

    public void ResetChain()
    {
        Index = 0; Progress = 0f;
        SaveData.SetInt(IndexKey, 0);
        if (Current != null) GameEvents.RaiseMissionStarted(Current);
    }

    public static string Title(MissionDef m) => m == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(m.titleAr) ? m.titleAr : m.titleEn);
    public static string Desc(MissionDef m) => m == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(m.descAr) ? m.descAr : m.descEn);
}
