using System;
using System.Globalization;
using System.Text;
using UnityEngine;

// Soil, water and crop agronomy for the open fields. Pure logic (no scene access) so it is tested headless.
// Data: Resources/Data/soil.json (sources and units in its "notes"). Used by SoilManager.

[Serializable] public class SoilCropDef
{
    public string id = "";
    public float n = 90f, p = 25f, k = 80f, rn = 40f, rp = 10f, rk = 30f, fix;
    public float phMin = 5.5f, phMax = 8.3f, ecT = 3f, ecS = 10f;
    public float tBase = 6f, tOptLo = 16f, tOptHi = 28f, tMax = 35f, et = 450f, ky = 1f, residue = 0.5f;
}

[Serializable] public class SoilStartDef { public float n = 200f, p = 55f, k = 250f, ph = 7.8f, om = 1f, ec = 1.5f, compaction = 0.15f; }

[Serializable] public class SoilRegionDef
{
    public string id = "";
    public float[] temp = { 24f, 34f, 27f, 15f }, et0 = { 6f, 9f, 6f, 3.5f };   // spring, summer, autumn, winter
    public float ecWater = 1.5f, waterCost = 0.1f, drainage = 1f;
    public SoilStartDef start = new SoilStartDef();
}

[Serializable] public class SoilIrrigationDef { public string id = "flood"; public float eff = 0.6f, salt = 1f, leach = 0.25f; }
[Serializable] public class SoilRotationDef { public string prev = "", next = ""; public float mult = 1f; }

[Serializable] public class SoilAmendmentDef
{
    public string id = "", nameEn = "", nameAr = "", careId = "";
    public int price = 50;
    public bool shop = true, herbicide, mulch, growing;
    public float n, p, k, om, ph, ec, compaction, manureKg;
}

[Serializable] public class SoilDataFile
{
    public int kitPrice = 25;
    public float leachMm = 150f, sameFamilyMult = 0.9f, otherFamilyMult = 1.1f;
    public SoilCropDef[] crops;
    public SoilRegionDef[] regions;
    public SoilIrrigationDef[] irrigation;
    public SoilRotationDef[] rotation;
    public SoilAmendmentDef[] amendments;
}

public static class SoilData
{
    private static SoilDataFile data;
    private static readonly SoilCropDef DefaultCrop = new SoilCropDef();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => data = null;

    public static SoilDataFile Data
    {
        get
        {
            if (data != null) return data;
            var json = Resources.Load<TextAsset>("Data/soil");
            try { if (json != null) data = JsonUtility.FromJson<SoilDataFile>(json.text); }
            catch (Exception e) { Debug.LogError("[Soil] Invalid soil.json: " + e.Message); }
            if (data == null) data = new SoilDataFile();
            if (data.crops == null) data.crops = new SoilCropDef[0];
            if (data.regions == null || data.regions.Length == 0) data.regions = new[] { new SoilRegionDef { id = Regions.DefaultId } };
            if (data.irrigation == null) data.irrigation = new SoilIrrigationDef[0];
            if (data.rotation == null) data.rotation = new SoilRotationDef[0];
            if (data.amendments == null) data.amendments = new SoilAmendmentDef[0];
            return data;
        }
    }

    public static SoilCropDef Crop(string id)
    {
        if (string.IsNullOrEmpty(id)) return DefaultCrop;
        foreach (var c in Data.crops) if (c.id == id) return c;
        return DefaultCrop;
    }

    public static SoilRegionDef Region(string id)
    {
        foreach (var r in Data.regions) if (r.id == id) return r;
        return Data.regions[0];
    }

    public static SoilRegionDef CurrentRegion => Region(Regions.Current != null ? Regions.Current.id : Regions.DefaultId);

    public static string IrrigationId(IrrigationKind k)
    {
        switch (k) { case IrrigationKind.Drip: return "drip"; case IrrigationKind.Sprinklers: return "sprinkler"; case IrrigationKind.CenterPivot: return "pivot"; default: return "flood"; }
    }

    public static SoilIrrigationDef Irrigation(IrrigationKind k)
    {
        string id = IrrigationId(k);
        foreach (var i in Data.irrigation) if (i.id == id) return i;
        return new SoilIrrigationDef { id = id };
    }

