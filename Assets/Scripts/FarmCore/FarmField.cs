using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>What the soil surface of a cell looks like (field work changes it).</summary>
public enum Ground : byte { Untilled = 0, Cultivated = 1, Ploughed = 2, Sown = 3, Stubble = 4 }

/// <summary>Per-cell flags (one byte).</summary>
[Flags] public enum CellFlags : byte { None = 0, Rolled = 1, Mulched = 2, Withered = 4, WeedFree = 8, Fertilised = 16, PoorSeedbed = 32, Residue = 64, Swath = 128 }

/// <summary>Per-cell layers stored as ushort arrays (fixed-point, see FarmField.Scale).</summary>
public enum FieldLayer { Growth, N, P, K, Ph, Weeds, Moisture, Salinity, Compaction, Yield, Count }

/// <summary>
/// One field: a rectangle in the world (centre, yaw, width x depth) divided into square cells (~2 m). Every cell keeps its own
/// ground state, crop, growth, nutrients, pH, weeds, stones, moisture, salinity, compaction and yield potential in compact
/// byte / ushort arrays (one array per layer), so a 20 ha farm is ~1 MB. Saved per layer as run-length encoded base64.
/// Pure logic (no scene access); FarmFields owns the fields and FarmFieldObject is the scene object of one field.
/// </summary>
public class FarmField
{
    public const float DefaultCell = 2f;
    public const int ChunkCells = 16;   // visual / sync chunk edge in cells

    public readonly int id;
    public string name;
    /// <summary>"farm", "office" or "world:&lt;parcel id&gt;".</summary>
    public string source = "farm";
    public Vector3 center;
    public float yaw;
    public readonly float width, depth, cellSize;
    public readonly int cols, rows;
    public int Cells => cols * rows;

    // ---- per-cell layers
    public readonly byte[] ground, crop, flags, stones;
    public readonly ushort[][] layers = new ushort[(int)FieldLayer.Count][];

    public static readonly float[] Scale = { 1f / 20000f, 0.01f, 0.01f, 0.01f, 0.001f, 1f / 60000f, 1f / 40000f, 0.001f, 1f / 60000f, 1f / 60000f };

    // ---- per-field state
    public float om = 1f;
    public string lastCrop = "", history = "", pest = "", swathCrop = "", plantedCrop = "";
    public int sampledDay = -1000, protectDays, weedFreeDays, cuts, density = 1;
    public float pestDamage, rotation = 1f, cycleWaterM3, cycleCost, swathKg;
    /// <summary>Cells sprayed against the current pest (not saved).</summary>
    public int pestSprayedCells;

    /// <summary>Scene object of the field (set by FarmFields; null in pure tests).</summary>
    public FarmFieldObject View;

    // ---- change tracking (visuals and co-op deltas)
    public readonly int chunksX, chunksZ;
    public readonly bool[] chunkDirty;
    public int Version { get; private set; }
    private readonly bool[] netDirty;
    private readonly List<int> netDirtyList = new List<int>();
    public bool SaveDirty;

    public FarmField(int id, string name, Vector3 center, float yaw, float width, float depth, float cell = DefaultCell)
    {
        this.id = id; this.name = name; this.center = center; this.yaw = yaw;
        cellSize = Mathf.Max(0.5f, cell);
        cols = Mathf.Max(1, Mathf.RoundToInt(width / cellSize));
        rows = Mathf.Max(1, Mathf.RoundToInt(depth / cellSize));
        this.width = cols * cellSize; this.depth = rows * cellSize;
        int n = cols * rows;
        ground = new byte[n]; crop = new byte[n]; flags = new byte[n]; stones = new byte[n];
        for (int l = 0; l < layers.Length; l++) layers[l] = new ushort[n];
        chunksX = (cols + ChunkCells - 1) / ChunkCells; chunksZ = (rows + ChunkCells - 1) / ChunkCells;
        chunkDirty = new bool[chunksX * chunksZ];
        netDirty = new bool[n];
        for (int i = 0; i < chunkDirty.Length; i++) chunkDirty[i] = true;
    }

    // ------------------------------------------------------------------ geometry

