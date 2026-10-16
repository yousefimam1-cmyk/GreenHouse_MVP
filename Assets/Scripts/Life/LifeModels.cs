using System;
using System.Collections.Generic;
using UnityEngine;

// Pure game rules for on-foot life and animal husbandry (no scene objects), so they can be tested headlessly.

/// <summary>
/// Player stamina (0..100) and hydration (0..1). Sprinting, jumping and manual work drain stamina, more in the heat
/// and while carrying; walking slowly, standing and crouching (resting) bring it back. Hydration drops over the day
/// (much faster in a summer heatwave) and caps stamina; drinking at the well or a water trough refills it.
/// Running dry makes you exhausted: no sprinting until stamina is back above 25.
/// </summary>
[Serializable]
public class StaminaModel
{
    public const float Max = 100f;
    public const float SprintDrain = 11f;      // per second at heat 0, nothing carried
    public const float RecoverIdle = 9f, RecoverWalk = 4f, RecoverRest = 16f;
    public const float ExhaustedUntil = 25f;

    public float stamina = Max;
    public float hydration = 1f;
    public bool exhausted;

    /// <summary>Stamina cannot go above this while thirsty (40 when completely dry).</summary>
    public float Cap => 40f + 60f * Mathf.Clamp01(hydration);
    public float Stamina01 => stamina / Max;

    /// <summary>Advance by dt seconds. heat01: 0 cool .. 1 heatwave noon. dayFraction: dt as a fraction of an in-game day.</summary>
    public void Tick(float dt, bool moving, bool sprinting, bool crouching, float heat01, float carryKg, float dayFraction)
    {
        heat01 = Mathf.Clamp01(heat01);
        float load = 1f + Mathf.Clamp01(carryKg / 40f);
        if (sprinting && moving)
            stamina -= dt * SprintDrain * (1f + 1.2f * heat01) * load;
        else
        {
            float rate = !moving ? (crouching ? RecoverRest : RecoverIdle) : (crouching ? RecoverWalk * 1.5f : RecoverWalk);
            if (carryKg > 0f) rate *= 0.5f;
            stamina += dt * rate * (1f - 0.45f * heat01);
        }
        // Thirst: about 60% of the water per day when mild, up to 160% in a heatwave (more when running).
        hydration -= dayFraction * (0.6f + 1.0f * heat01) * (sprinting && moving ? 1.6f : 1f);
        hydration = Mathf.Clamp01(hydration);
        Clamp();
    }

    /// <summary>Spend stamina for work (tools, jump). Returns false (and spends nothing) when too tired.</summary>
    public bool Spend(float amount, float heat01)
    {
        float cost = amount * (1f + 0.5f * Mathf.Clamp01(heat01));
        if (stamina < cost) return false;
        stamina -= cost;
        Clamp();
        return true;
    }

    public void Drink() { hydration = 1f; stamina = Mathf.Min(Cap, stamina + 25f); Clamp(); }

    public void Reset() { stamina = Max; hydration = 1f; exhausted = false; }

    private void Clamp()
    {
        stamina = Mathf.Clamp(stamina, 0f, Cap);
        if (stamina <= 0.01f) exhausted = true;
        else if (exhausted && stamina >= ExhaustedUntil) exhausted = false;
    }

    /// <summary>Heat felt outside: heatwave 1, summer 0.6, spring / autumn 0.15, winter 0; half at night.</summary>
    public static float Heat(Season s, Weather w, bool night)
    {
        float h = s == Season.Summer ? 0.6f : s == Season.Spring || s == Season.Autumn ? 0.15f : 0f;
        if (w == Weather.Heatwave) h = Mathf.Max(h + 0.4f, 0.7f);
        if (w == Weather.Rain || w == Weather.Cloudy) h *= 0.7f;
        if (night) h *= 0.5f;
        return Mathf.Clamp01(h);
    }
}

/// <summary>Speed rules on foot: carried weight and crouching slow you, heavy loads stop sprinting.</summary>
public static class MoveRules
{
    public const float MaxCarryKg = 50f;
    public const float NoSprintKg = 20f;

    /// <summary>1 with empty hands, down to 0.5 at the 50 kg limit.</summary>
    public static float CarryFactor(float kg) => 1f - 0.5f * Mathf.Clamp01(kg / MaxCarryKg);

    public static float SpeedFactor(float kg, bool crouching, bool exhausted, float hydration)
    {
        float f = CarryFactor(kg);
        if (crouching) f *= 0.5f;
        if (exhausted) f *= 0.85f;
        if (hydration < 0.15f) f *= 0.9f;
        return f;
    }

    public static bool CanSprint(float kg, bool crouching, bool exhausted) => !exhausted && !crouching && kg < NoSprintKg;

    /// <summary>Acceleration multiplier for the controller (smooth start / stop; slower with a load).</summary>
    public static float Acceleration(float kg) => Mathf.Lerp(0.6f, 0.3f, Mathf.Clamp01(kg / MaxCarryKg));
}

