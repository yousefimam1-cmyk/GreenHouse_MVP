using System;
using UnityEngine;

/// <summary>
/// Thin adapter between FarmCore and the rest of the game (missions, contracts, achievements, farm life, tutorial, academy,
/// collections, sponsors, co-op): the few field events those systems listened to on the old plots, now raised by FarmCore.
/// - Planted: a field got its first sown cells of a crop for this cycle (missions "plantField", "plant_&lt;crop&gt;").
/// - Harvested: kg of a crop cut on a field (one event per harvest pass, see FarmFields.FlushHarvests; missions "harvestField").
/// - Worked: cells changed by a tool. Tractor work also raises GameEvents.TractorWorked once per work unit (36 m2, the size of
///   an old plot), so "plow a field", "mechanised farm" and field contracts keep counting the same way.
/// </summary>
public static class FieldEvents
{
    public static event Action<FarmField, string> Planted;
    public static event Action<FarmField, string, float> Harvested;
    public static event Action<FarmField, FieldTool, int> Worked;

    /// <summary>Area of one tractor work unit (m2): one old 6 x 6 m plot.</summary>
    public const float WorkUnitM2 = 36f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Planted = null; Harvested = null; Worked = null; }

    public static void RaisePlanted(FarmField f, string crop) { Safe(() => Planted?.Invoke(f, crop)); GameEvents.RaiseCustom("plant_" + crop, 1f); }
    public static void RaiseHarvested(FarmField f, string crop, float kg) { Safe(() => Harvested?.Invoke(f, crop, kg)); if (!string.IsNullOrEmpty(crop)) GameEvents.RaiseCustom("harvest_" + crop, kg); }
    public static void RaiseWorked(FarmField f, FieldTool t, int cells) => Safe(() => Worked?.Invoke(f, t, cells));

    private static void Safe(Action a)
    {
        try { a(); } catch (Exception e) { Debug.LogException(e); }
    }
}
