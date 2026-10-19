using UnityEngine;

/// <summary>Climate and growing conditions for one simulation step (taken from the WorldClock, region data and settings).</summary>
public struct FarmEnv
{
    public FcRegionDef region;
    public Season season;
    public Weather weather;
    /// <summary>Clock hour 0..23.</summary>
    public int hour;
    public float tempC, et0Day, rainMmPerHour, growthSpeed, fieldDry;
    public bool withering, weeds, plowingRequired;

    public bool Daytime => hour >= 7 && hour < 19;

    /// <summary>The farm's conditions now (falls back to spring noon when there is no clock).</summary>
    public static FarmEnv Now()
    {
        var c = WorldClock.Instance;
        var r = FcData.CurrentRegion;
        var e = new FarmEnv
        {
            region = r,
            season = c != null ? c.Season : Season.Spring,
            weather = c != null ? c.Weather : Weather.Sunny,
            hour = c != null ? Mathf.FloorToInt((6f + c.DayProgress * 24f) % 24f) : 12,
            growthSpeed = EconomyManager.GrowthSpeed,
            fieldDry = FarmModifiers.FieldDry,
            withering = GameSettings.Withering,
            weeds = GameSettings.Weeds,
            plowingRequired = GameSettings.PlowingRequired,
        };
        e.tempC = FarmAgronomy.Temperature(r, e.season, e.weather);
        e.et0Day = FarmAgronomy.Et0(r, e.season) * FarmAgronomy.WeatherEt(e.weather);
        e.rainMmPerHour = e.weather == Weather.Rain ? r.rainMm / 6f : 0f;   // a rainy day: ~6 hours of rain
        return e;
    }
}

/// <summary>Yield factors of one cell (1 = no loss); the lowest one is the limiting factor.</summary>
public struct CellFactors
{
    public float n, p, k, ph, salt, compaction, water, waterlog, weeds, stones;
    public float Product => n * p * k * ph * salt * compaction * water * waterlog * weeds * stones;

    public static readonly string[] Names = { "Nitrogen", "Phosphorus", "Potassium", "Soil pH", "Salinity", "Compaction", "Water stress", "Waterlogging", "Weeds", "Stones" };
    public const int Count = 10;
    /// <summary>Factors that only a soil test shows.</summary>
    public static bool NeedsTest(int i) => i <= 5;

    public float Get(int i)
    {
        switch (i)
        {
            case 0: return n; case 1: return p; case 2: return k; case 3: return ph; case 4: return salt; case 5: return compaction;
            case 6: return water; case 7: return waterlog; case 8: return weeds; default: return stones;
        }
    }

    /// <summary>Index of the lowest factor, or -1 when nothing costs more than 3%.</summary>
    public int Limiting
    {
        get
        {
            int best = -1; float low = 0.97f;
            for (int i = 0; i < Count; i++) { float f = Get(i); if (f < low) { low = f; best = i; } }
            return best;
        }
    }

    public static string Advice(int limiting)
    {
        switch (limiting)
        {
            case 0: return "Spread urea or NPK fertilizer";
            case 1: return "Spread superphosphate";
            case 2: return "Spread potassium sulfate";
            case 3: return "Spread agricultural sulfur to lower the pH";
            case 4: return "Spread gypsum and leach the salts; drip builds up less salt than flooding";
            case 5: return "Plow deep with the tractor when the soil is not wet";
            case 6: return "Water more often or install drip irrigation";
            case 7: return "Water less and let the field drain";
            case 8: return "Spray herbicide or plow deep";
            case 9: return "Roll the field to press the stones";
            default: return "";
        }
    }
}

/// <summary>
/// Soil, water and crop agronomy for FarmCore cells. Ported from the old SoilModel (FAO-56 crop coefficients, Maas-Hoffman salt
/// tolerance, FAO-33 yield response to water, cardinal temperatures, nutrient response curves) and reworked for per-cell layers
/// with in-game time steps. Pure functions, tested headless.
/// </summary>
public static class FarmAgronomy
{
    public const float SaltBuffer = 1700f;   // mm of applied water x dS/m that raise the root-zone ECe by 1 dS/m
    public const float WeedDays = 6f;        // in-game days for weeds to cover an untreated field
    public static readonly float[] DensitySeed = { 0.75f, 1f, 1.3f };
    public static readonly string[] DensityNames = { "Low", "Normal", "High" };

