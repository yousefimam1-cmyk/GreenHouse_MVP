using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pests and diseases on the open fields (data: CropsData pests, cures owned in CropCareManager). Every morning a field with a
/// growing crop may get a pest that fits the season, weather and crop family; a pest slows growth (growthMult) and adds yield
/// damage every day until treated: E on the field uses a matching cure you own, or the sprayer with pesticide / neem covers
/// it (half the crop sprayed). A treatment protects the field for the cure's days.
/// </summary>
public static class FieldPests
{
    public static System.Random Rng = new System.Random();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Rng = new System.Random();

    private static IEnumerable<FarmField> Fields => FarmFields.Instance != null ? FarmFields.Instance.Fields : new List<FarmField>();

    public static bool Growing(FarmField f)
    {
        for (int i = 0; i < f.Cells; i++) if (f.ground[i] == (byte)Ground.Sown && !f.Has(i, CellFlags.Withered)) return true;
        return false;
    }

    public static float GrowthMultiplier(FarmField f)
    {
        var pest = string.IsNullOrEmpty(f.pest) ? null : CropsData.Pest(f.pest);
        return pest != null ? Mathf.Clamp(pest.growthMult, 0.1f, 1f) : 1f;
    }

    public static List<FarmField> Infested()
    {
        var list = new List<FarmField>();
        foreach (var f in Fields) if (!string.IsNullOrEmpty(f.pest) && CropsData.Pest(f.pest) != null) list.Add(f);
        return list;
    }

    /// <summary>Daily: damage from pests that are there, new pests on unprotected growing fields.</summary>
    public static void OnDay(FarmFields ff)
    {
        var clock = WorldClock.Instance;
        string season = clock != null ? WorldClock.SeasonId(clock.Season) : "spring";
        Weather w = clock != null ? clock.Weather : Weather.Sunny;
        foreach (var f in ff.Fields)
        {
            bool growing = Growing(f);
            if (!growing) { if (f.pest != "") { f.pest = ""; f.SaveDirty = true; } continue; }
            var pest = CropsData.Pest(f.pest);
            if (pest != null) { f.pestDamage = Mathf.Min(0.6f, f.pestDamage + pest.damagePerDay); f.SaveDirty = true; continue; }
            if (f.protectDays > 0) continue;
            var s = FarmSim.Summarize(f);
            string family = FcData.Family(s.mainCrop);
            foreach (var d in CropsData.Data.pests)
            {
                if (d.id == "locust" || !CropsData.Has(d.seasons, season) || !CropsData.Has(d.families, family)) continue;
                if (Rng.NextDouble() < d.chance * CropCareManager.WeatherFactor(d, w)) { Infest(f, d.id); break; }
            }
        }
    }

    /// <summary>Put a pest on a field (ignored while it is protected or has no growing crop).</summary>
    public static bool Infest(FarmField f, string pestId)
    {
        var pest = CropsData.Pest(pestId);
        if (f == null || pest == null || f.protectDays > 0 || !Growing(f)) return false;
        f.pest = pestId; f.pestSprayedCells = 0; f.SaveDirty = true;
        FcText.Say(CropsData.Name(pest) + " - " + FarmFields.FieldName(f));
        return true;
    }

    /// <summary>A desert locust swarm: every unprotected growing field loses a quarter of its growth and gets locusts.</summary>
    public static void LocustSwarm()
    {
        foreach (var f in Fields)
        {
            if (f.protectDays > 0 || !Growing(f)) continue;
            for (int i = 0; i < f.Cells; i++)
                if (f.ground[i] == (byte)Ground.Sown) { f.Set(FieldLayer.Growth, i, Mathf.Max(0f, Mathf.Min(1f, f.Get(FieldLayer.Growth, i)) - 0.25f)); f.Touch(i); }
            f.pest = "locust";
            f.pestDamage = Mathf.Min(0.6f, f.pestDamage + 0.1f);
        }
    }

    /// <summary>First cure you own that treats this field's pest, or null.</summary>
    public static CureDef OwnedCure(FarmField f)
    {
        if (f == null || string.IsNullOrEmpty(f.pest) || CropCareManager.Instance == null) return null;
        return CropCareManager.Instance.OwnedCureFor(f.pest);
    }

    /// <summary>E on an infested field: use a matching cure you own.</summary>
    public static bool TreatByHand(FarmField f)
    {
        var cure = OwnedCure(f);
        if (cure == null || !CropCareManager.Instance.UseItem(cure.id)) return false;
        Clear(f, cure);
        return true;
    }

    /// <summary>The sprayer covered the field with a cure (product id = the cure / amendment care id).</summary>
    public static void Cured(FarmField f, string productId)
    {
        if (f == null || string.IsNullOrEmpty(f.pest)) return;
        var a = FcData.Amendment(productId);
        string cureId = a != null && !string.IsNullOrEmpty(a.careId) ? a.careId : productId;
        CureDef cure = null;
        foreach (var c in CropsData.Data.cures) if (c.id == cureId) cure = c;
        var pest = CropsData.Pest(f.pest);
        if (cure != null && !(CropsData.Has(cure.cures, f.pest) || (pest != null && CropsData.Has(pest.curedBy, cure.id)))) return;   // wrong product
        Clear(f, cure);
    }

    private static void Clear(FarmField f, CureDef cure)
    {
        string pestName = CropsData.Name(CropsData.Pest(f.pest));
        f.pest = ""; f.pestSprayedCells = 0;
        f.protectDays = Mathf.Max(f.protectDays, cure != null ? cure.protectDays : 2);
        f.SaveDirty = true;
        GameEvents.RaiseCustom("cropsCurePest", 1f);
        FcText.Say((cure != null ? CropsData.Name(cure) + ": " : "") + pestName + " - " + CropsText.R("treated"));
    }
}
