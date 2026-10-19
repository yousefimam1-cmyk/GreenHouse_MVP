using System;
using UnityEngine;

public enum CropAttributeType
{
    Water, Nutrients, Energy, Climate, Health, Yield
}

[Serializable]
public class CropbedData
{
    public CropState state;
    public float water;
    public float nutrients;
    public float energy;
    public float climate;
    public float health;
    public float yieldProjection;

    public CropbedData Clone() => (CropbedData)MemberwiseClone();
}

public enum CropState
{
    Active, Dead
}

public class Cropbed : Interactable
{
    [Tooltip("Starting values of this bed (overridden by crop_profiles.json when a matching key exists)")]
    public CropbedData data;
    public Material[] cropbedMats;
    public GameObject[] foliage;
    public MeshRenderer meshRenderer;

    [Tooltip("Crop id from crop_profiles.json, used when the file has no entry for this bed")]
    [SerializeField] private string defaultCropId = "";

    private GreenhouseManager greenHouseManager;
    private CropbedData baseline;       // the bed's own values, before modules are added
    private Vector3[] foliageScales;

    public CropProfile Crop { get; private set; }
    public string ProfileKey => (greenHouseManager != null ? greenHouseManager.name : "") + "/" + name;
    /// <summary>0..1 growth of the current planting. Only healthy beds grow.</summary>
    public float Growth { get; private set; }
    public bool IsActive => data.state == CropState.Active;
    public bool IsReadyToHarvest => IsInitialized && IsActive && Growth >= 1f;

    public event Action<Cropbed> OnStateChanged;

    // Feature hooks (e.g. greenhouse pests): yield multipliers, extra prompt text (translated, unshaped), E handlers.
    public static readonly System.Collections.Generic.List<Func<Cropbed, float>> YieldModifiers = new System.Collections.Generic.List<Func<Cropbed, float>>();
    public static readonly System.Collections.Generic.List<Func<Cropbed, string>> PromptExtras = new System.Collections.Generic.List<Func<Cropbed, string>>();
    public static readonly System.Collections.Generic.List<Func<Cropbed, bool>> InteractHandlers = new System.Collections.Generic.List<Func<Cropbed, bool>>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHooks() { YieldModifiers.Clear(); PromptExtras.Clear(); InteractHandlers.Clear(); }

    protected override void Awake()
    {
        base.Awake();
        greenHouseManager = GetComponentInParent<GreenhouseManager>();
        LoadProfile();
    }

    private void LoadProfile()
    {
        baseline = data.Clone();
        var config = GreenHouseHelper.configurationData;
        bool useFile = config == null || config.useCropProfilesFile;
        CropbedProfile bed = useFile ? CropDatabase.GetBed(ProfileKey) : null;
        if (bed != null)
        {
            baseline.water = bed.water;
            baseline.nutrients = bed.nutrients;
            baseline.energy = bed.energy;
            baseline.climate = bed.climate;
            baseline.health = bed.health;
            baseline.yieldProjection = bed.yieldProjection;
        }
        Crop = CropDatabase.GetCrop(bed != null ? bed.crop : defaultCropId);
    }

    protected override void Initialize()
    {
        base.Initialize();
        foliageScales = new Vector3[foliage.Length];
        for (int i = 0; i < foliage.Length; i++)
        {
            Transform plants = GetPlants(i);
            if (plants != null) foliageScales[i] = plants.localScale;
        }
        CalculateData();
    }

    private Transform GetPlants(int i)
        => foliage[i] != null && foliage[i].transform.childCount > 0 ? foliage[i].transform.GetChild(0) : null;

    private Transform GetDeadPlants(int i)
        => foliage[i] != null && foliage[i].transform.childCount > 1 ? foliage[i].transform.GetChild(1) : null;

    public override string InteractionPrompt
    {
        get
        {
            string key = IsReadyToHarvest ? "[E] Harvest" : PromptKey;
            string text = Loc.Raw(key);
            if (Crop != null) text += " - " + Loc.CropName(Crop);
            foreach (var f in PromptExtras) { string x = f(this); if (!string.IsNullOrEmpty(x)) text += " | " + x; }
            return Loc.Display(text); // shape the whole line once so RTL order is correct
        }
    }

