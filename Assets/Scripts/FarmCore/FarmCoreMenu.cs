using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FarmCore data for the farm menu's Map and Calendar tabs (FarmMenuData.Fields / FarmMenuData.Calendar): one row per
/// FarmCore field (number, state, main crop, growth, area, centre) and per-crop sow / harvest seasons from the FarmCore
/// sowing windows and growth days.
/// </summary>
public class FarmCoreFields : IFieldInfoSource
{
    public int Count => FarmFields.Instance != null ? FarmFields.Instance.Fields.Count : 0;

    public FieldInfoRow Row(int i)
    {
        var row = new FieldInfoRow { number = i + 1 };
        var ff = FarmFields.Instance;
        if (ff == null || i < 0 || i >= ff.Fields.Count) return row;
        var f = ff.Fields[i];
        row.number = Number(f, i + 1);
        var s = FarmSim.Summarize(f);
        row.state = State(s);
        row.growth = s.Planted ? s.growth : 0f;
        row.crop = s.Planted ? FcText.CropName(s.mainCrop) : "";
        row.areaM2 = f.Area;
        row.position = f.center;
        return row;
    }

    /// <summary>The number in the field's name ("Field 3" = 3), else the fallback.</summary>
    public static int Number(FarmField f, int fallback)
    {
        if (f == null || string.IsNullOrEmpty(f.name)) return fallback;
        int n = 0; bool any = false;
        foreach (char c in f.name) if (c >= '0' && c <= '9') { n = n * 10 + (c - '0'); any = true; }
        return any ? n : fallback;
    }

    /// <summary>Loc key of the field's main state.</summary>
    public static string State(FieldSummary s)
    {
        if (s.cells == 0) return "";
        if (s.Planted)
        {
            if (s.withered * 2 > s.sown) return "Withered";
            return s.RipeShare >= 0.5f ? "Ready" : "Growing";
        }
        if (s.stubble * 2 > s.cells) return "Stubble";
        if (s.ploughed + s.cultivated > 0 && s.ploughed + s.cultivated >= s.untilled) return "Plowed";
        return "Untilled";
    }
}

public class FarmCoreCalendar : ICropCalendarSource
{
    public List<CropCalendarRow> Rows()
    {
        var list = new List<CropCalendarRow>();
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Data.fieldCrops == null) return list;
        var clock = WorldClock.Instance;
        int perSeason = clock != null ? Mathf.Max(1, clock.daysPerSeason) : 5;
        foreach (var c in eco.Data.fieldCrops)
        {
            if (c.regions != null && c.regions.Length > 0 && Regions.Current != null && System.Array.IndexOf(c.regions, Regions.Current.id) < 0) continue;
            var def = FcData.Crop(c.id);
            var row = new CropCalendarRow { name = FcText.CropName(c.id) };
            int shift = def != null ? Mathf.Clamp(Mathf.FloorToInt(def.growDays / perSeason), 0, 3) : 0;
            for (int s = 0; s < 4; s++)
            {
                if (!FcData.InSowingWindow(c.id, (Season)s)) continue;
                row.sow[s] = true;
                row.harvest[(s + shift) % 4] = true;
            }
            list.Add(row);
        }
        return list;
    }
}
