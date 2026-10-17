using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Glue for map content packs (Regions, regions.json): names the market pad and the farm shop after the map
/// ("Village souq" and "Agricultural cooperative" on the Nile Delta) and turns field work into named story events
/// (plant_rice, harvest_cotton ... through GameEvents.Custom) so the Egypt missions and quests can ask for one crop.
/// The rest of a pack is read by the systems themselves: crops (EconomyManager), missions (MissionManager),
/// campaign (CampaignManager), events and the canal turn (EventDirector), quests (DailyQuests), buyers and sell
/// points (MarketManager, DesertDressing), look (DesertDressing, DesertWeatherFX, GraphicsEnhancer).
/// </summary>
[MazraaSystem(37)]
public class RegionContent : MonoBehaviour
{
    public static RegionContent Instance { get; private set; }
    public const string MarketKey = "Market", ShopKey = "Farm Shop";

    // label -> the key it was built with, so the names go back to the base ones on a Najd career
    private readonly Dictionary<LocalizedLabel, string> original = new Dictionary<LocalizedLabel, string>();

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        FieldPlot.FieldPlanted += OnPlanted;
        FieldPlot.FieldHarvested += OnHarvested;
        GameEvents.RegionChanged += OnRegion;
    }

    private void OnDisable()
    {
        FieldPlot.FieldPlanted -= OnPlanted;
        FieldPlot.FieldHarvested -= OnHarvested;
        GameEvents.RegionChanged -= OnRegion;
    }

    private void Start() => Invoke(nameof(ApplyNames), 0.5f);   // after VehicleSpawner has built the market and the stalls

    private void OnRegion(string id) => ApplyNames();

    private static void OnPlanted(FieldPlot p) { if (p != null && !string.IsNullOrEmpty(p.CropId)) GameEvents.RaiseCustom("plant_" + p.CropId, 1f); }
    private static void OnHarvested(FieldPlot p, string crop, float kg) { if (!string.IsNullOrEmpty(crop)) GameEvents.RaiseCustom("harvest_" + crop, kg); }

    /// <summary>English Loc key of the market pad on the current map.</summary>
    public static string MarketName => Regions.Current != null && !string.IsNullOrEmpty(Regions.Current.marketName) ? Regions.Current.marketName : MarketKey;
    /// <summary>English Loc key of the farm shop stall on the current map.</summary>
    public static string ShopName => Regions.Current != null && !string.IsNullOrEmpty(Regions.Current.shopName) ? Regions.Current.shopName : ShopKey;

    /// <summary>Rename the market and shop signs for the current map.</summary>
    public void ApplyNames()
    {
        foreach (var l in FindObjectsByType<LocalizedLabel>(FindObjectsSortMode.None))
        {
            if (l == null) continue;
            string key;
            if (!original.TryGetValue(l, out key)) { key = l.key; original[l] = key; }
            if (key == MarketKey) l.SetKey(MarketName);
            else if (key == ShopKey) l.SetKey(ShopName);
        }
    }
}