    public static SoilAmendmentDef Amendment(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var a in Data.amendments) if (a.id == id) return a;
        return null;
    }

    public static SoilAmendmentDef ForCareItem(string careId)
    {
        if (string.IsNullOrEmpty(careId)) return null;
        foreach (var a in Data.amendments) if (a.careId == careId) return a;
        return null;
    }

    /// <summary>Yield multiplier for crop family 'next' after 'prev' (1 when there was no previous crop).</summary>
    public static float Rotation(string prev, string next)
    {
        if (string.IsNullOrEmpty(prev) || string.IsNullOrEmpty(next)) return 1f;
        foreach (var r in Data.rotation) if (r.prev == prev && r.next == next) return r.mult;
        return prev == next ? Data.sameFamilyMult : Data.otherFamilyMult;
    }

    public static string Name(SoilAmendmentDef a) => a == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(a.nameAr) ? a.nameAr : a.nameEn;
}

/// <summary>Everything the soil model knows about one field plot. Saved compactly by SoilManager.</summary>
public class SoilState
{
    // soil
    public float n, p, k, ph, om, ec, compaction, weeds, waterlog, residue, seedbed = 1f;
    public bool mulch, weedProtect;
    public int tillDepth, sampledDay = -1000, density = 1;
    // this crop (reset at harvest)
    public float lastGrowth = -1f, accG, accN, accP, accK, accPh, accEc, accComp, stressW, stressLog, stressHeat;
    public float cycN, cycP, cycK, cycWater, cycCost;
    public int appliedMask;
    public string lastCrop = "", history = "";

    public const int Fields = 32;

    public static SoilState New(SoilStartDef s)
    {
        if (s == null) s = new SoilStartDef();
        return new SoilState { n = s.n, p = s.p, k = s.k, ph = s.ph, om = s.om, ec = s.ec, compaction = s.compaction };
    }

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public string Pack()
    {
        var sb = new StringBuilder(220);
        float[] v = { n, p, k, ph, om, ec, compaction, weeds, waterlog, residue, seedbed, mulch ? 1 : 0, weedProtect ? 1 : 0, tillDepth, sampledDay, density,
                      lastGrowth, accG, accN, accP, accK, accPh, accEc, accComp, stressW, stressLog, stressHeat, cycN, cycP, cycK, cycWater, cycCost };
        for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(F(v[i])); }
        sb.Append(',').Append(appliedMask);
        return sb.ToString();
    }

    public static SoilState Unpack(string s, SoilStartDef fallback)
    {
        var st = New(fallback);
        if (string.IsNullOrEmpty(s)) return st;
        var parts = s.Split(',');
        if (parts.Length < Fields) return st;
        var v = new float[Fields];
        for (int i = 0; i < Fields; i++) float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
        st.n = v[0]; st.p = v[1]; st.k = v[2]; st.ph = v[3]; st.om = v[4]; st.ec = v[5]; st.compaction = v[6]; st.weeds = v[7]; st.waterlog = v[8];
        st.residue = v[9]; st.seedbed = v[10]; st.mulch = v[11] > 0.5f; st.weedProtect = v[12] > 0.5f; st.tillDepth = (int)v[13]; st.sampledDay = (int)v[14];
        st.density = Mathf.Clamp((int)v[15], 0, 2); st.lastGrowth = v[16]; st.accG = v[17]; st.accN = v[18]; st.accP = v[19]; st.accK = v[20];
        st.accPh = v[21]; st.accEc = v[22]; st.accComp = v[23]; st.stressW = v[24]; st.stressLog = v[25]; st.stressHeat = v[26];
        st.cycN = v[27]; st.cycP = v[28]; st.cycK = v[29]; st.cycWater = v[30]; st.cycCost = v[31];
        if (parts.Length > Fields) int.TryParse(parts[Fields], out st.appliedMask);
        return st;
    }
}

/// <summary>Yield factors of a field (1 = no loss). Pests are applied by CropCareManager; shown here for the limiting factor.</summary>
public struct SoilFactors
{
    public float n, p, k, ph, salt, compaction, water, waterlog, heat, weeds, seedbed, density, pests;
    public float Product => n * p * k * ph * salt * compaction * water * waterlog * heat * weeds * seedbed * density;

