using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GreenhouseState
{
    public float water;
    public float nutrients;
    public float energy;
    public float climate;
    public float health;
    public float yieldProjection;
}

public class GreenhouseManager : MonoBehaviour
{
    public ConfigurationData configurationData;
    public GreenhouseState currentState;
    public Module[] modules;
    public bool randomizeModules = true;

    [Tooltip("Name shown in the HUD (defaults to the GameObject name)")]
    [SerializeField] private string displayName = "";

    private Cropbed[] cropbeds = new Cropbed[0];

    public bool HasCustomName => !string.IsNullOrEmpty(displayName);
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public IReadOnlyList<Cropbed> Cropbeds => cropbeds;
    public int HealthyCount
    {
        get { int n = 0; foreach (var c in cropbeds) if (c != null && c.IsActive) n++; return n; }
    }

    private static readonly ModuleType[] RequiredModules =
        { ModuleType.Irrigation, ModuleType.Nutrient, ModuleType.Energy, ModuleType.ClimateControl };

    private void Awake()
    {
        GreenHouseHelper.Initialize(configurationData);
        if (configurationData == null) configurationData = GreenHouseHelper.Config;
        PrepareModules();
        GatherCropbeds();
    }

    private void Start()
    {
        if (randomizeModules)
            RandomizeModules();
        else
            OptimizeAllModules();
        RefreshCurrentState();
    }

    /// <summary>
    /// Work on runtime copies of the module assets. The original code wrote random values straight into
    /// the .asset files, so every Play session in the editor permanently changed the project data,
    /// and two greenhouses sharing an asset would overwrite each other. Missing modules are created
    /// so the greenhouse still works if an asset reference is broken.
    /// </summary>
    private void PrepareModules()
    {
        var list = new List<Module>();
        if (modules != null)
            foreach (var m in modules)
                if (m != null) list.Add(Instantiate(m));

        foreach (var type in RequiredModules)
        {
            if (list.Exists(m => m.moduleType == type)) continue;
            var created = ScriptableObject.CreateInstance<Module>();
            created.moduleType = type;
            created.name = type + " (auto)";
            list.Add(created);
            Debug.LogWarning($"[{name}] No {type} module assigned - created a default one.");
        }
        modules = list.ToArray();
    }

    private void RandomizeModules()
    {
        var c = configurationData;
        foreach (var module in modules)
        {
            switch (module.moduleType)
            {
                case ModuleType.Irrigation: module.water = Rand(c.initWaterRandomization); break;
                case ModuleType.Nutrient: module.nutrient = Rand(c.initNutrientRandomization); break;
                case ModuleType.Energy: module.energy = Rand(c.initEnergyRandomization); break;
                case ModuleType.ClimateControl: module.climate = Rand(c.initClimateRandomization); break;
            }
            module.health = Rand(c.initHealthRandomization);
            module.yieldProjection = Rand(c.initYieldRandomization);
        }
    }