public enum HandTool { Hands, Hoe, WateringCan, Shovel, Brush }

public static class HandTools
{
    public static readonly HandTool[] All = { HandTool.Hands, HandTool.Hoe, HandTool.WateringCan, HandTool.Shovel, HandTool.Brush };

    /// <summary>English Loc key of a tool.</summary>
    public static string Key(HandTool t)
    {
        switch (t)
        {
            case HandTool.Hoe: return "Hoe";
            case HandTool.WateringCan: return "Watering can";
            case HandTool.Shovel: return "Shovel";
            case HandTool.Brush: return "Grooming brush";
            default: return "Bare hands";
        }
    }

    /// <summary>Stamina cost of one use.</summary>
    public static float Cost(HandTool t) => t == HandTool.Hoe ? 8f : t == HandTool.Shovel ? 10f : t == HandTool.WateringCan ? 4f : t == HandTool.Brush ? 3f : 0f;

    public static HandTool Cycle(HandTool t, int d) => All[((Array.IndexOf(All, t) + d) % All.Length + All.Length) % All.Length];
}

// ------------------------------------------------------------------ animals

/// <summary>Biology per species (days are in-game days; one farm year = 4 seasons).</summary>
public class SpeciesTraits
{
    public float ration;        // feed units eaten per animal per day
    public int maturity;        // days until adult (produces, breeds)
    public int prime;           // age when it starts to slow down
    public int gestation;       // days pregnant (incubation for hens)
    public float heatSens;      // 0 heat-proof (camel) .. 1 very sensitive (chicken)

    private static readonly Dictionary<string, SpeciesTraits> table = new Dictionary<string, SpeciesTraits>
    {
        { "chicken", new SpeciesTraits { ration = 0.1f, maturity = 4, prime = 40, gestation = 2, heatSens = 1f } },
        { "sheep",   new SpeciesTraits { ration = 0.4f, maturity = 8, prime = 70, gestation = 5, heatSens = 0.6f } },
        { "goat",    new SpeciesTraits { ration = 0.35f, maturity = 8, prime = 70, gestation = 5, heatSens = 0.45f } },
        { "cow",     new SpeciesTraits { ration = 0.8f, maturity = 12, prime = 100, gestation = 9, heatSens = 0.9f } },
        { "camel",   new SpeciesTraits { ration = 0.7f, maturity = 16, prime = 160, gestation = 13, heatSens = 0.15f } },
    };
    private static readonly SpeciesTraits fallback = new SpeciesTraits { ration = 0.4f, maturity = 8, prime = 80, gestation = 6, heatSens = 0.6f };

    public static SpeciesTraits Get(string id) => id != null && table.TryGetValue(id, out var t) ? t : fallback;
}

/// <summary>One animal in the herd. Needs are 0..1; trait is its inherited productivity (about 0.6 .. 1.6).</summary>
[Serializable]
public class AnimalRecord
{
    public int uid;
    public string species;
    public bool male;
    public int ageDays;
    public float food = 1f, water = 1f, clean = 1f, health = 1f, happiness = 0.8f;
    public float heatStress;
    public float trait = 1f;
    public string illness = "";       // "" healthy, else an Illness id
    public int sickDays;
    public int gestationLeft;          // > 0 while pregnant
    public float sireTrait = 1f;
    public int groomedDay = -99;

    public bool Sick => !string.IsNullOrEmpty(illness);
    public bool Pregnant => gestationLeft > 0;
    public bool Adult => ageDays >= SpeciesTraits.Get(species).maturity;
}

/// <summary>Daily husbandry rules shared by the herd manager and the tests.</summary>
public static class Husbandry
{
    public static readonly string[] Illnesses = { "respiratory", "digestive", "heatstroke" };

    public static string IllnessKey(string id)
    {
        switch (id)
        {
            case "respiratory": return "Respiratory infection";
            case "digestive": return "Digestive upset";
            case "heatstroke": return "Heat stroke";
            default: return "Healthy";
        }
    }

    /// <summary>Heat load of the day on the pen (0..1) before shade and fans.</summary>
    public static float HeatLoad(Season s, Weather w) => StaminaModel.Heat(s, w, false);

    /// <summary>Heat stress of one species after shade (-0.35) and misting fans (-0.45; need water in the trough).</summary>
    public static float HeatStress(string species, float load, bool shade, bool fans)
    {
        float l = load * SpeciesTraits.Get(species).heatSens;
        if (shade) l -= 0.35f * load;
        if (fans) l -= 0.45f * load;
        return Mathf.Clamp01(l);
    }