    public static readonly string[] Names = { "Nitrogen", "Phosphorus", "Potassium", "Soil pH", "Salinity", "Compaction", "Water stress", "Waterlogging", "Heat stress", "Weeds", "Seedbed", "Sowing density", "Pests" };
    /// <summary>Factors that only a soil test can show.</summary>
    public static bool NeedsTest(int i) => i <= 5;

    public float Get(int i)
    {
        switch (i)
        {
            case 0: return n; case 1: return p; case 2: return k; case 3: return ph; case 4: return salt; case 5: return compaction;
            case 6: return water; case 7: return waterlog; case 8: return heat; case 9: return weeds; case 10: return seedbed; case 11: return density;
            default: return pests;
        }
    }

    /// <summary>Index of the lowest factor, or -1 when nothing costs more than 3%.</summary>
    public int Limiting
    {
        get
        {
            int best = -1; float low = 0.97f;
            for (int i = 0; i < Names.Length; i++) { float f = Get(i); if (f < low) { low = f; best = i; } }
            return best;
        }
    }

    public string LimitingName => Limiting < 0 ? "" : Names[Limiting];
}

public static class SoilModel
{
    public const float SaltBuffer = 1700f;     // mm of applied water x dS/m that raise the root-zone ECe by 1 dS/m
    public const float RefEt0 = 6f;            // mm/day the crop "et" values refer to
    public const float WeedSeconds = 600f;     // real seconds for weeds to cover an untreated field (at growth speed 1)
    public static readonly float[] DensitySeed = { 0.75f, 1f, 1.3f };
    public static readonly string[] DensityNames = { "Low", "Normal", "High" };

    // ---------------------------------------------------------------- climate

    public static int SeasonIndex(Season s) { switch (s) { case Season.Spring: return 0; case Season.Summer: return 1; case Season.Autumn: return 2; default: return 3; } }

    /// <summary>Mean day temperature (C) from region, season and today's weather.</summary>
    public static float Temperature(SoilRegionDef r, Season s, Weather w)
    {
        int i = SeasonIndex(s);
        float t = r != null && r.temp != null && r.temp.Length > i ? r.temp[i] : 25f;
        switch (w) { case Weather.Heatwave: return t + 7f; case Weather.Rain: return t - 4f; case Weather.Cloudy: return t - 2f; default: return t; }
    }

    /// <summary>Reference evapotranspiration relative to 6 mm/day, without the weather (season and region).</summary>
    public static float SeasonEt(SoilRegionDef r, Season s)
    {
        int i = SeasonIndex(s);
        float e = r != null && r.et0 != null && r.et0.Length > i ? r.et0[i] : RefEt0;
        return e / RefEt0;
    }

    public static float WeatherEt(Weather w) { switch (w) { case Weather.Heatwave: return 1.35f; case Weather.Cloudy: return 0.8f; case Weather.Rain: return 0.35f; default: return 1f; } }

    /// <summary>FAO-56 style crop coefficient by growth stage.</summary>
    public static float Kc(float growth)
    {
        if (growth < 0.1f) return 0.5f;
        if (growth < 0.5f) return 0.5f + (growth - 0.1f) / 0.4f * 0.65f;
        if (growth < 0.8f) return 1.15f;
        return 0.8f;
    }

    public static string Stage(float growth)
    {
        if (growth < 0.1f) return "Germination";
        if (growth < 0.5f) return "Vegetative";
        if (growth < 0.75f) return "Flowering";
        return "Ripening";
    }

    /// <summary>0..1 development speed from temperature (trapezoid on the crop's cardinal temperatures).</summary>
    public static float TempFactor(float t, SoilCropDef c)
    {
        if (t <= c.tBase) return 0f;
        if (t < c.tOptLo) return (t - c.tBase) / Mathf.Max(1f, c.tOptLo - c.tBase);
        if (t <= c.tOptHi) return 1f;
        if (t <= c.tMax) return 1f - 0.6f * (t - c.tOptHi) / Mathf.Max(1f, c.tMax - c.tOptHi);
        return Mathf.Max(0f, 0.4f - 0.05f * (t - c.tMax));
    }

    /// <summary>Growth speed multiplier from temperature (0.5 .. 1.1 so the season rule in FieldPlot stays the main one).</summary>
    public static float TempGrowth(float t, SoilCropDef c) => 0.5f + 0.6f * TempFactor(t, c);

