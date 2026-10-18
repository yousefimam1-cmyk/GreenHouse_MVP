using System.Collections.Generic;
using UnityEngine;

/// <summary>Field-wide numbers for the overlay, the Fields page, workers and missions.</summary>
public struct FieldSummary
{
    public int cells, untilled, cultivated, ploughed, sown, stubble, ripe, withered, weedy, dry, wet;
    public string mainCrop;
    public int mainCropCells;
    public float growth, moisture, n, p, k, ph, weeds, ec, compaction, yieldFactor, expectedKg;
    public int limiting;
    public CellFactors factors;
    public bool Planted => sown > 0;
    public float SownShare => cells > 0 ? sown / (float)cells : 0f;
    public float RipeShare => sown > 0 ? ripe / (float)sown : 0f;
}

/// <summary>
/// Per-cell growth model. Every step (a few in-game minutes) each cell loses water to evapotranspiration (ET0 of the region and
/// season x weather x time of day x crop coefficient), gains rain, drains surplus water (leaching salt and nitrate), grows its crop
/// (driven by temperature, water and soil), averages its yield factors over the crop's growth into the yield potential, grows
/// weeds and, with Settings > Withering, kills crops left dry for a day or ripe for too long. Daily: organic matter releases
/// nitrogen, rain washes salt, compaction slowly recovers. Pure logic on FarmField data.
/// </summary>
public static class FarmSim
{
    private static readonly Dictionary<byte, FcCropDef> cropCache = new Dictionary<byte, FcCropDef>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cropCache.Clear();

    public static FcCropDef CropOf(byte index)
    {
        if (index == 0) return null;
        FcCropDef c;
        if (!cropCache.TryGetValue(index, out c)) { c = FcData.Crop(FcData.CropId(index)); cropCache[index] = c; }
        return c;
    }

    public static bool Ripe(FarmField f, int i) => f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0 && !f.Has(i, CellFlags.Withered) && f.Get(FieldLayer.Growth, i) >= 1f;

    /// <summary>Advance one field by some in-game hours. growthMul = pests / events on this field.</summary>
    public static void Step(FarmField f, float hours, FarmEnv env, float growthMul = 1f)
    {
        if (hours <= 0f) return;
        var r = env.region ?? FcData.CurrentRegion;
        float cap = Mathf.Max(10f, r.capacityMm);
        float et0 = env.et0Day / 24f * FarmAgronomy.Diurnal(env.hour) * hours * Mathf.Max(0f, env.fieldDry <= 0f ? 1f : env.fieldDry);
        float rain = env.rainMmPerHour * hours / cap;
        float drainRate = 0.05f * r.drainage * hours;
        float days = hours / 24f;
        float weedRate = env.weeds ? days / FarmAgronomy.WeedDays : 0f;
        float t = env.tempC;
        bool frost = FarmModifiers.FrostCrops.Count > 0;
        int n = f.Cells;
        var moist = f.layers[(int)FieldLayer.Moisture];
        for (int i = 0; i < n; i++)
        {
            byte g0 = f.ground[i];
            FcCropDef c = g0 == (byte)Ground.Sown ? CropOf(f.crop[i]) : null;
            bool withered = (f.flags[i] & (byte)CellFlags.Withered) != 0;
            bool covered = (f.flags[i] & (byte)(CellFlags.Mulched | CellFlags.Residue)) != 0;
            float growth = c != null ? f.Get(FieldLayer.Growth, i) : 0f;

            // ---- water balance
            float m = moist[i] * FieldScaleMoist;
            m += rain;
            float kc = c != null && !withered ? FarmAgronomy.Kc(Mathf.Min(growth, 1f)) : (covered ? 0.15f : 0.25f);
            m -= et0 * kc / cap;
            if (m < 0f) m = 0f;
            if (m > 1f)
            {
                float excess = m - 1f;
                float flood = c != null && c.flood ? 0.4f : 0.1f;   // paddies hold their water (bunds, puddled soil)
                float comp = f.Get(FieldLayer.Compaction, i);
                float drain = Mathf.Min(excess, drainRate * flood * 4f * (1.2f - comp));
                m -= drain;
                if (drain > 0f)
                {
                    float leach = Mathf.Clamp01(drain * cap / 800f);
                    f.Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, f.Get(FieldLayer.Salinity, i) * (1f - 0.5f * leach)));
                    f.Set(FieldLayer.N, i, f.Get(FieldLayer.N, i) * (1f - 0.2f * leach));
                }
            }
            moist[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(m / FieldScaleMoist), 0, 65535);

