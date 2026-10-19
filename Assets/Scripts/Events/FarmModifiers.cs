using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small static modifiers that random events (EventDirector) set and the existing farm code reads.
/// Neutral values (1 / false / empty) mean "no event". EventDirector recomputes them every frame.
/// Hooks: FarmCore FarmSim (FieldDry, FieldGrowth), FarmCore irrigation and hand watering (IrrigationBanned),
/// AnimalPen.Update (PenWaterUse), MarketManager.Price (Price).
/// </summary>
public static class FarmModifiers
{
    /// <summary>Field drying speed multiplier (extreme heat).</summary>
    public static float FieldDry = 1f;
    /// <summary>Pen water use multiplier (extreme heat).</summary>
    public static float PenWaterUse = 1f;
    /// <summary>No irrigation allowed today (water restriction day).</summary>
    public static bool IrrigationBanned;
    /// <summary>Growth multiplier for the crops in FrostCrops (frost night).</summary>
    public static float FrostGrowth = 1f;
    public static readonly HashSet<string> FrostCrops = new HashSet<string>();
    /// <summary>Market price multipliers by good id (Ramadan demand).</summary>
    public static readonly Dictionary<string, float> PriceMul = new Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        FieldDry = 1f; PenWaterUse = 1f; IrrigationBanned = false; FrostGrowth = 1f;
        FrostCrops.Clear(); PriceMul.Clear();
    }

    public static float FieldGrowth(string cropId) => cropId != null && FrostCrops.Contains(cropId) ? FrostGrowth : 1f;

    public static float Price(string goodId) => goodId != null && PriceMul.TryGetValue(goodId, out var m) ? m : 1f;

    public static string BanText() => FinText.T("Water restriction today - no irrigation until tomorrow");
}

/// <summary>Localized text for the workers / land / bank / events features (keys in Resources/Data/Loc/40_eventsfin.json).</summary>
public static class FinText
{
    public static string R(string english) => Loc.Raw(english);
    public static string T(string english) => Loc.T(english);
    public static string T(string english, params object[] args) => Loc.T(english, args);
    public static string Sar(int amount) => amount.ToString("N0") + " " + Loc.Raw("SAR");
    public static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