    /// <summary>How fast the plot dries compared to the default (crop stage, season heat, mulch / straw, organic matter).</summary>
    public static float DryFactor(SoilState s, float growth, float seasonEt)
    {
        float cover = s.mulch ? 0.75f : s.residue > 0.4f ? 0.85f : 1f;
        float om = Mathf.Clamp(1f / (1f + 0.12f * (s.om - 1f)), 0.85f, 1.1f);
        return Mathf.Clamp(Kc(growth) * seasonEt * cover * om, 0.3f, 2f);
    }

    // ---------------------------------------------------------------- factors

    public static float Response(float avail, float need) => need <= 0f ? 1f : Mathf.Clamp01(0.35f + 0.65f * avail / need);
    public static float PAvailability(float ph) => Mathf.Clamp(ph > 7.5f ? 1f - 0.3f * (ph - 7.5f) : ph < 5.5f ? 1f - 0.25f * (5.5f - ph) : 1f, 0.4f, 1f);
    public static float EffectiveP(SoilState s) => s.p * PAvailability(s.ph);
    public static float PhFactor(float ph, SoilCropDef c) => ph < c.phMin ? Mathf.Max(0.3f, 1f - 0.15f * (c.phMin - ph)) : ph > c.phMax ? Mathf.Max(0.3f, 1f - 0.15f * (ph - c.phMax)) : 1f;
    public static float SaltFactor(float ec, SoilCropDef c) => Mathf.Clamp01(1f - c.ecS / 100f * Mathf.Max(0f, ec - c.ecT));
    public static float CompactionFactor(float comp) => !GameSettings.PlowingRequired ? 1f : 1f - 0.4f * Mathf.Max(0f, comp - 0.25f) / 0.75f;
    public static float WeedFactor(float w) => 1f - 0.5f * Mathf.Max(0f, w - 0.15f) / 0.85f;

    private static float Blend(float acc, float accG, float now) => accG > 0.001f ? Mathf.Clamp01(acc + Mathf.Max(0f, 1f - accG) * now) : now;

    /// <summary>Yield factors of the crop on this soil: the crop's history so far (stress, average soil) plus the soil now for the rest.</summary>
    public static SoilFactors Factors(SoilState s, SoilCropDef c, float pests = 1f)
    {
        var f = new SoilFactors();
        f.n = Blend(s.accN, s.accG, Response(s.n, c.n));
        f.p = Blend(s.accP, s.accG, Response(EffectiveP(s), c.p));
        f.k = Blend(s.accK, s.accG, Response(s.k, c.k));
        f.ph = Blend(s.accPh, s.accG, PhFactor(s.ph, c));
        f.salt = Blend(s.accEc, s.accG, SaltFactor(s.ec, c));
        f.compaction = Blend(s.accComp, s.accG, CompactionFactor(s.compaction));
        float ky = c.ky * (s.density == 0 ? 0.7f : 1f);
        f.water = Mathf.Clamp(1f - ky * s.stressW, 0.2f, 1f);
        f.waterlog = Mathf.Clamp(1f - 0.5f * s.stressLog, 0.4f, 1f);
        f.heat = Mathf.Clamp(1f - 1.6f * s.stressHeat, 0.5f, 1f);
        f.weeds = WeedFactor(s.weeds);
        f.seedbed = Mathf.Clamp(s.seedbed, 0.5f, 1f);
        f.density = s.density == 0 ? 0.9f : s.density == 2 ? (s.stressW < 0.1f && f.n >= 0.99f ? 1.06f : 0.9f) : 1f;
        f.pests = Mathf.Clamp01(pests);
        return f;
    }

    /// <summary>Growth speed multiplier from the soil now (waterlogging, salt, weeds, compaction).</summary>
    public static float GrowthFactor(SoilState s, SoilCropDef c)
    {
        float m = 1f;
        if (s.waterlog > 0.5f) m *= 0.6f;
        m *= 1f - 0.5f * (1f - SaltFactor(s.ec, c));
        m *= 1f - 0.3f * Mathf.Max(0f, s.weeds - 0.15f);
        m *= 1f - 0.3f * Mathf.Max(0f, s.compaction - 0.25f);
        return Mathf.Clamp(m, 0.2f, 1f);
    }