            // ---- crop
            if (c != null && !withered)
            {
                if (growth < 1f)
                {
                    float speed = days / Mathf.Max(0.2f, c.growDays) * env.growthSpeed * (0.05f + 0.95f * FarmAgronomy.TempFactor(t, c))
                                  * FarmAgronomy.SoilGrowth(f, i, c) * FarmAgronomy.WaterGrowth(m, c) * growthMul;
                    if (frost) speed *= FarmModifiers.FieldGrowth(c.id);
                    float g1 = Mathf.Min(1f, growth + speed);
                    float dg = g1 - growth;
                    if (dg > 0f)
                    {
                        float fNow = FarmAgronomy.Factors(f, i, c, env.plowingRequired).Product;
                        float yp = f.Get(FieldLayer.Yield, i);
                        yp = growth <= 0.0001f ? fNow : (yp * growth + fNow * dg) / g1;
                        float mid = growth + dg * 0.5f;
                        if (mid >= 0.5f && mid < 0.75f && t > c.tMax) yp *= 1f - 1.6f * dg;   // heat at flowering
                        f.Set(FieldLayer.Yield, i, yp);
                        f.Set(FieldLayer.Growth, i, g1);
                        if (StageOf(growth) != StageOf(g1)) f.Touch(i);
                    }
                    if (env.withering && m <= 0.02f)
                    {
                        float yp = f.Get(FieldLayer.Yield, i) - days;   // a whole day bone dry kills the crop
                        f.Set(FieldLayer.Yield, i, Mathf.Max(0f, yp));
                        if (yp <= 0.001f) { f.SetFlag(i, CellFlags.Withered, true); f.Touch(i); }
                    }
                }
                else
                {
                    // ripe: the crop waits for the harvest; with withering on it spoils after its harvest window
                    float g1 = growth + days / Mathf.Max(0.2f, c.growDays);
                    f.Set(FieldLayer.Growth, i, Mathf.Min(3f, g1));
                    if (env.withering && g1 > 1f + c.harvestDays / Mathf.Max(0.2f, c.growDays)) { f.SetFlag(i, CellFlags.Withered, true); f.Touch(i); }
                }
            }