    // ------------------------------------------------------------------ climate

    public static int SeasonIndex(Season s) { switch (s) { case Season.Spring: return 0; case Season.Summer: return 1; case Season.Autumn: return 2; default: return 3; } }

    public static float Temperature(FcRegionDef r, Season s, Weather w)
    {
        int i = SeasonIndex(s);
        float t = r != null && r.temp != null && r.temp.Length > i ? r.temp[i] : 25f;
        switch (w) { case Weather.Heatwave: return t + 7f; case Weather.Rain: return t - 4f; case Weather.Cloudy: return t - 2f; default: return t; }
    }

    /// <summary>Reference evapotranspiration mm/day for the region and season (FAO-56 ET0).</summary>
    public static float Et0(FcRegionDef r, Season s)
    {
        int i = SeasonIndex(s);
        return r != null && r.et0 != null && r.et0.Length > i ? r.et0[i] : 6f;
    }

    public static float WeatherEt(Weather w) { switch (w) { case Weather.Heatwave: return 1.35f; case Weather.Cloudy: return 0.8f; case Weather.Rain: return 0.35f; default: return 1f; } }

    private static readonly float[] diurnal = BuildDiurnal();

    private static float[] BuildDiurnal()
    {
        // Evaporative demand through the day: low at night, peak in the early afternoon. Normalised to an average of 1.
        var w = new float[24]; float sum = 0f;
        for (int h = 0; h < 24; h++) { float s = Mathf.Sin((h - 6f) / 12f * Mathf.PI); w[h] = 0.12f + Mathf.Max(0f, s) * 1.6f; sum += w[h]; }
        for (int h = 0; h < 24; h++) w[h] *= 24f / sum;
        return w;
    }

