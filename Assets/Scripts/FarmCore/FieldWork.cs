using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a tool does to the ground (tractor implements and hand tools).</summary>
public enum FieldTool { None, Cultivator, Plough, Seeder, Spreader, Sprayer, Roller, Mulcher, Harvester, Mower, Baler, Hoe, HandSow, HandHarvest, WateringCan }

/// <summary>Settings and results of one piece of field work (one frame of a tractor pass, or one hand job).</summary>
public class WorkContext
{
    public FieldTool tool;
    public bool byHand, machine = true, hot;
    /// <summary>Crop to sow (Seeder / HandSow).</summary>
    public string crop = "";
    /// <summary>Product to spread or spray (amendment id).</summary>
    public string product = "";
    public int density = 1;
    public bool plowingRequired = true;
    /// <summary>Water per cell (WateringCan), mm.</summary>
    public float waterMm = 8f;
    /// <summary>Returns true when there is seed for this many m2 of the crop (takes it).</summary>
    public Func<string, float, bool> takeSeed;
    /// <summary>Returns true when there is product for this many m2 (takes it).</summary>
    public Func<string, float, bool> takeProduct;
    /// <summary>Room left for harvested goods (kg); harvesting stops when it runs out (combine tank full).</summary>
    public float capacityKg = float.MaxValue;
    public int maxCells = int.MaxValue;

    // ---- results
    public int cells;
    public string blocked;              // English Loc key: why nothing could be done (no seed, tank full...)
    public readonly Dictionary<string, float> harvested = new Dictionary<string, float>();
    public readonly Dictionary<string, int> sown = new Dictionary<string, int>();
    public float baledKg, feed;
    public string firstCropPlanted;
    /// <summary>A pesticide pass treated the field's pest.</summary>
    public bool pestCured;

    public float HarvestedKg { get { float t = 0f; foreach (var v in harvested.Values) t += v; return t; } }
}