    /// <summary>How much this animal produces now relative to a perfect adult with trait 1 (0 for young animals).</summary>
    public static float Productivity(AnimalRecord a)
    {
        if (a == null || !a.Adult) return 0f;
        var sp = SpeciesTraits.Get(a.species);
        float needs = Mathf.Clamp01(0.25f + 0.25f * a.food + 0.25f * a.water + 0.1f * a.clean + 0.15f * a.happiness);
        float age = a.ageDays <= sp.prime ? 1f : Mathf.Max(0.3f, 1f - (a.ageDays - sp.prime) / (float)sp.prime);
        float p = a.trait * needs * (0.4f + 0.6f * a.health) * age * (1f - 0.6f * a.heatStress);
        if (a.Sick) p *= 0.35f;
        if (a.male && a.species == "chicken") p = 0f;   // roosters don't lay
        return Mathf.Max(0f, p);
    }

    /// <summary>Sale value: base price by age (young cheaper, old declining), health, illness and genetics.</summary>
    public static int Value(AnimalRecord a, int basePrice)
    {
        var sp = SpeciesTraits.Get(a.species);
        float age = a.ageDays < sp.maturity ? 0.45f + 0.55f * a.ageDays / Mathf.Max(1f, sp.maturity)
                  : a.ageDays <= sp.prime ? 1f : Mathf.Max(0.3f, 1f - (a.ageDays - sp.prime) / (float)sp.prime);
        float v = basePrice * age * (0.35f + 0.65f * a.health) * (a.Sick ? 0.5f : 1f) * (0.8f + 0.2f * a.trait) * (a.Pregnant ? 1.15f : 1f);
        return Mathf.Max(Mathf.RoundToInt(basePrice * 0.05f), Mathf.RoundToInt(v));
    }

    /// <summary>Offspring trait: parents' average plus up to +-0.1 variation, kept in 0.5 .. 1.6.</summary>
    public static float Inherit(float mother, float father, System.Random rnd)
    {
        float v = (mother + father) * 0.5f + ((float)rnd.NextDouble() * 2f - 1f) * 0.1f;
        return Mathf.Clamp(v, 0.5f, 1.6f);
    }

    /// <summary>Daily needs update for one animal (feed / water already given or not).</summary>
    public static void Day(AnimalRecord a, bool fed, bool watered, float penClean, float stress, int day, System.Random rnd)
    {
        a.ageDays++;
        a.heatStress = stress;
        a.food = fed ? 1f : Mathf.Max(0f, a.food - 0.5f);
        a.water = watered ? 1f : Mathf.Max(0f, a.water - (0.6f + 0.4f * stress));
        a.clean = Mathf.Clamp01(Mathf.Lerp(a.clean, penClean, 0.6f) - 0.08f);

        // Illness: neglect, dirt and heat make it likely; healthy, clean animals rarely fall ill.
        if (a.Sick)
        {
            a.sickDays++;
            a.health -= a.illness == "heatstroke" ? 0.2f : 0.12f;
        }
        else
        {
            float risk = 0.01f + (a.clean < 0.3f ? 0.12f : 0f) + (a.food < 0.3f || a.water < 0.3f ? 0.08f : 0f) + 0.1f * (1f - a.health);
            float heatRisk = stress > 0.4f ? 0.25f * stress : 0f;
            double r = rnd.NextDouble();
            if (r < heatRisk) { a.illness = "heatstroke"; a.sickDays = 0; }
            else if (r < heatRisk + risk) { a.illness = a.clean < 0.4f ? "digestive" : "respiratory"; a.sickDays = 0; }
        }
        int unmet = (a.food < 0.3f ? 1 : 0) + (a.water < 0.3f ? 1 : 0) + (a.clean < 0.25f ? 1 : 0);
        if (unmet == 0 && !a.Sick) a.health += 0.08f; else a.health -= 0.1f * unmet;
        if (stress > 0.5f) a.health -= 0.1f * stress;
        // Mild cases can pass on their own when the animal is well kept (heat stroke needs treatment).
        if (a.Sick && a.illness != "heatstroke" && unmet == 0 && a.sickDays >= 2 && rnd.NextDouble() < 0.2) { a.illness = ""; a.sickDays = 0; }
        a.health = Mathf.Clamp(a.health, 0.05f, 1f);   // neglect weakens, never kills
        bool groomed = day - a.groomedDay <= 1;
        a.happiness = Mathf.Clamp01(0.3f * a.food + 0.3f * a.water + 0.15f * a.clean + 0.25f * a.health - 0.35f * stress - (a.Sick ? 0.2f : 0f) + (groomed ? 0.15f : 0f));
    }

    public static void Cure(AnimalRecord a, float healthBoost)
    {
        a.illness = ""; a.sickDays = 0;
        a.health = Mathf.Clamp01(a.health + healthBoost);
    }

    /// <summary>Can this female conceive today with a male around?</summary>
    public static bool CanBreed(AnimalRecord f) => f != null && !f.male && f.Adult && !f.Pregnant && !f.Sick && f.health > 0.6f && f.happiness > 0.55f;
}