    /// <summary>Share of the daily ET0 that happens in this clock hour, x24 (average 1).</summary>
    public static float Diurnal(int hour) => diurnal[((hour % 24) + 24) % 24];

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
        if (growth >= 1f) return "Ripe";
        if (growth < 0.1f) return "Germination";
        if (growth < 0.5f) return "Vegetative";
        if (growth < 0.75f) return "Flowering";
        return "Ripening";
    }

    /// <summary>0..1 development speed from temperature (trapezoid on the crop's cardinal temperatures).</summary>
    public static float TempFactor(float t, FcCropDef c)
    {
        if (t <= c.tBase) return 0f;
        if (t < c.tOptLo) return (t - c.tBase) / Mathf.Max(1f, c.tOptLo - c.tBase);
        if (t <= c.tOptHi) return 1f;
        if (t <= c.tMax) return 1f - 0.6f * (t - c.tOptHi) / Mathf.Max(1f, c.tMax - c.tOptHi);
        return Mathf.Max(0f, 0.4f - 0.05f * (t - c.tMax));
    }

    // ------------------------------------------------------------------ factors

    public static float Response(float avail, float need) => need <= 0f ? 1f : Mathf.Clamp01(0.35f + 0.65f * avail / need);
    public static float PAvailability(float ph) => Mathf.Clamp(ph > 7.5f ? 1f - 0.3f * (ph - 7.5f) : ph < 5.5f ? 1f - 0.25f * (5.5f - ph) : 1f, 0.4f, 1f);
    public static float PhFactor(float ph, FcCropDef c) => ph < c.phMin ? Mathf.Max(0.3f, 1f - 0.15f * (c.phMin - ph)) : ph > c.phMax ? Mathf.Max(0.3f, 1f - 0.15f * (ph - c.phMax)) : 1f;
    public static float SaltFactor(float ec, FcCropDef c) => Mathf.Clamp01(1f - c.ecS / 100f * Mathf.Max(0f, ec - c.ecT));
    public static float CompactionFactor(float comp, bool plowingRequired) => !plowingRequired ? 1f : 1f - 0.4f * Mathf.Max(0f, comp - 0.25f) / 0.75f;
    public static float WeedFactor(float w) => 1f - 0.5f * Mathf.Max(0f, w - 0.15f) / 0.85f;
    public static float WaterFactor(float moisture, FcCropDef c) => Mathf.Clamp(1f - c.ky * Mathf.Clamp01((c.waterMin - moisture) / 0.25f), 0.2f, 1f);
    public static bool Waterlogged(float moisture) => moisture > 1.03f;

    /// <summary>Yield factors of a crop on one cell right now.</summary>
    public static CellFactors Factors(FarmField f, int i, FcCropDef c, bool plowingRequired = true)
    {
        var r = new CellFactors();
        float ph = f.Get(FieldLayer.Ph, i), m = f.Get(FieldLayer.Moisture, i);
        r.n = Response(f.Get(FieldLayer.N, i), c.n);
        r.p = Response(f.Get(FieldLayer.P, i) * PAvailability(ph), c.p);
        r.k = Response(f.Get(FieldLayer.K, i), c.k);
        r.ph = PhFactor(ph, c);
        r.salt = SaltFactor(f.Get(FieldLayer.Salinity, i), c);
        r.compaction = CompactionFactor(f.Get(FieldLayer.Compaction, i), plowingRequired);
        r.water = WaterFactor(m, c);
        r.waterlog = c.flood || !Waterlogged(m) ? 1f : 0.55f;
        r.weeds = WeedFactor(f.Get(FieldLayer.Weeds, i));
        r.stones = 1f - 0.03f * f.stones[i];
        return r;
    }

    /// <summary>Growth speed multiplier from the soil now (waterlogging, salt, weeds, compaction).</summary>
    public static float SoilGrowth(FarmField f, int i, FcCropDef c)
    {
        float m = 1f, moist = f.Get(FieldLayer.Moisture, i);
        if (Waterlogged(moist) && !c.flood) m *= 0.6f;
        m *= 1f - 0.5f * (1f - SaltFactor(f.Get(FieldLayer.Salinity, i), c));
        m *= 1f - 0.3f * Mathf.Max(0f, f.Get(FieldLayer.Weeds, i) - 0.15f);
        m *= 1f - 0.3f * Mathf.Max(0f, f.Get(FieldLayer.Compaction, i) - 0.25f);
        return Mathf.Clamp(m, 0.2f, 1f);
    }

    /// <summary>Growth speed from soil water: none when wilting, slower below the crop's minimum.</summary>
    public static float WaterGrowth(float moisture, FcCropDef c)
    {
        if (moisture <= 0.05f) return 0f;
        if (moisture >= c.waterMin) return 1f;
        return 0.3f + 0.7f * (moisture - 0.05f) / Mathf.Max(0.05f, c.waterMin - 0.05f);
    }

    // ------------------------------------------------------------------ water

    /// <summary>
    /// Water reaching one cell from irrigation (mm at the roots after losses). Salt arrives with the water (more in heat, where it
    /// evaporates); deep percolation from inefficient methods carries some salt and nitrate below the roots.
    /// </summary>
    public static void Irrigate(FarmField f, int i, float rootMm, float appliedMm, FcEmitterDef e, float ecWater, float heat, float capacityMm)
    {
        if (appliedMm <= 0f) return;
        f.Add(FieldLayer.Moisture, i, rootMm / Mathf.Max(10f, capacityMm));
        float conc = Mathf.Max(0.8f, 1f + 0.5f * (heat - 1f));
        float ec = f.Get(FieldLayer.Salinity, i) + appliedMm * ecWater * e.salt * conc / SaltBuffer;
        float leach = Mathf.Clamp01(e.leach * appliedMm / 800f);
        f.Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, ec * (1f - leach)));
        f.Set(FieldLayer.N, i, f.Get(FieldLayer.N, i) * (1f - leach * 0.2f));
    }

    /// <summary>Leaching irrigation (one heavy watering): washes salts below the roots, some nitrate with them.</summary>
    public static void Leach(FarmField f, int i)
    {
        f.Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, f.Get(FieldLayer.Salinity, i) * 0.55f));
        f.Set(FieldLayer.N, i, f.Get(FieldLayer.N, i) * 0.75f);
        f.Set(FieldLayer.Moisture, i, Mathf.Max(f.Get(FieldLayer.Moisture, i), 1.1f));
    }

    // ------------------------------------------------------------------ field work

    /// <summary>
    /// Tillage of one cell. Deep (plough ~25 cm) breaks compaction, buries weeds and works the straw in; shallow (cultivator / hoe ~10 cm)
    /// leaves more weeds. Working wet soil smears and compacts it; dry soil in the heat leaves a cloddy seedbed.
    /// </summary>
    public static void Till(FarmField f, int i, bool deep, bool hot)
    {
        float comp = f.Get(FieldLayer.Compaction, i), w = f.Get(FieldLayer.Weeds, i), m = f.Get(FieldLayer.Moisture, i);
        comp = Mathf.Max(0f, comp - (deep ? 0.45f : 0.12f));
        f.Set(FieldLayer.Weeds, i, deep ? 0.02f : w * 0.3f);
        if (f.Has(i, CellFlags.Residue) || f.Has(i, CellFlags.Mulched)) { f.om += (deep ? 0.08f : 0.04f) / Mathf.Max(1, f.Cells); }
        f.SetFlag(i, CellFlags.Residue, false); f.SetFlag(i, CellFlags.Mulched, false); f.SetFlag(i, CellFlags.Rolled, false);
        bool poor = false;
        if (m > 0.85f) { comp = Mathf.Min(1f, comp + 0.15f); poor = true; }   // too wet: smeared and compacted
        else if (m < 0.1f && hot) poor = true;                               // too dry: cloddy seedbed
        f.SetFlag(i, CellFlags.PoorSeedbed, poor);
        f.Set(FieldLayer.Compaction, i, comp);
    }

    /// <summary>Spread or spray an amendment on one cell.</summary>
    public static void Apply(FarmField f, int i, FcAmendDef a)
    {
        if (a == null) return;
        if (a.n != 0f) f.Add(FieldLayer.N, i, a.n);
        if (a.p != 0f) f.Add(FieldLayer.P, i, a.p);
        if (a.k != 0f) f.Add(FieldLayer.K, i, a.k);
        if (a.ph != 0f) f.Set(FieldLayer.Ph, i, Mathf.Clamp(f.Get(FieldLayer.Ph, i) + a.ph, 4f, 9.5f));
        if (a.ec != 0f) f.Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, f.Get(FieldLayer.Salinity, i) + a.ec));
        if (a.compaction != 0f) f.Set(FieldLayer.Compaction, i, Mathf.Clamp01(f.Get(FieldLayer.Compaction, i) + a.compaction));
        if (a.om > 0f) f.om += a.om / Mathf.Max(1, f.Cells);
        if (a.herbicide) { f.Set(FieldLayer.Weeds, i, 0f); f.SetFlag(i, CellFlags.WeedFree, true); }
        if (a.mulch) f.SetFlag(i, CellFlags.Mulched, true);
        if (a.n > 0f || a.p > 0f || a.k > 0f) f.SetFlag(i, CellFlags.Fertilised, true);
    }

    /// <summary>Harvest of one cell: the crop takes nutrients with it (in proportion to the yield), legumes leave nitrogen, straw stays,
    /// machines compact the soil (more when it is wet).</summary>
    public static void HarvestCell(FarmField f, int i, FcCropDef c, float yieldFrac, bool machine)
    {
        float y = Mathf.Clamp(yieldFrac, 0.2f, 1.2f);
        f.Set(FieldLayer.N, i, Mathf.Max(0f, f.Get(FieldLayer.N, i) - c.rn * y + c.fix));
        f.Set(FieldLayer.P, i, Mathf.Max(0f, f.Get(FieldLayer.P, i) - c.rp * y));
        f.Set(FieldLayer.K, i, Mathf.Max(0f, f.Get(FieldLayer.K, i) - c.rk * y));
        float wet = f.Get(FieldLayer.Moisture, i) > 0.85f ? 2f : 1f;
        f.Set(FieldLayer.Compaction, i, Mathf.Min(1f, f.Get(FieldLayer.Compaction, i) + (machine ? 0.06f : 0.01f) * wet));
    }
}