    /// <summary>Human-readable status line (localized + shaped).</summary>
    public string StatusText()
    {
        if (!IsActive) return Loc.T("Dead - optimize modules");
        if (Growth >= 1f) return Loc.T("Ready to harvest");
        return Loc.T("Growing {0}%", Mathf.FloorToInt(Growth * 100f));
    }

    public override void Interact()
    {
        foreach (var h in InteractHandlers) if (h(this)) return;
        if (IsReadyToHarvest)
        {
            Harvest(); // plays its own harvest sound
            return;
        }
        base.Interact();
        if (UIManager.Instance != null) UIManager.Instance.ShowCropbedUI(this);
    }

    private void Harvest()
    {
        var config = GreenHouseHelper.Config;
        float factor = Crop != null && Crop.yieldFactor > 0f ? Crop.yieldFactor : 1f;
        float kg = Mathf.Max(0f, data.yieldProjection) * config.harvestKgPerYieldPoint * factor;
        foreach (var f in YieldModifiers) kg *= Mathf.Max(0f, f(this));
        Growth = 0f;
        ApplyGrowthVisual();
        if (GameManager.Instance != null) GameManager.Instance.RegisterHarvest(this, kg);
    }

    public void ResetGrowth()
    {
        Growth = 0f;
        ApplyGrowthVisual();
    }

    private void Update()
    {
        if (!IsInitialized || !IsActive || Growth >= 1f) return;
        float seconds = Mathf.Max(1f, GreenHouseHelper.Config.growthSeconds / EconomyManager.GrowthSpeed);
        Growth = Mathf.Min(1f, Growth + Time.deltaTime / seconds * UtilityHooks.GreenhouseGrowth); // power/water supply (Utilities)
        ApplyGrowthVisual();
    }

    private void ApplyGrowthVisual()
    {
        if (foliageScales == null) return;
        float s = Mathf.Lerp(GreenHouseHelper.Config.seedlingScale, 1f, Growth);
        for (int i = 0; i < foliage.Length; i++)
        {
            Transform plants = GetPlants(i);
            if (plants != null) plants.localScale = foliageScales[i] * s;
        }
    }

    /// <summary>
    /// Effective value = the bed's own value + what the greenhouse modules provide.
    /// (Original code added each module's full value again every time it changed, so values kept
    /// growing; recomputing from the baseline makes the result the same no matter how often it runs.)
    /// </summary>
    public void CalculateData()
    {
        if (greenHouseManager == null) return;
        GreenhouseState totals = greenHouseManager.ComputeTotals();

        data.water = Combine(baseline.water, totals.water, CropAttributeType.Water);
        data.nutrients = Combine(baseline.nutrients, totals.nutrients, CropAttributeType.Nutrients);
        data.energy = Combine(baseline.energy, totals.energy, CropAttributeType.Energy);
        data.climate = Combine(baseline.climate, totals.climate, CropAttributeType.Climate);
        data.health = Combine(baseline.health, totals.health, CropAttributeType.Health);
        data.yieldProjection = Combine(baseline.yieldProjection, totals.yieldProjection, CropAttributeType.Yield);

        CropState previous = data.state;
        data.state = GreenHouseHelper.IsCropbedActive(data) ? CropState.Active : CropState.Dead;
        if (data.state == CropState.Dead) Growth = 0f;
        RefreshState();
        if (previous != data.state)
        {
            OnStateChanged?.Invoke(this);
            GameEvents.RaiseBedStateChanged(this);
        }
    }

    // Already-satisfied attributes keep their own value (same rule as the original code).
    private static float Combine(float own, float fromModules, CropAttributeType type)
        => GreenHouseHelper.IsValueSatisfied(own, type) ? own : own + fromModules;

    private void RefreshState()
    {
        bool active = data.state == CropState.Active;
        for (int i = 0; i < foliage.Length; i++)
        {
            Transform plants = GetPlants(i), dead = GetDeadPlants(i);
            if (plants != null) plants.gameObject.SetActive(active);
            if (dead != null) dead.gameObject.SetActive(!active);
        }
        if (meshRenderer != null && cropbedMats != null && cropbedMats.Length >= 2)
            meshRenderer.sharedMaterial = active ? cropbedMats[0] : cropbedMats[1];
        ApplyGrowthVisual();
    }
}
