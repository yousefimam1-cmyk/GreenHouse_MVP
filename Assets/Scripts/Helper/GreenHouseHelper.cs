using UnityEngine;

public static class GreenHouseHelper
{
    public static ConfigurationData configurationData { get; private set; }

    public static void Initialize(ConfigurationData configData)
    {
        if (configData != null) configurationData = configData;
    }

    /// <summary>Use whichever configuration is available first (UIManager or a GreenhouseManager).</summary>
    public static ConfigurationData Config
    {
        get
        {
            if (configurationData == null)
            {
                Debug.LogWarning("[GreenHouseHelper] No ConfigurationData assigned - using defaults.");
                configurationData = ScriptableObject.CreateInstance<ConfigurationData>();
            }
            return configurationData;
        }
    }

    public static float Threshold(CropAttributeType attributeType)
    {
        var c = Config;
        switch (attributeType)
        {
            case CropAttributeType.Water: return c.activeWaterRange;
            case CropAttributeType.Nutrients: return c.activeNutrientsRange;
            case CropAttributeType.Energy: return c.activeEnergyRange;
            case CropAttributeType.Climate: return c.activeClimateRange;
            case CropAttributeType.Health: return c.activeHealthRange;
            case CropAttributeType.Yield: return c.activeYieldProjectionRange;
        }
        return float.MaxValue;
    }

    // Small tolerance so 9.999 (float rounding when splitting health/yield across modules) still counts.
    public static bool IsValueSatisfied(float value, CropAttributeType attributeType)
        => value >= Threshold(attributeType) - 0.001f;

    public static bool IsCropbedActive(CropbedData data)
    {
        return IsValueSatisfied(data.water, CropAttributeType.Water) &&
               IsValueSatisfied(data.nutrients, CropAttributeType.Nutrients) &&
               IsValueSatisfied(data.energy, CropAttributeType.Energy) &&
               IsValueSatisfied(data.climate, CropAttributeType.Climate) &&
               IsValueSatisfied(data.health, CropAttributeType.Health) &&
               IsValueSatisfied(data.yieldProjection, CropAttributeType.Yield);
    }

    public static float Get(CropbedData d, CropAttributeType t)
    {
        switch (t)
        {
            case CropAttributeType.Water: return d.water;
            case CropAttributeType.Nutrients: return d.nutrients;
            case CropAttributeType.Energy: return d.energy;
            case CropAttributeType.Climate: return d.climate;
            case CropAttributeType.Health: return d.health;
            case CropAttributeType.Yield: return d.yieldProjection;
        }
        return 0f;
    }
}