    public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);
    public float Area => width * depth;
    public float Hectares => Area / 10000f;
    public float CellArea => cellSize * cellSize;
    public float CellHa => CellArea / 10000f;

    public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Rotation) * (world - center);

    public bool Contains(Vector3 world, float pad = 0f)
    {
        Vector3 l = ToLocal(world);
        return Mathf.Abs(l.x) <= width * 0.5f + pad && Mathf.Abs(l.z) <= depth * 0.5f + pad;
    }

    /// <summary>Cell under a world point, or -1 outside the field.</summary>
    public int CellAt(Vector3 world)
    {
        Vector3 l = ToLocal(world);
        int cx = Mathf.FloorToInt((l.x + width * 0.5f) / cellSize), cz = Mathf.FloorToInt((l.z + depth * 0.5f) / cellSize);
        if (cx < 0 || cz < 0 || cx >= cols || cz >= rows) return -1;
        return cz * cols + cx;
    }

    public int Index(int cx, int cz) => cz * cols + cx;
    public Vector3 LocalCellCenter(int i) => new Vector3((i % cols + 0.5f) * cellSize - width * 0.5f, 0f, (i / cols + 0.5f) * cellSize - depth * 0.5f);
    public Vector3 CellCenter(int i) => center + Rotation * LocalCellCenter(i);

    /// <summary>World-space axis-aligned bounds (y from the centre, 3 m tall).</summary>
    public Bounds WorldBounds
    {
        get
        {
            var r = Rotation;
            float hx = width * 0.5f, hz = depth * 0.5f;
            var b = new Bounds(center + Vector3.up * 1.5f, new Vector3(0.01f, 3f, 0.01f));
            foreach (var c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) })
            {
                Vector3 p = center + r * c;
                b.Encapsulate(new Vector3(p.x, center.y, p.z)); b.Encapsulate(new Vector3(p.x, center.y + 3f, p.z));
            }
            return b;
        }
    }

    public int ChunkOf(int i) => (i / cols / ChunkCells) * chunksX + (i % cols) / ChunkCells;

    // ------------------------------------------------------------------ layer access

    public float Get(FieldLayer l, int i) => layers[(int)l][i] * Scale[(int)l];
    public void Set(FieldLayer l, int i, float v)
    {
        float raw = v / Scale[(int)l];
        layers[(int)l][i] = (ushort)(raw <= 0f ? 0 : raw >= 65535f ? 65535 : (int)(raw + 0.5f));
    }
    public void Add(FieldLayer l, int i, float dv) => Set(l, i, Get(l, i) + dv);

    public Ground G(int i) => (Ground)ground[i];
    public string CropId(int i) => FcData.CropId(crop[i]);
    public bool Has(int i, CellFlags f) => (flags[i] & (byte)f) != 0;
    public void SetFlag(int i, CellFlags f, bool on) { if (on) flags[i] |= (byte)f; else flags[i] &= (byte)~f; }

    /// <summary>A cell changed: redraw its chunk, send it to co-op clients, save the field.</summary>
    public void Touch(int i)
    {
        chunkDirty[ChunkOf(i)] = true;
        if (!netDirty[i]) { netDirty[i] = true; netDirtyList.Add(i); }
        SaveDirty = true;
        Version++;
    }

    /// <summary>Something changed that the views must pick up (chunks already flagged).</summary>
    public void Bump() => Version++;

    public void TouchAll() { for (int i = 0; i < chunkDirty.Length; i++) chunkDirty[i] = true; SaveDirty = true; Version++; }

    /// <summary>Cells changed since the last call (co-op deltas); clears the list.</summary>
    public List<int> TakeNetDirty()
    {
        var l = new List<int>(netDirtyList);
        foreach (var i in netDirtyList) netDirty[i] = false;
        netDirtyList.Clear();
        return l;
    }

    public int NetDirtyCount => netDirtyList.Count;

    // ------------------------------------------------------------------ fresh soil

    /// <summary>Region's starting soil on every cell (new land, new game). Stones and small variations come from a hash of the cell, so they are stable.</summary>
    public void ResetSoil(FcRegionDef r)
    {
        var s = r != null ? r.start : new FcSoilStart();
        om = s.om; lastCrop = ""; history = ""; pest = ""; pestDamage = 0f; protectDays = 0; weedFreeDays = 0; cuts = 0; rotation = 1f;
        swathKg = 0f; swathCrop = ""; plantedCrop = ""; cycleWaterM3 = 0f; cycleCost = 0f; sampledDay = -1000;
        for (int i = 0; i < Cells; i++)
        {
            float v = Hash01(i, 11) - 0.5f;   // +-5 % patchiness
            ground[i] = (byte)Ground.Untilled; crop[i] = 0; flags[i] = 0;
            stones[i] = (byte)(r != null && Hash01(i, 37) < r.stones ? 1 + (int)(Hash01(i, 53) * 2.99f) : 0);
            Set(FieldLayer.Growth, i, 0f);
            Set(FieldLayer.N, i, s.n * (1f + 0.1f * v));
            Set(FieldLayer.P, i, s.p * (1f - 0.1f * v));
            Set(FieldLayer.K, i, s.k * (1f + 0.08f * v));
            Set(FieldLayer.Ph, i, s.ph + 0.2f * v);
            Set(FieldLayer.Weeds, i, 0.05f + 0.05f * Hash01(i, 71));
            Set(FieldLayer.Moisture, i, 0.45f);
            Set(FieldLayer.Salinity, i, Mathf.Max(0.2f, s.ec * (1f + 0.15f * v)));
            Set(FieldLayer.Compaction, i, Mathf.Clamp01(s.compaction + 0.1f * v));
            Set(FieldLayer.Yield, i, 1f);
        }
        TouchAll();
        for (int i = 0; i < Cells; i++) Touch(i);
    }

    public float Hash01(int i, int salt)
    {
        unchecked
        {
            uint h = (uint)(i * 73856093) ^ (uint)(id * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / 16777216f;
        }
    }

    // ------------------------------------------------------------------ save codec (run-length + base64 per layer)

    public const string Magic = "FC1";

    /// <summary>All cell layers: "FC1;cols;rows;cropTable;ground;crop;flags;stones;layer0..layerN" (each layer RLE + base64).</summary>
    public string EncodeCells()
    {
        var sb = new StringBuilder(256);
        sb.Append(Magic).Append(';').Append(cols).Append(';').Append(rows).Append(';');
        // crop table: local index -> crop id, so a save never depends on the session's crop registry order
        var used = new List<byte>();
        foreach (var c in crop) if (c != 0 && !used.Contains(c)) used.Add(c);
        var local = new byte[crop.Length];
        for (int i = 0; i < crop.Length; i++) local[i] = (byte)(crop[i] == 0 ? 0 : used.IndexOf(crop[i]) + 1);
        for (int k = 0; k < used.Count; k++) { if (k > 0) sb.Append(','); sb.Append(FcData.CropId(used[k])); }
        sb.Append(';').Append(Rle.Bytes(ground)).Append(';').Append(Rle.Bytes(local)).Append(';').Append(Rle.Bytes(flags)).Append(';').Append(Rle.Bytes(stones));
        foreach (var l in layers) sb.Append(';').Append(Rle.Shorts(l));
        return sb.ToString();
    }

    /// <summary>Read cell layers saved by EncodeCells. False (and nothing changed) when the data doesn't fit this field.</summary>
    public bool DecodeCells(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var p = s.Split(';');
        if (p.Length < 8 + layers.Length || p[0] != Magic) return false;
        if (!int.TryParse(p[1], out int c) || !int.TryParse(p[2], out int r) || c != cols || r != rows) return false;
        var table = p[3].Length > 0 ? p[3].Split(',') : new string[0];
        int n = Cells;
        var g = Rle.ReadBytes(p[4], n); var cr = Rle.ReadBytes(p[5], n); var fl = Rle.ReadBytes(p[6], n); var st = Rle.ReadBytes(p[7], n);
        if (g == null || cr == null || fl == null || st == null) return false;
        var ls = new ushort[layers.Length][];
        for (int l = 0; l < layers.Length; l++) { ls[l] = Rle.ReadShorts(p[8 + l], n); if (ls[l] == null) return false; }
        var map = new byte[table.Length + 1];
        for (int k = 0; k < table.Length; k++) map[k + 1] = FcData.CropIndex(table[k]);
        for (int i = 0; i < n; i++)
        {
            ground[i] = g[i]; flags[i] = fl[i]; stones[i] = st[i];
            crop[i] = cr[i] < map.Length ? map[cr[i]] : (byte)0;
        }
        for (int l = 0; l < layers.Length; l++) Array.Copy(ls[l], layers[l], n);
        TouchAll();
        return true;
    }

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Clean(string s) => (s ?? "").Replace("|", "/");

    /// <summary>Per-field values (soil organic matter, rotation, pests, history...).</summary>
    public string EncodeMeta()
    {
        return string.Join("|", new[] { F(om), Clean(lastCrop), sampledDay.ToString(CultureInfo.InvariantCulture), Clean(pest), F(pestDamage), protectDays.ToString(CultureInfo.InvariantCulture),
            weedFreeDays.ToString(CultureInfo.InvariantCulture), cuts.ToString(CultureInfo.InvariantCulture), F(rotation), density.ToString(CultureInfo.InvariantCulture), F(cycleWaterM3), F(cycleCost),
            F(swathKg), Clean(swathCrop), Clean(plantedCrop), (history ?? "").Replace("|", "!") });
    }

    public void DecodeMeta(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        var p = s.Split('|');
        if (p.Length < 16) return;
        float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out om);
        lastCrop = p[1];
        int.TryParse(p[2], out sampledDay);
        pest = p[3];
        float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out pestDamage);
        int.TryParse(p[5], out protectDays);
        int.TryParse(p[6], out weedFreeDays);
        int.TryParse(p[7], out cuts);
        float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out rotation);
        int.TryParse(p[9], out density); density = Mathf.Clamp(density, 0, 2);
        float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out cycleWaterM3);
        float.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out cycleCost);
        float.TryParse(p[12], NumberStyles.Float, CultureInfo.InvariantCulture, out swathKg);
        swathCrop = p[13]; plantedCrop = p[14]; history = p[15].Replace("!", "|");
        if (rotation <= 0f) rotation = 1f;
    }
}

