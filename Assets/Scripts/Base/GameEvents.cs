using System;
using UnityEngine;

/// <summary>
/// Central event bus (Observer pattern). Gameplay objects raise events here; systems such as
/// missions, economy, HUD and effects subscribe, so no system needs a direct reference to another.
/// </summary>
public static class GameEvents
{
    public static event Action<WellController> WellStarted;
    public static event Action<GreenhouseManager, ModuleType> ModuleOptimized;
    public static event Action<Cropbed> BedStateChanged;
    public static event Action<Cropbed, float> Harvested;          // bed, kg
    public static event Action<float> CargoLoaded;                 // kg
    public static event Action<float, int> Delivered;              // kg, money earned
    public static event Action<bool> VehicleChanged;               // true = entered
    public static event Action<string> UpgradeBought;              // upgrade id
    public static event Action<int> MoneyChanged;                  // new balance
    public static event Action<MissionDef> MissionStarted;
    public static event Action<MissionDef> MissionCompleted;
    public static event Action ProductsCollected;
    public static event Action<string> AnimalBorn;
    public static event Action PenCleaned;
    public static event Action<Implement> TractorWorked;
    public static event Action ContractCompleted;
    /// <summary>Generic event for features added later: mission type + amount (e.g. "catchFish", 1).</summary>
    public static event Action<string, float> Custom;
    /// <summary>Raised after save data is wiped for a new game; feature systems reset their state here.</summary>
    public static event Action NewGameStarted;
    /// <summary>A career was started on a map (Regions.Select): region content (crops, story, events, look) switches here.</summary>
    public static event Action<string> RegionChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        WellStarted = null; ModuleOptimized = null; BedStateChanged = null; Harvested = null;
        CargoLoaded = null; Delivered = null; VehicleChanged = null; UpgradeBought = null;
        MoneyChanged = null; MissionStarted = null; MissionCompleted = null; ProductsCollected = null; AnimalBorn = null; PenCleaned = null; TractorWorked = null; ContractCompleted = null; Custom = null; NewGameStarted = null; RegionChanged = null;
    }

    public static void RaiseWellStarted(WellController w) => WellStarted?.Invoke(w);
    public static void RaiseModuleOptimized(GreenhouseManager g, ModuleType t) => ModuleOptimized?.Invoke(g, t);
    public static void RaiseBedStateChanged(Cropbed b) => BedStateChanged?.Invoke(b);
    public static void RaiseHarvested(Cropbed b, float kg) => Harvested?.Invoke(b, kg);
    public static void RaiseCargoLoaded(float kg) => CargoLoaded?.Invoke(kg);
    public static void RaiseDelivered(float kg, int money) => Delivered?.Invoke(kg, money);
    public static void RaiseVehicleChanged(bool entered) => VehicleChanged?.Invoke(entered);
    public static void RaiseUpgradeBought(string id) => UpgradeBought?.Invoke(id);
    public static void RaiseMoneyChanged(int balance) => MoneyChanged?.Invoke(balance);
    public static void RaiseMissionStarted(MissionDef m) => MissionStarted?.Invoke(m);
    public static void RaiseMissionCompleted(MissionDef m) => MissionCompleted?.Invoke(m);
    public static void RaiseProductsCollected() => ProductsCollected?.Invoke();
    public static void RaiseAnimalBorn(string id) => AnimalBorn?.Invoke(id);
    public static void RaisePenCleaned() => PenCleaned?.Invoke();
    public static void RaiseTractorWorked(Implement i) => TractorWorked?.Invoke(i);
    public static void RaiseContractCompleted() => ContractCompleted?.Invoke();
    public static void RaiseCustom(string type, float amount = 1f) => Custom?.Invoke(type, amount);
    public static void RaiseNewGameStarted() => NewGameStarted?.Invoke();
    public static void RaiseRegionChanged(string regionId) => RegionChanged?.Invoke(regionId);
}