    // ---------------------------------------------------------------- dynamics

    /// <summary>Water applied (mm) to meet a crop water use with an irrigation method.</summary>
    public static float Applied(float etcMm, SoilIrrigationDef irr) => etcMm / Mathf.Max(0.1f, irr.eff);

    /// <summary>Book irrigation water on the soil: salt brought in (more in heat, where it evaporates) minus what drains past the roots.</summary>
    public static void Irrigate(SoilState s, float appliedMm, SoilIrrigationDef irr, float ecWater, float heat)
    {
        if (appliedMm <= 0f) return;
        float conc = Mathf.Max(0.8f, 1f + 0.5f * (heat - 1f));
        s.ec += appliedMm * ecWater * irr.salt * conc / SaltBuffer;
        float leach = Mathf.Clamp01(irr.leach * appliedMm / 800f);
        s.ec = Mathf.Max(0.2f, s.ec * (1f - leach));
        s.n = Mathf.Max(0f, s.n * (1f - leach * 0.2f));   // nitrate leaves with the drainage water
    }

    /// <summary>One growth step of the crop on this soil (dg = growth gained). Returns the irrigation water applied in mm.</summary>
    public static float Grow(SoilState s, SoilCropDef c, float growthBefore, float dg, float water, float tempC, float et, bool rain, SoilIrrigationDef irr, float ecWater)
    {
        if (dg <= 0f) return 0f;
        float g = Mathf.Min(dg, Mathf.Max(0f, 1.05f - s.accG));
        s.accG += g;
        s.accN += g * Response(s.n, c.n);
        s.accP += g * Response(EffectiveP(s), c.p);
        s.accK += g * Response(s.k, c.k);
        s.accPh += g * PhFactor(s.ph, c);
        s.accEc += g * SaltFactor(s.ec, c);
        s.accComp += g * CompactionFactor(s.compaction);
        s.stressW += dg * Mathf.Clamp01((0.3f - water) / 0.25f);
        if (s.waterlog > 0.5f) s.stressLog += dg;
        float mid = growthBefore + dg * 0.5f;
        if (mid >= 0.5f && mid < 0.75f && tempC > c.tMax) s.stressHeat += dg;
        float etc = c.et * dg * et;
        if (rain) return 0f;
        float applied = Applied(etc, irr);
        Irrigate(s, applied, irr, ecWater, et);
        return applied;
    }

    /// <summary>Weeds grow on every field (less under a closed crop canopy, mulch or herbicide; more when wet and warm).</summary>
    public static void GrowWeeds(SoilState s, float seconds, bool growingCrop, float growth, float water, float tempC)
    {
        float r = (water > 0.3f ? 1.3f : 0.6f) * (tempC < 10f ? 0.4f : tempC > 38f ? 0.7f : 1f);
        if (growingCrop && growth > 0.4f) r *= 0.5f;
        if (s.mulch) r *= 0.5f;
        if (s.weedProtect) r *= 0.25f;
        s.weeds = Mathf.Clamp01(s.weeds + r * seconds / WeedSeconds);
    }

    /// <summary>Daily soil processes: organic matter releases nitrogen and slowly burns off (faster in heat), surplus nitrate
    /// leaches, rain washes some salt down, wetting/drying loosens compaction, standing water drains.</summary>
    public static void Daily(SoilState s, Season season, Weather w, SoilRegionDef r)
    {
        float heat = season == Season.Summer || w == Weather.Heatwave ? 1.5f : 1f;
        s.n += s.om * 4f;
        s.om = Mathf.Max(0.1f, s.om - 0.004f * heat);
        if (s.n > 260f) s.n -= (s.n - 260f) * 0.3f;
        if (w == Weather.Rain) { s.ec = Mathf.Max(0.2f, s.ec * 0.93f); s.n *= 0.97f; s.waterlog = Mathf.Min(1f, s.waterlog + 0.2f * (1f + s.compaction)); }
        s.compaction = Mathf.Max(0f, s.compaction - 0.01f);
        if (s.residue > 0f && !s.mulch) s.residue = Mathf.Max(0f, s.residue - 0.05f);
    }

    /// <summary>Standing water drains (slower on clay and compacted soil). Per real second.</summary>
    public static void Drain(SoilState s, float seconds, float drainage)
    {
        if (s.waterlog <= 0f) return;
        s.waterlog = Mathf.Max(0f, s.waterlog - 0.01f * drainage * (1.2f - s.compaction) * seconds);
    }