            // ---- weeds (less under a closed canopy, mulch or herbicide; more when wet and warm)
            if (weedRate > 0f)
            {
                float w = f.Get(FieldLayer.Weeds, i);
                if (w < 1f)
                {
                    float wr = (m > 0.3f ? 1.3f : 0.6f) * (t < 10f ? 0.4f : t > 38f ? 0.7f : 1f);
                    if (c != null && growth > 0.4f) wr *= 0.5f;
                    if ((f.flags[i] & (byte)CellFlags.Mulched) != 0) wr *= 0.5f;
                    if ((f.flags[i] & (byte)CellFlags.WeedFree) != 0) wr *= 0.1f;
                    float w1 = Mathf.Min(1f, w + wr * weedRate);
                    f.Set(FieldLayer.Weeds, i, w1);
                    if ((w < 0.3f) != (w1 < 0.3f)) f.Touch(i);
                }
            }
            else if (!env.weeds && f.layers[(int)FieldLayer.Weeds][i] != 0) f.Set(FieldLayer.Weeds, i, 0f);
        }
        f.SaveDirty = true;
    }

    private static readonly float FieldScaleMoist = FarmField.Scale[(int)FieldLayer.Moisture];

    /// <summary>Growth stage 0..4 (seedling, young, grown, ripe) used by the visuals; changes redraw the cell.</summary>
    public static int StageOf(float growth) => growth >= 1f ? 4 : growth < 0.1f ? 0 : growth < 0.4f ? 1 : growth < 0.7f ? 2 : 3;

    /// <summary>Daily soil processes for one field.</summary>
    public static void Daily(FarmField f, FarmEnv env)
    {
        float heat = env.season == Season.Summer || env.weather == Weather.Heatwave ? 1.5f : 1f;
        bool rain = env.weather == Weather.Rain;
        for (int i = 0; i < f.Cells; i++)
        {
            float nn = f.Get(FieldLayer.N, i) + f.om * 4f;
            if (nn > 260f) nn -= (nn - 260f) * 0.3f;
            if (rain) { nn *= 0.97f; f.Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, f.Get(FieldLayer.Salinity, i) * 0.93f)); }
            f.Set(FieldLayer.N, i, nn);
            f.Set(FieldLayer.Compaction, i, Mathf.Max(0f, f.Get(FieldLayer.Compaction, i) - 0.01f));
        }
        f.om = Mathf.Max(0.1f, f.om - 0.004f * heat);
        if (f.weedFreeDays > 0 && --f.weedFreeDays == 0)
            for (int i = 0; i < f.Cells; i++) f.SetFlag(i, CellFlags.WeedFree, false);
        if (f.protectDays > 0) f.protectDays--;
        f.SaveDirty = true;
    }

    // ------------------------------------------------------------------ summaries

    /// <summary>kg one ripe cell gives now (crop potential x yield potential x bonuses x field rotation and pests).</summary>
    public static float CellKg(FarmField f, int i, FcCropDef c, float extraMul = 1f)
    {
        if (c == null) return 0f;
        float kgHa = c.yieldT * 1000f;
        float yp = Mathf.Clamp01(f.Get(FieldLayer.Yield, i));
        float bonus = 1f;
        if (f.Has(i, CellFlags.Rolled)) bonus *= 1.025f;
        if (f.Has(i, CellFlags.Mulched)) bonus *= 1.025f;
        if (f.Has(i, CellFlags.PoorSeedbed)) bonus *= 0.92f;
        float density = f.density == 0 ? 0.9f : f.density == 2 ? 1.06f : 1f;
        return kgHa * f.CellHa * yp * bonus * density * f.rotation * (1f - Mathf.Clamp01(f.pestDamage)) * extraMul;
    }

    public static FieldSummary Summarize(FarmField f, bool plowingRequired = true)
    {
        var s = new FieldSummary { cells = f.Cells, limiting = -1, mainCrop = "" };
        var counts = new Dictionary<byte, int>();
        int n = f.Cells, sownCells = 0;
        float fa = 0f, fn = 0f, fp = 0f, fk = 0f, fph = 0f, fs = 0f, fc = 0f, fw = 0f, fl = 0f, fwd = 0f, fst = 0f;
        for (int i = 0; i < n; i++)
        {
            switch ((Ground)f.ground[i])
            {
                case Ground.Untilled: s.untilled++; break;
                case Ground.Cultivated: s.cultivated++; break;
                case Ground.Ploughed: s.ploughed++; break;
                case Ground.Sown: s.sown++; break;
                default: s.stubble++; break;
            }
            float m = f.Get(FieldLayer.Moisture, i);
            s.moisture += m; s.n += f.Get(FieldLayer.N, i); s.p += f.Get(FieldLayer.P, i); s.k += f.Get(FieldLayer.K, i); s.ph += f.Get(FieldLayer.Ph, i);
            float w = f.Get(FieldLayer.Weeds, i);
            s.weeds += w; s.ec += f.Get(FieldLayer.Salinity, i); s.compaction += f.Get(FieldLayer.Compaction, i);
            if (w > 0.3f) s.weedy++;
            if (m < 0.25f) s.dry++;
            if (m > 1.03f) s.wet++;
            if (f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0)
            {
                int cnt; counts.TryGetValue(f.crop[i], out cnt); counts[f.crop[i]] = cnt + 1;
                if (f.Has(i, CellFlags.Withered)) { s.withered++; continue; }
                var c = CropOf(f.crop[i]);
                float g = f.Get(FieldLayer.Growth, i);
                s.growth += Mathf.Min(1f, g);
                if (g >= 1f) s.ripe++;
                s.yieldFactor += f.Get(FieldLayer.Yield, i);
                s.expectedKg += CellKg(f, i, c);
                if (c != null)
                {
                    var cf = FarmAgronomy.Factors(f, i, c, plowingRequired);
                    fn += cf.n; fp += cf.p; fk += cf.k; fph += cf.ph; fs += cf.salt; fc += cf.compaction; fw += cf.water; fl += cf.waterlog; fwd += cf.weeds; fst += cf.stones; fa += 1f;
                }
                sownCells++;
            }
        }
        if (n > 0) { s.moisture /= n; s.n /= n; s.p /= n; s.k /= n; s.ph /= n; s.weeds /= n; s.ec /= n; s.compaction /= n; }
        int live = sownCells;
        if (live > 0) { s.growth /= live; s.yieldFactor /= live; }
        foreach (var kv in counts) if (kv.Value > s.mainCropCells) { s.mainCropCells = kv.Value; s.mainCrop = FcData.CropId(kv.Key); }
        if (fa > 0f)
        {
            s.factors = new CellFactors { n = fn / fa, p = fp / fa, k = fk / fa, ph = fph / fa, salt = fs / fa, compaction = fc / fa, water = fw / fa, waterlog = fl / fa, weeds = fwd / fa, stones = fst / fa };
            s.limiting = s.factors.Limiting;
        }
        return s;
    }
}
