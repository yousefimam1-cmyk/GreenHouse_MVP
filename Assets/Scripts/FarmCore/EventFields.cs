using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What farm events (EventDirector) do to FarmCore fields: canal water and dust storms change soil water, wadi / Nile floods
/// wash out patches of the lowest fields, locusts eat patches of crop. A "patch" is ~6 x 6 m (one old plot), so event data
/// written for plots ("plots": 2, "lossChance") keeps its meaning.
/// </summary>
public static class EventFields
{
    public const float PatchM = 6f;

    private static List<FarmField> Fields => FarmFields.Instance != null ? FarmFields.Instance.Fields : new List<FarmField>();

    /// <summary>Add (or remove, negative) soil water as a share of field capacity. worked = also fields without a crop that were worked. Returns fields touched.</summary>
    public static int WaterAll(float share, bool worked)
    {
        int n = 0;
        foreach (var f in Fields)
        {
            bool any = false;
            for (int i = 0; i < f.Cells; i++)
            {
                var g = (Ground)f.ground[i];
                bool ok = g == Ground.Sown || (worked && (g == Ground.Cultivated || g == Ground.Ploughed));
                if (!ok) continue;
                f.Set(FieldLayer.Moisture, i, Mathf.Clamp(f.Get(FieldLayer.Moisture, i) + share, 0f, 1.05f));
                any = true;
            }
            if (any) { n++; f.TouchAll(); }
        }
        return n;
    }

    /// <summary>Patches (cell indices) of a field, ~6 x 6 m each.</summary>
    public static List<List<int>> Patches(FarmField f)
    {
        var list = new List<List<int>>();
        int step = Mathf.Max(1, Mathf.RoundToInt(PatchM / f.cellSize));
        for (int z = 0; z < f.rows; z += step)
            for (int x = 0; x < f.cols; x += step)
            {
                var p = new List<int>();
                for (int dz = 0; dz < step && z + dz < f.rows; dz++)
                    for (int dx = 0; dx < step && x + dx < f.cols; dx++) p.Add((z + dz) * f.cols + x + dx);
                list.Add(p);
            }
        return list;
    }

    private static bool HasCrop(FarmField f, List<int> patch) { foreach (int i in patch) if (f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0) return true; return false; }

    private static void Wash(FarmField f, List<int> patch, Ground to)
    {
        foreach (int i in patch)
        {
            f.ground[i] = (byte)to; f.crop[i] = 0;
            f.Set(FieldLayer.Growth, i, 0f); f.Set(FieldLayer.Yield, i, 1f);
            f.SetFlag(i, CellFlags.Withered, false);
            if (to == Ground.Untilled) f.Set(FieldLayer.Moisture, i, 1.05f);
            f.Touch(i);
        }
    }

    /// <summary>A flood washes out 'patches' patches, cropped ones first (that is what hurts). Returns the ids of the flooded fields.</summary>
    public static List<int> Flood(int patches, out int flooded, out int lost)
    {
        flooded = 0; lost = 0;
        var ids = new List<int>();
        var all = new List<KeyValuePair<FarmField, List<int>>>();
        foreach (var f in Fields) foreach (var p in Patches(f)) all.Add(new KeyValuePair<FarmField, List<int>>(f, p));
        all.Sort((a, b) => (HasCrop(b.Key, b.Value) ? 1 : 0).CompareTo(HasCrop(a.Key, a.Value) ? 1 : 0));
        for (int k = 0; k < all.Count && flooded < patches; k++)
        {
            var kv = all[k];
            if (HasCrop(kv.Key, kv.Value)) lost++;
            Wash(kv.Key, kv.Value, Ground.Untilled);
            flooded++;
            if (!ids.Contains(kv.Key.id)) ids.Add(kv.Key.id);
        }
        return ids;
    }

    /// <summary>Locusts: every cropped patch is eaten with this chance. Returns patches lost.</summary>
    public static int Destroy(float chance)
    {
        int lost = 0;
        foreach (var f in Fields)
            foreach (var p in Patches(f))
                if (HasCrop(f, p) && Random.value < chance) { Wash(f, p, Ground.Stubble); lost++; }
        return lost;
    }
}
