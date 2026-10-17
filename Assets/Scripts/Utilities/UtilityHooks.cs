using UnityEngine;

/// <summary>
/// Small static hooks that existing gameplay code reads (set every in-game hour by UtilityGrid).
/// Defaults keep the original behaviour when the utilities system is absent.
/// - Cropbed growth is multiplied by GreenhouseGrowth (power for cooling pads + water per bed).
/// - FieldPlot dries FieldDryFactor times as fast, and is kept wet while FieldsAutoWatered (drip / pivot / sprinklers).
/// </summary>
public static class UtilityHooks
{
    /// <summary>0..1 multiplier on greenhouse bed growth.</summary>
    public static float GreenhouseGrowth = 1f;
    /// <summary>Multiplier on how fast field plots dry out (1 = normal).</summary>
    public static float FieldDryFactor = 1f;
    /// <summary>True while an irrigation system keeps every field plot watered.</summary>
    public static bool FieldsAutoWatered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetHooks() { GreenhouseGrowth = 1f; FieldDryFactor = 1f; FieldsAutoWatered = false; }
}