    /// <summary>Watering a soil that is still wet: water stands in the field.</summary>
    public static void OverWater(SoilState s, float waterBefore)
    {
        if (waterBefore < 0.85f) return;
        s.waterlog = Mathf.Min(1f, s.waterlog + 0.3f * (1f + s.compaction));
    }

    /// <summary>Tillage. Deep (tractor plough ~25 cm) breaks compaction, buries weeds and works the straw in; shallow (hand hoe ~10 cm)
    /// leaves more weeds. Working wet soil smears and compacts it; dry soil in the heat leaves a cloddy seedbed.</summary>
    public static string Till(SoilState s, bool deep, float water, bool hot)
    {
        s.tillDepth = deep ? 25 : 10;
        s.compaction = Mathf.Max(0f, s.compaction - (deep ? 0.45f : 0.12f));
        s.weeds = deep ? 0.02f : Mathf.Max(0.12f, s.weeds * 0.4f);
        float inc = deep ? 1f : 0.5f;
        s.om += (s.residue + (s.mulch ? 0.6f : 0f)) * inc * 0.08f;
        s.residue *= 1f - inc;
        s.mulch = false;
        if (deep) s.om = Mathf.Max(0.1f, s.om - 0.01f);
        if (water > 0.75f) { s.compaction = Mathf.Min(1f, s.compaction + 0.15f); s.seedbed = 0.9f; return "Too wet: the soil smeared and compacted"; }
        if (water < 0.1f && hot) { s.seedbed = 0.93f; return "Too dry: cloddy seedbed"; }
        s.seedbed = 1f;
        return deep ? "Deep plowing: good seedbed" : "Shallow cultivation";
    }

    /// <summary>Spread an amendment. Returns true when it changed anything.</summary>
    public static void Apply(SoilState s, SoilAmendmentDef a)
    {
        if (a == null) return;
        s.n += a.n; s.p += a.p; s.k += a.k; s.om += a.om;
        s.ph = Mathf.Clamp(s.ph + a.ph, 4f, 9.5f);
        s.ec = Mathf.Max(0.2f, s.ec + a.ec);
        s.compaction = Mathf.Clamp01(s.compaction + a.compaction);
        if (a.herbicide) { s.weeds = 0f; s.weedProtect = true; }
        if (a.mulch) s.mulch = true;
        s.cycN += a.n; s.cycP += a.p; s.cycK += a.k;
    }

    /// <summary>Leaching irrigation: a heavy watering that washes salts below the roots (and some nitrate with them).</summary>
    public static void Leach(SoilState s)
    {
        s.ec = Mathf.Max(0.2f, s.ec * 0.55f);
        s.n *= 0.75f;
        s.waterlog = Mathf.Min(1f, s.waterlog + 0.3f);
    }

    /// <summary>Harvest: the crop takes nutrients with it (in proportion to the yield), legumes leave nitrogen, straw stays on the field,
    /// machines compact the soil (more when it is wet). Clears the crop's history.</summary>
    public static void Harvest(SoilState s, SoilCropDef c, float yieldFrac, bool machine, float water, bool regrow)
    {
        float y = Mathf.Clamp(yieldFrac, 0.2f, 1.2f);
        s.n = Mathf.Max(0f, s.n - c.rn * y + c.fix);
        s.p = Mathf.Max(0f, s.p - c.rp * y);
        s.k = Mathf.Max(0f, s.k - c.rk * y);
        s.compaction = Mathf.Min(1f, s.compaction + (machine ? 0.06f : 0.01f) * (water > 0.75f ? 2f : 1f));
        if (!regrow) { s.residue = Mathf.Clamp01(s.residue + c.residue); s.weedProtect = false; }
        ClearCycle(s);
    }

    public static void ClearCycle(SoilState s)
    {
        s.lastGrowth = -1f; s.accG = s.accN = s.accP = s.accK = s.accPh = s.accEc = s.accComp = 0f;
        s.stressW = s.stressLog = s.stressHeat = 0f;
        s.cycN = s.cycP = s.cycK = s.cycWater = s.cycCost = 0f;
        s.appliedMask = 0;
    }
}