/// <summary>Run-length encoding of cell layers (varint run length + value), base64 text.</summary>
public static class Rle
{
    private static void Varint(List<byte> o, int v) { uint u = (uint)v; while (u >= 0x80) { o.Add((byte)(u | 0x80)); u >>= 7; } o.Add((byte)u); }

    private static int ReadVarint(byte[] b, ref int pos)
    {
        int shift = 0, v = 0;
        while (pos < b.Length)
        {
            byte x = b[pos++];
            v |= (x & 0x7f) << shift;
            if ((x & 0x80) == 0) return v;
            shift += 7;
            if (shift > 28) return -1;
        }
        return -1;
    }

    public static string Bytes(byte[] a)
    {
        var o = new List<byte>(16);
        for (int i = 0; i < a.Length;)
        {
            int j = i + 1;
            while (j < a.Length && a[j] == a[i]) j++;
            Varint(o, j - i); o.Add(a[i]);
            i = j;
        }
        return Convert.ToBase64String(o.ToArray());
    }

    public static string Shorts(ushort[] a)
    {
        var o = new List<byte>(16);
        for (int i = 0; i < a.Length;)
        {
            int j = i + 1;
            while (j < a.Length && a[j] == a[i]) j++;
            Varint(o, j - i); Varint(o, a[i]);
            i = j;
        }
        return Convert.ToBase64String(o.ToArray());
    }

    public static byte[] ReadBytes(string s, int n)
    {
        byte[] b;
        try { b = Convert.FromBase64String(s); } catch (FormatException) { return null; }
        var r = new byte[n]; int pos = 0, at = 0;
        while (pos < b.Length && at < n)
        {
            int run = ReadVarint(b, ref pos);
            if (run <= 0 || pos >= b.Length) return null;
            byte v = b[pos++];
            for (int k = 0; k < run && at < n; k++) r[at++] = v;
        }
        return at == n ? r : null;
    }

    public static ushort[] ReadShorts(string s, int n)
    {
        byte[] b;
        try { b = Convert.FromBase64String(s); } catch (FormatException) { return null; }
        var r = new ushort[n]; int pos = 0, at = 0;
        while (pos < b.Length && at < n)
        {
            int run = ReadVarint(b, ref pos);
            int v = ReadVarint(b, ref pos);
            if (run <= 0 || v < 0 || v > 65535) return null;
            for (int k = 0; k < run && at < n; k++) r[at++] = (ushort)v;
        }
        return at == n ? r : null;
    }
}
