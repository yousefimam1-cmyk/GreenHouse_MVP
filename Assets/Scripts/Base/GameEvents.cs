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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        WellStarted = null; ModuleOptimized = null; BedStateChanged = null; Harvested = null;
        CargoLoaded = null; Delivered = null; VehicleChanged = null; UpgradeBought = null;
        MoneyChanged = null; MissionStarted = null; MissionCompleted = null; ProductsCollected = null;
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
}
