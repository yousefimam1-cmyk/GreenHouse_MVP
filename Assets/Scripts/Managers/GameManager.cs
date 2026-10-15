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
        TotalHarvestKg = PlayerPrefs.GetFloat(KgKey, 0f);
        HarvestCount = PlayerPrefs.GetInt(CountKey, 0);
        hud = GetComponent<GameHUD>();
        if (hud == null) hud = gameObject.AddComponent<GameHUD>();
        if (GetComponent<ScenarioManager>() == null) gameObject.AddComponent<ScenarioManager>();
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
        PlayerPrefs.SetFloat(KgKey, TotalHarvestKg);
        PlayerPrefs.SetInt(CountKey, HarvestCount);
        PlayerPrefs.Save();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        string crop = bed != null && bed.Crop != null ? Loc.CropName(bed.Crop) : (bed != null ? bed.name : "");
        if (UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(Loc.T("Harvested {0} kg of {1}", kg.ToString("F0"), crop));
        OnHarvest?.Invoke(kg);
        OnStatsChanged?.Invoke();
    }

    /// <summary>Clear saved harvest totals (e.g. from a reset button or the inspector context menu).</summary>
    [ContextMenu("Reset saved progress")]
    public void ResetProgress()
    {
        TotalHarvestKg = 0f;
        HarvestCount = 0;
        PlayerPrefs.DeleteKey(KgKey);
        PlayerPrefs.DeleteKey(CountKey);
        PlayerPrefs.Save();
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
