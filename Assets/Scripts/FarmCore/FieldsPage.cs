using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The Fields page of the farm info screen (I, then 3 / LB RB), on FarmCore: five setting rows (seeder crop, spreader
/// product, sprayer product, sowing density, map layer) and then every field with its ground, crop and stage, soil water,
/// the soil test (when sampled this season), expected yield, limiting factor with advice, pests, rotation, the irrigation
/// serving it, water and cost this crop and the yield history. Up / Down picks a row, Left / Right changes a setting,
/// Enter on a field opens the irrigation planner there (on a setting row it changes the setting too).
/// FarmInfoScreen hosts the page (it calls Text / Input); Q on a field opens it on that field.
/// </summary>
public static class FieldsPage
{
    public const int SettingRows = 5, FieldsShown = 3;
    public static int Row { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Row = 0;

    /// <summary>The Fields page as an object others can call (FarmFieldObject's Q); null without the info screen.</summary>
    public static FieldsPageHandle Instance => FarmInfoScreen.Instance != null ? FieldsPageHandle.One : null;

    public static int RowCount => SettingRows + (FarmFields.Instance != null ? FarmFields.Instance.Fields.Count : 0);

    public static void Move(int dir) => Row = Mathf.Clamp(Row + dir, 0, Mathf.Max(0, RowCount - 1));

    public static void Select(FarmField f)
    {
        var ff = FarmFields.Instance;
        if (ff == null || f == null) return;
        int i = ff.Fields.IndexOf(f);
        if (i >= 0) Row = SettingRows + i;
    }

    public static FarmField SelectedField
    {
        get
        {
            var ff = FarmFields.Instance;
            int i = Row - SettingRows;
            return ff != null && i >= 0 && i < ff.Fields.Count ? ff.Fields[i] : null;
        }
    }

    /// <summary>Left / Right on a setting row.</summary>
    public static bool Adjust(int dir)
    {
        var ff = FarmFields.Instance;
        if (ff == null || dir == 0) return false;
        switch (Row)
        {
            case 0: ff.CycleSowCrop(dir); return true;
            case 1: ff.CycleProduct(false, dir); return true;
            case 2: ff.CycleProduct(true, dir); return true;
            case 3: ff.CycleDensity(dir); return true;
            case 4: ff.CycleMapLayer(dir); return true;
        }
        return false;
    }

    /// <summary>Enter: on a field, open the irrigation planner there; on a setting, next value.</summary>
    public static bool Confirm()
    {
        var f = SelectedField;
        if (f != null) { if (IrrigationPlanner.Instance != null) { if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Close(); IrrigationPlanner.Instance.Open(f); } return true; }
        return Adjust(1);
    }

    private static string Sel(int row, string text) => row == Row ? "<color=#F2A93B>> " + text + "</color>" : "  " + text;

    public static string Text()
    {
        var ff = FarmFields.Instance;
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(FcText.T("Fields")));
        if (ff == null) return sb.ToString();
        Row = Mathf.Clamp(Row, 0, Mathf.Max(0, RowCount - 1));
        string sow = string.IsNullOrEmpty(ff.SowCrop) ? FcText.R("Best seed you own") : FcText.CropName(ff.SowCrop);
        var eco = EconomyManager.Instance;
        int seeds = eco != null && !string.IsNullOrEmpty(ff.CropToSow()) ? eco.Seeds(ff.CropToSow()) : 0;
        sb.Append('\n').Append(Loc.Display(Sel(0, FcText.R("Seeder") + ": " + sow + " (" + seeds + " " + FcText.R("bags") + ", " + FcText.Area(FarmFields.BagM2(ff.CropToSow())) + " " + FcText.R("each") + ")")));
        var spread = FcData.Amendment(ff.SpreadProduct); var spray = FcData.Amendment(ff.SprayProduct);
        sb.Append('\n').Append(Loc.Display(Sel(1, FcText.R("Spreader") + ": " + FcData.AmendName(spread) + " (" + ff.Bags(ff.SpreadProduct) + ")")));
        sb.Append('\n').Append(Loc.Display(Sel(2, FcText.R("Sprayer") + ": " + FcData.AmendName(spray) + " (" + ff.Bags(ff.SprayProduct) + ")")));
        sb.Append('\n').Append(Loc.Display(Sel(3, FcText.R("Sowing density") + ": " + FarmFields.DensityText(ff.Density))));
        sb.Append('\n').Append(Loc.Display(Sel(4, FcText.R("Map layer") + ": " + FcText.R(ff.MapLayer.ToString()))));
        var irr = IrrigationSystem.Instance;
        if (irr != null)
            sb.Append('\n').Append(Loc.Display("  " + FcText.R("Groundwater") + ": " + irr.Net.GroundwaterM.ToString("F1") + " m · " + FcText.R("Irrigation today") + ": " + irr.WaterM3Today.ToString("F0") + " m3, " + irr.CostToday.ToString("F0") + " " + FcText.R("SAR")
                + (FcData.CurrentRegion.canal ? " · " + FcText.R(IrrigationSystem.CanalOpen() ? "Canal open today" : "Canal closed today") : "")));
        int first = Mathf.Clamp(Row - SettingRows - 1, 0, Mathf.Max(0, ff.Fields.Count - FieldsShown));
        for (int k = first; k < Mathf.Min(ff.Fields.Count, first + FieldsShown); k++)
        {
            var f = ff.Fields[k];
            sb.Append("\n\n").Append(Loc.Display(Sel(SettingRows + k, "<b>" + FarmFields.FieldName(f) + "</b> · " + FcText.Area(f.Area))));
            foreach (var line in FieldLines(f)) sb.Append('\n').Append("    ").Append(Loc.Display(line));
        }
        if (ff.Fields.Count > FieldsShown) sb.Append("\n\n").Append(Loc.Display(FcText.R("Fields") + " " + (first + 1) + "-" + Mathf.Min(ff.Fields.Count, first + FieldsShown) + " / " + ff.Fields.Count));
        return sb.ToString();
    }

    /// <summary>The detail lines of one field (translated, not shaped).</summary>
    public static List<string> FieldLines(FarmField f)
    {
        var ff = FarmFields.Instance;
        var s = FarmSim.Summarize(f, GameSettings.PlowingRequired);
        var lines = new List<string>();
        string ground = FcText.R("Untilled") + " " + Pct(s.untilled, s.cells) + " · " + FcText.R("Worked") + " " + Pct(s.cultivated + s.ploughed, s.cells) + " · " + FcText.R("Sown") + " " + Pct(s.sown, s.cells) + " · " + FcText.R("Stubble") + " " + Pct(s.stubble, s.cells);
        lines.Add(ground);
        if (s.Planted)
        {
            var c = FcData.Crop(s.mainCrop);
            string crop = FcText.CropName(s.mainCrop) + " · " + FcText.R(FarmAgronomy.Stage(s.growth)) + " " + Mathf.FloorToInt(s.growth * 100f) + "% · " + FcText.R("Ripe") + " " + Pct(s.ripe, s.sown);
            if (s.withered > 0) crop += " · " + FcText.R("Withered") + " " + Pct(s.withered, s.sown);
            if (c != null && c.cuts > 1) crop += " · " + FcText.R("Cut") + " " + (f.cuts + 1) + "/" + c.cuts;
            if (!FarmFields.InSeasonNow(s.mainCrop)) crop += " · " + FcText.R("Out of season - slow");
            lines.Add(crop);
            lines.Add(FcText.R("Expected yield") + ": " + FieldOverlay.Kg(s.expectedKg) + " (" + Mathf.RoundToInt(s.yieldFactor * 100f) + "%) · " + FcText.R("Rotation") + " " + FarmFields.RotationText(f.rotation)
                      + (f.density != 1 ? " · " + FcText.R("Sowing density") + " " + FcText.R(FarmAgronomy.DensityNames[f.density]) : ""));
        }
        else if (!string.IsNullOrEmpty(f.lastCrop)) lines.Add(FcText.R("Last crop") + ": " + FcText.CropName(f.lastCrop) + " (" + FcText.R(CropCareManager.FamilyName(FcData.Family(f.lastCrop))) + ")");
        lines.Add(FcText.R("Soil water") + " " + FcText.Pct(s.moisture) + (s.dry > 0 ? " · " + FcText.R("Dry") + " " + Pct(s.dry, s.cells) : "") + (s.wet > 0 ? " · " + FcText.R("Waterlogged") + " " + Pct(s.wet, s.cells) : "")
                  + " · " + FcText.R("Weeds") + " " + FcText.Pct(s.weeds));
        if (ff != null && ff.Sampled(f)) lines.Add(FcText.R("Soil test") + ": " + FarmFields.TestLine(s) + " · OM " + f.om.ToString("F1") + "%");
        else lines.Add(FcText.R("No soil test this season - buy a sample kit (Soil) and press E on the field"));
        if (s.limiting >= 0 && (ff == null || ff.Sampled(f) || !CellFactors.NeedsTest(s.limiting)))
            lines.Add(FcText.R("Limiting") + ": " + FcText.R(CellFactors.Names[s.limiting]) + " " + FcText.Pct(s.factors.Get(s.limiting)) + " - " + FcText.R(CellFactors.Advice(s.limiting)));
        if (!string.IsNullOrEmpty(f.pest)) lines.Add(CropsData.Name(CropsData.Pest(f.pest)) + "! " + (FieldPests.OwnedCure(f) == null ? FcText.R("Buy a cure (Crop care)") : FcText.R("[E] Treat")) + " · " + FcText.R("Damage") + " " + FcText.Pct(f.pestDamage));
        lines.Add(FcText.R("Irrigation") + ": " + IrrigationText(f) + " · " + FcText.R("This crop") + ": " + f.cycleWaterM3.ToString("F0") + " m3, " + f.cycleCost.ToString("F0") + " " + FcText.R("SAR"));
        if (!string.IsNullOrEmpty(f.history))
        {
            var recs = f.history.Split(';');
            var last = recs[recs.Length - 1].Split('|');
            if (last.Length >= 4) lines.Add(FcText.R("Last harvest") + ": " + FcText.CropName(last[1]) + " " + last[2] + " " + FcText.R("kg") + " (" + last[3] + "%) " + FcText.R("Day") + " " + last[0] + " · " + FcText.R("Harvests") + ": " + recs.Length);
        }
        return lines;
    }

    /// <summary>The parts of the network that water this field ("by hand" when none).</summary>
    public static string IrrigationText(FarmField f)
    {
        var irr = IrrigationSystem.Instance;
        if (irr == null) return FcText.R("By hand");
        var names = new List<string>();
        foreach (var n in irr.Net.Nodes)
        {
            if (!n.IsEmitter || IrrigationNetwork.Cover(n, f, null) == 0) continue;
            string s = FcText.R(n.kind.ToString()) + " (" + FcText.R(n.mode.ToString()) + (n.running ? ", " + FcText.R("running") : "") + ")";
            if (!names.Contains(s)) names.Add(s);
        }
        return names.Count == 0 ? FcText.R("By hand") : string.Join(", ", names.ToArray());
    }

    private static string Pct(int a, int total) => total > 0 ? Mathf.RoundToInt(100f * a / total) + "%" : "0%";
}

/// <summary>Opening the Fields page from the world (Q on a field).</summary>
public class FieldsPageHandle
{
    public static readonly FieldsPageHandle One = new FieldsPageHandle();
    public void Open(FarmField f)
    {
        FieldsPage.Select(f);
        if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Show(FarmInfoScreen.Page.Fields);
    }
}
