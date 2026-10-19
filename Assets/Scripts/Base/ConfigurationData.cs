using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Scriptables/Configuration")]
public class ConfigurationData : ScriptableObject
{
    // Field names fixed from "acitve..." - FormerlySerializedAs keeps the values already saved in Configuration.asset.
    [Tooltip("Water value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveWaterRange")] public float activeWaterRange = 10;
    [Tooltip("Nutrients value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveNutrientsRange")] public float activeNutrientsRange = 15;
    [Tooltip("Energy value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveEnergyRange")] public float activeEnergyRange = 12;
    [Tooltip("Climate value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveClimateRange")] public float activeClimateRange = 9;
    [Tooltip("Health value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveHealthRange")] public float activeHealthRange = 10;
    [Tooltip("Yield Projection value at which this crop value will be considered satisfied")]
    [FormerlySerializedAs("acitveYieldProjectionRange")] public float activeYieldProjectionRange = 5;

    [Space]
    public Vector2 initWaterRandomization = new Vector2(3, 5);
    public Vector2 initNutrientRandomization = new Vector2(5, 7);
    public Vector2 initEnergyRandomization = new Vector2(3, 6);
    public Vector2 initClimateRandomization = new Vector2(2, 5);
    public Vector2 initHealthRandomization = new Vector2(0.5f, 2);
    public Vector2 initYieldRandomization = new Vector2(0.5f, 1.2f);

    [Header("Growth & Harvest")]
    [Tooltip("Seconds a healthy bed needs to grow from planted to ready-to-harvest")]
    public float growthSeconds = 45f;
    [Tooltip("Harvested kg = bed yield projection x this x crop yield factor")]
    public float harvestKgPerYieldPoint = 12f;
    [Tooltip("Plant size at 0% growth relative to full size")]
    [Range(0.05f, 1f)] public float seedlingScale = 0.35f;

    [Header("Data")]
    [Tooltip("Load crop types and bed starting values from Resources/Data/crop_profiles.json")]
    public bool useCropProfilesFile = true;
}