/// <summary>
/// Field work painted cell by cell: a tool of a given working width swept from point A to point B (the implement's path since the
/// last frame) changes every cell whose centre lies in the swept strip. Each tool has its own effect on the soil (see ApplyCell),
/// so a whole field is worked by driving over it, like in Farming Simulator.
/// </summary>
public static class FieldWork
{
    /// <summary>Cells of a field whose centre lies within the strip swept from a to b (width across the travel direction).</summary>
    public static void CellsInStrip(FarmField f, Vector3 a, Vector3 b, float width, List<int> into)
    {
        Vector3 la = f.ToLocal(a), lb = f.ToLocal(b);
        la.y = lb.y = 0f;
        Vector3 d = lb - la;
        float len = d.magnitude;
        Vector3 dir = len > 1e-4f ? d / len : Vector3.forward;
        float half = Mathf.Max(0.05f, width * 0.5f);
        // a standing tool still works the strip it covers (half a cell along the travel direction)
        float along0 = len > 1e-4f ? 0f : -f.cellSize * 0.5f, along1 = len > 1e-4f ? len : f.cellSize * 0.5f;
        float pad = half + f.cellSize;
        float minX = Mathf.Min(la.x, lb.x) - pad, maxX = Mathf.Max(la.x, lb.x) + pad, minZ = Mathf.Min(la.z, lb.z) - pad, maxZ = Mathf.Max(la.z, lb.z) + pad;
        int cx0 = Mathf.Max(0, Mathf.FloorToInt((minX + f.width * 0.5f) / f.cellSize)), cx1 = Mathf.Min(f.cols - 1, Mathf.FloorToInt((maxX + f.width * 0.5f) / f.cellSize));
        int cz0 = Mathf.Max(0, Mathf.FloorToInt((minZ + f.depth * 0.5f) / f.cellSize)), cz1 = Mathf.Min(f.rows - 1, Mathf.FloorToInt((maxZ + f.depth * 0.5f) / f.cellSize));
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                float px = (cx + 0.5f) * f.cellSize - f.width * 0.5f, pz = (cz + 0.5f) * f.cellSize - f.depth * 0.5f;
                float rx = px - la.x, rz = pz - la.z;
                float along = rx * dir.x + rz * dir.z;
                float across = Mathf.Abs(rx * dir.z - rz * dir.x);
                if (along < along0 || along > along1 || across > half) continue;
                into.Add(cz * f.cols + cx);
            }
    }

    private static readonly List<int> scratch = new List<int>();

    /// <summary>Work the strip from a to b on one field. Returns the number of cells changed.</summary>
    public static int Paint(FarmField f, Vector3 a, Vector3 b, float width, WorkContext ctx)
    {
        scratch.Clear();
        CellsInStrip(f, a, b, width, scratch);
        int changed = 0;
        foreach (int i in scratch)
        {
            if (ctx.cells >= ctx.maxCells) break;
            if (ApplyCell(f, i, ctx)) { changed++; ctx.cells++; f.Touch(i); }
            else if (ctx.blocked != null && (ctx.tool == FieldTool.Seeder || ctx.tool == FieldTool.HandSow || ctx.tool == FieldTool.Spreader || ctx.tool == FieldTool.Sprayer || ctx.tool == FieldTool.Harvester)) break;
        }
        return changed;
    }

    private static bool Sowable(FarmField f, int i, WorkContext ctx)
    {
        var g = (Ground)f.ground[i];
        if (g == Ground.Cultivated || g == Ground.Ploughed) return true;
        // direct drilling into stubble or untilled land only when ploughing isn't required (Settings > Game)
        return !ctx.plowingRequired && (g == Ground.Untilled || g == Ground.Stubble);
    }

    private static void ClearCrop(FarmField f, int i)
    {
        f.crop[i] = 0;
        f.Set(FieldLayer.Growth, i, 0f);
        f.Set(FieldLayer.Yield, i, 1f);
        f.SetFlag(i, CellFlags.Withered, false);
    }

    /// <summary>The tool's effect on one cell. True when the cell changed.</summary>
    public static bool ApplyCell(FarmField f, int i, WorkContext ctx)
    {
        var g = (Ground)f.ground[i];
        float area = f.CellArea;
        switch (ctx.tool)
        {
            case FieldTool.Cultivator:
            case FieldTool.Hoe:
            {
                // shallow tillage: stubble, untilled land, a withered or unwanted crop (a cultivator destroys what grows)
                if (g == Ground.Cultivated) return false;
                if (g == Ground.Sown && ctx.byHand && !f.Has(i, CellFlags.Withered)) return false;   // the hoe never destroys a crop
                ClearCrop(f, i);
                FarmAgronomy.Till(f, i, false, ctx.hot);
                f.ground[i] = (byte)Ground.Cultivated;
                return true;
            }
            case FieldTool.Plough:
            {
                if (g == Ground.Ploughed) return false;
                ClearCrop(f, i);
                FarmAgronomy.Till(f, i, true, ctx.hot);
                f.ground[i] = (byte)Ground.Ploughed;
                // ploughing turns up stones now and then (roll them in afterwards)
                if (f.stones[i] == 0 && f.Hash01(i, 97 + f.cuts + (int)(f.om * 10f)) < 0.08f) f.stones[i] = 1;
                return true;
            }
            case FieldTool.Seeder:
            case FieldTool.HandSow:
            {
                if (!Sowable(f, i, ctx) || string.IsNullOrEmpty(ctx.crop)) return false;
                if (ctx.takeSeed != null && !ctx.takeSeed(ctx.crop, area * FarmAgronomy.DensitySeed[Mathf.Clamp(ctx.density, 0, 2)])) { ctx.blocked = "No seeds - buy them at a Farm Shop"; return false; }
                f.ground[i] = (byte)Ground.Sown;
                f.crop[i] = FcData.CropIndex(ctx.crop);
                f.Set(FieldLayer.Growth, i, 0f);
                f.Set(FieldLayer.Yield, i, 1f);
                f.SetFlag(i, CellFlags.Withered, false);
                f.SetFlag(i, CellFlags.Fertilised, false);
                int s; ctx.sown.TryGetValue(ctx.crop, out s); ctx.sown[ctx.crop] = s + 1;
                if (ctx.firstCropPlanted == null) ctx.firstCropPlanted = ctx.crop;
                return true;
            }
            case FieldTool.Spreader:
            case FieldTool.Sprayer:
            {
                var a = FcData.Amendment(ctx.product);
                if (a == null) { ctx.blocked = "Choose a product on the Fields page"; return false; }
                if (ctx.tool == FieldTool.Sprayer ? !a.spray : !a.spread) { ctx.blocked = "This product doesn't fit this machine"; return false; }
                if (a.pesticide) return SprayPest(f, i, a, ctx);
                if (a.herbicide && f.Has(i, CellFlags.WeedFree) && f.Get(FieldLayer.Weeds, i) < 0.01f) return false;
                if (a.mulch && f.Has(i, CellFlags.Mulched)) return false;
                if (!a.herbicide && !a.mulch && f.Has(i, CellFlags.Fertilised) && (a.n > 0f || a.p > 0f || a.k > 0f)) return false;   // one dose per crop cycle
                if (g == Ground.Sown && !a.growing) return false;   // only top dressings go on a growing crop
                if (ctx.takeProduct != null && !ctx.takeProduct(a.id, area)) { ctx.blocked = "The machine is empty - buy more at the Farm Shop"; return false; }
                FarmAgronomy.Apply(f, i, a);
                if (a.herbicide) f.weedFreeDays = Mathf.Max(f.weedFreeDays, 4);
                return true;
            }
            case FieldTool.Roller:
            {
                if (f.Has(i, CellFlags.Rolled) && f.stones[i] == 0) return false;
                if (g == Ground.Sown && f.Get(FieldLayer.Growth, i) > 0.3f) return false;   // only young crops are rolled
                f.SetFlag(i, CellFlags.Rolled, true);
                f.stones[i] = 0;
                f.Set(FieldLayer.Compaction, i, Mathf.Min(1f, f.Get(FieldLayer.Compaction, i) + 0.02f));
                return true;
            }
            case FieldTool.Mulcher:
            {
                bool witheredCrop = g == Ground.Sown && f.Has(i, CellFlags.Withered);
                if (!(g == Ground.Stubble || witheredCrop) || f.Has(i, CellFlags.Mulched)) return false;
                if (witheredCrop) { ClearCrop(f, i); f.ground[i] = (byte)Ground.Stubble; }
                f.SetFlag(i, CellFlags.Mulched, true);
                f.SetFlag(i, CellFlags.Residue, false);
                f.Set(FieldLayer.Weeds, i, f.Get(FieldLayer.Weeds, i) * 0.5f);
                return true;
            }
            case FieldTool.Harvester:
            case FieldTool.HandHarvest:
            {
                if (!FarmSim.Ripe(f, i)) return false;
                var c = FarmSim.CropOf(f.crop[i]);
                if (c == null) return false;
                if (c.forage && ctx.tool == FieldTool.Harvester) { ctx.blocked = "Cut this crop with the mower"; return false; }
                float kg = FarmSim.CellKg(f, i, c);
                if (kg > ctx.capacityKg) { ctx.blocked = "The harvester tank is full - unload it at the loading bay"; return false; }
                ctx.capacityKg -= kg;
                float yp = Mathf.Clamp01(f.Get(FieldLayer.Yield, i));
                FarmAgronomy.HarvestCell(f, i, c, yp, !ctx.byHand);
                Add(ctx.harvested, c.id, kg);
                if (c.feedPerKg > 0f) ctx.feed += kg * c.feedPerKg;
                AfterCut(f, i, c);
                return true;
            }
            case FieldTool.Mower:
            {
                if (!FarmSim.Ripe(f, i)) return false;
                var c = FarmSim.CropOf(f.crop[i]);
                if (c == null || !c.forage) return false;
                float kg = FarmSim.CellKg(f, i, c);
                FarmAgronomy.HarvestCell(f, i, c, Mathf.Clamp01(f.Get(FieldLayer.Yield, i)), true);
                if (f.swathCrop != c.id) { f.swathKg = 0f; f.swathCrop = c.id; }
                f.swathKg += kg;
                f.SetFlag(i, CellFlags.Swath, true);
                AfterCut(f, i, c);
                return true;
            }
            case FieldTool.Baler:
            {
                if (!f.Has(i, CellFlags.Swath)) return false;
                int swathCells = 0;
                for (int k = 0; k < f.Cells; k++) if (f.Has(k, CellFlags.Swath)) swathCells++;
                float kg = swathCells > 0 ? f.swathKg / swathCells : 0f;
                f.swathKg = Mathf.Max(0f, f.swathKg - kg);
                f.SetFlag(i, CellFlags.Swath, false);
                if (kg > 0f && !string.IsNullOrEmpty(f.swathCrop))
                {
                    Add(ctx.harvested, f.swathCrop, kg);
                    var c = FcData.Crop(f.swathCrop);
                    if (c != null && c.feedPerKg > 0f) ctx.feed += kg * c.feedPerKg;
                    ctx.baledKg += kg;
                }
                return true;
            }
            case FieldTool.WateringCan:
            {
                if (g != Ground.Sown && g != Ground.Cultivated && g != Ground.Ploughed) return false;
                float cap = Mathf.Max(10f, FcData.CurrentRegion.capacityMm);
                float m = f.Get(FieldLayer.Moisture, i);
                if (m >= 1f) return false;
                f.Set(FieldLayer.Moisture, i, Mathf.Min(1.05f, m + ctx.waterMm / cap));
                return true;
            }
        }
        return false;
    }

    /// <summary>After a harvest cut: multi-cut forage regrows from part-grown, everything else leaves stubble and straw.</summary>
    private static void AfterCut(FarmField f, int i, FcCropDef c)
    {
        if (c.cuts > 1 && f.cuts + 1 < c.cuts)
        {
            f.Set(FieldLayer.Growth, i, Mathf.Clamp01(c.regrowFrom) * 0.999f);
            return;
        }
        ClearCrop(f, i);
        f.ground[i] = (byte)Ground.Stubble;
        f.SetFlag(i, CellFlags.Fertilised, false);
        f.SetFlag(i, CellFlags.Rolled, false);
        if (c.residue > 0.3f) f.SetFlag(i, CellFlags.Residue, true);
    }

    /// <summary>Pesticide on a field with a pest: counts the sprayed crop; the field is treated once half its crop got a dose.</summary>
    private static bool SprayPest(FarmField f, int i, FcAmendDef a, WorkContext ctx)
    {
        if (string.IsNullOrEmpty(f.pest) || f.ground[i] != (byte)Ground.Sown) return false;
        if (ctx.takeProduct != null && !ctx.takeProduct(a.id, f.CellArea)) { ctx.blocked = "The machine is empty - buy more at the Farm Shop"; return false; }
        int sown = 0;
        for (int k = 0; k < f.Cells; k++) if (f.ground[k] == (byte)Ground.Sown) sown++;
        f.pestSprayedCells++;
        if (f.pestSprayedCells >= Mathf.Max(1, (sown + 1) / 2)) ctx.pestCured = true;
        return true;
    }

    private static void Add(Dictionary<string, float> d, string k, float v) { float x; d.TryGetValue(k, out x); d[k] = x + v; }
}