    private static float Rand(Vector2 range) => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));

    private void OptimizeAllModules()
    {
        foreach (var type in RequiredModules) SetOptimized(type);
        SetOptimized(ModuleType.Health);
        SetOptimized(ModuleType.YieldProjection);
    }

    public GreenhouseState ComputeTotals()
    {
        var s = new GreenhouseState();
        foreach (var module in modules)
        {
            if (module == null) continue;
            s.water += module.water;
            s.nutrients += module.nutrient;
            s.energy += module.energy;
            s.climate += module.climate;
            s.health += module.health;
            s.yieldProjection += module.yieldProjection;
        }
        return s;
    }

    public void RefreshCurrentState() => currentState = ComputeTotals();

    public bool IsModuleOptimized(ModuleType moduleType)
    {
        if (currentState == null) RefreshCurrentState();
        CropAttributeType attr = ToAttribute(moduleType);
        return GreenHouseHelper.IsValueSatisfied(GetModuleValue(moduleType), attr);
    }

    /// <summary>Raised after the player optimizes a module (used by training scenarios).</summary>
    public event System.Action<GreenhouseManager, ModuleType> OnModuleOptimized;

    /// <summary>Re-roll (or fully optimize) all modules and recalculate the beds. Used by training scenarios.</summary>
    public void ResetModules(bool randomize)
    {
        if (randomize) RandomizeModules(); else OptimizeAllModules();
        RefreshCurrentState();
        foreach (var bed in cropbeds) if (bed != null) bed.ResetGrowth();
        CalculateCropbeds();
    }

    /// <summary>Simulate a failure: the module's main value drops to 0.</summary>
    public void BreakModule(ModuleType moduleType)
    {
        Module m = GetModule(moduleType);
        if (m == null) return;
        switch (moduleType)
        {
            case ModuleType.Irrigation: m.water = 0f; break;
            case ModuleType.Nutrient: m.nutrient = 0f; break;
            case ModuleType.Energy: m.energy = 0f; break;
            case ModuleType.ClimateControl: m.climate = 0f; break;
        }
        RefreshCurrentState();
        CalculateCropbeds();
    }

    public void OptimizeModule(ModuleType moduleType, bool invokeAction = true)
    {
        SetOptimized(moduleType);
        if (invokeAction)
        {
            OnModuleOptimized?.Invoke(this, moduleType);
            GameEvents.RaiseModuleOptimized(this, moduleType);
        }
        // Totals must be refreshed BEFORE the beds recalculate (the original order was reversed).
        RefreshCurrentState();
        if (invokeAction)
            CalculateCropbeds();
    }

    private void SetOptimized(ModuleType moduleType)
    {
        var c = configurationData;
        Module m;
        switch (moduleType)
        {
            case ModuleType.Irrigation: if ((m = GetModule(moduleType)) != null) m.water = c.activeWaterRange; break;
            case ModuleType.Nutrient: if ((m = GetModule(moduleType)) != null) m.nutrient = c.activeNutrientsRange; break;
            case ModuleType.Energy: if ((m = GetModule(moduleType)) != null) m.energy = c.activeEnergyRange; break;
            case ModuleType.ClimateControl: if ((m = GetModule(moduleType)) != null) m.climate = c.activeClimateRange; break;
            case ModuleType.Health: SetUniversalAttribute(CropAttributeType.Health); break;
            case ModuleType.YieldProjection: SetUniversalAttribute(CropAttributeType.Yield); break;
        }
    }

    public float GetModuleValue(ModuleType moduleType)
    {
        if (currentState == null) RefreshCurrentState();
        switch (moduleType)
        {
            case ModuleType.Irrigation: return currentState.water;
            case ModuleType.Nutrient: return currentState.nutrients;
            case ModuleType.Energy: return currentState.energy;
            case ModuleType.ClimateControl: return currentState.climate;
            case ModuleType.Health: return currentState.health;
            case ModuleType.YieldProjection: return currentState.yieldProjection;
        }
        return 0f;
    }

    public static CropAttributeType ToAttribute(ModuleType t)
    {
        switch (t)
        {
            case ModuleType.Irrigation: return CropAttributeType.Water;
            case ModuleType.Nutrient: return CropAttributeType.Nutrients;
            case ModuleType.Energy: return CropAttributeType.Energy;
            case ModuleType.ClimateControl: return CropAttributeType.Climate;
            case ModuleType.Health: return CropAttributeType.Health;
            default: return CropAttributeType.Yield;
        }
    }

    private Module GetModule(ModuleType moduleType)
    {
        foreach (var module in modules)
            if (module != null && module.moduleType == moduleType)
                return module;
        return null;
    }

    // Health and yield have no module of their own: split the target evenly across all modules.
    private void SetUniversalAttribute(CropAttributeType attributeType)
    {
        if (modules.Length == 0) return;
        float target = GreenHouseHelper.Threshold(attributeType);
        float share = target / modules.Length;
        foreach (var module in modules)
        {
            if (attributeType == CropAttributeType.Health) module.health = share;
            else module.yieldProjection = share;
        }
    }

    private void GatherCropbeds()
    {
        cropbeds = GetComponentsInChildren<Cropbed>(true);
    }

    private void CalculateCropbeds()
    {
        foreach (var cropbed in cropbeds)
            if (cropbed != null) cropbed.CalculateData();
    }
}
