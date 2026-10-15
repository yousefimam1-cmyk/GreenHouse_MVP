using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Game-wide state: harvest totals (saved), language switching, help, and "all beds healthy" goal.
/// Created automatically when a scene loads, so nothing needs to be added to the scene by hand.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    private const string KgKey = "mazraa.harvestKg";
    private const string CountKey = "mazraa.harvestCount";

    public float TotalHarvestKg { get; private set; }
    public int HarvestCount { get; private set; }
    public IReadOnlyList<GreenhouseManager> Greenhouses => greenhouses;

    public event Action OnStatsChanged;
    /// <summary>kg harvested in one action.</summary>
    public event Action<float> OnHarvest;

    private readonly List<GreenhouseManager> greenhouses = new List<GreenhouseManager>();
    private bool allHealthyAnnounced;
    private GameHUD hud;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<GameManager>() != null) return;
        // Only start in scenes that actually contain the greenhouse game.
        if (FindAnyObjectByType<GreenhouseManager>() == null) return;
        new GameObject("GameManager (auto)").AddComponent<GameManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        TotalHarvestKg = SaveData.GetFloat(KgKey, 0f);
        HarvestCount = SaveData.GetInt(CountKey, 0);
        hud = GetComponent<GameHUD>();
        if (hud == null) hud = gameObject.AddComponent<GameHUD>();
        if (GetComponent<ScenarioManager>() == null) gameObject.AddComponent<ScenarioManager>();
        if (GetComponent<EconomyManager>() == null) gameObject.AddComponent<EconomyManager>();
        if (GetComponent<MissionManager>() == null) gameObject.AddComponent<MissionManager>();
        if (GetComponent<VehicleSpawner>() == null) gameObject.AddComponent<VehicleSpawner>();
        if (GetComponent<GraphicsEnhancer>() == null) gameObject.AddComponent<GraphicsEnhancer>();
        if (GetComponent<FeedbackFX>() == null) gameObject.AddComponent<FeedbackFX>();
        if (GetComponent<ShopUI>() == null) gameObject.AddComponent<ShopUI>();
        if (GetComponent<WorldClock>() == null) gameObject.AddComponent<WorldClock>();
        if (GetComponent<FieldManager>() == null) gameObject.AddComponent<FieldManager>();
        if (GetComponent<MarketManager>() == null) gameObject.AddComponent<MarketManager>();
        if (GetComponent<ObjectiveMarker>() == null) gameObject.AddComponent<ObjectiveMarker>();
        if (GetComponent<StoryIntro>() == null) gameObject.AddComponent<StoryIntro>();
    }

    private void Start()
    {
        greenhouses.AddRange(FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None));
        greenhouses.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        foreach (var gh in greenhouses)
            foreach (var bed in gh.Cropbeds)
                if (bed != null) bed.OnStateChanged += HandleBedStateChanged;

        Loc.LoadSaved();
    }

    private void OnDestroy()
    {
        foreach (var gh in greenhouses)
            if (gh != null)
                foreach (var bed in gh.Cropbeds)
                    if (bed != null) bed.OnStateChanged -= HandleBedStateChanged;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.lKey.wasPressedThisFrame) Loc.Toggle();
        if (kb.hKey.wasPressedThisFrame) hud.ToggleHelp();
    }

    public void RegisterHarvest(Cropbed bed, float kg)
    {
        TotalHarvestKg += kg;
        HarvestCount++;
        SaveData.SetFloat(KgKey, TotalHarvestKg);
        SaveData.SetInt(CountKey, HarvestCount);
        SaveData.Save();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        string crop = bed != null && bed.Crop != null ? Loc.CropName(bed.Crop) : (bed != null ? bed.name : "");
        if (UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(Loc.T("Harvested {0} kg of {1}", kg.ToString("F0"), crop));
        OnHarvest?.Invoke(kg);
        GameEvents.RaiseHarvested(bed, kg);
        OnStatsChanged?.Invoke();
    }

    /// <summary>Open-field harvest: counts toward totals and missions (the plot already stored the goods).</summary>
    public void RegisterFieldHarvest(float kg)
    {
        TotalHarvestKg += kg;
        HarvestCount++;
        SaveData.SetFloat(KgKey, TotalHarvestKg);
        SaveData.SetInt(CountKey, HarvestCount);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        OnHarvest?.Invoke(kg);
        GameEvents.RaiseHarvested(null, kg);
        OnStatsChanged?.Invoke();
    }

    /// <summary>Clear saved harvest totals (e.g. from a reset button or the inspector context menu).</summary>
    [ContextMenu("Reset saved progress")]
    public void ResetProgress()
    {
        TotalHarvestKg = 0f;
        HarvestCount = 0;
        SaveData.DeleteKey(KgKey);
        SaveData.DeleteKey(CountKey);
        SaveData.Save();
        OnStatsChanged?.Invoke();
    }

    private void HandleBedStateChanged(Cropbed bed)
    {
        OnStatsChanged?.Invoke();
        if (allHealthyAnnounced) return;
        int total = 0, healthy = 0;
        foreach (var gh in greenhouses) { total += gh.Cropbeds.Count; healthy += gh.HealthyCount; }
        if (total > 0 && healthy == total)
        {
            allHealthyAnnounced = true;
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("All crop beds are healthy!"));
        }
    }
}
