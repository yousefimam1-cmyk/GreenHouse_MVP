using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Co-op for FarmCore (wire format of NetProtocol: text, U+001F between fields). The host simulates the fields and sends:
/// - FieldCells ("L"): "d" = compact deltas of the cells changed since the last send (field id, then per cell: index,
///   ground, crop id, flags, stones and every layer, base64), "s" = a full snapshot of one field (its save string and
///   meta), sent round-robin every few seconds so slowly drifting values (moisture, growth) stay in step;
/// - IrrSync ("N"): the irrigation network, groundwater level and the combine tank.
/// Clients send their field work ("J": tool, path, width, crop, product...), settings ("F") and irrigation edits ("X")
/// to the host, which does them on the shared farm.
/// </summary>
public static class FarmNet
{
    public const string FieldWork = "J", FieldSet = "F", IrrEdit = "X", FieldCells = "L", IrrSync = "N";
    public const float SnapshotEvery = 3f;

    private static int snapshotIndex;
    private static float snapshotTimer;
    private static string lastIrr = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { snapshotIndex = 0; snapshotTimer = 0f; lastIrr = ""; }

    public static string V(Vector3 v) => NetProtocol.F(v.x) + "," + NetProtocol.F(v.y) + "," + NetProtocol.F(v.z);
    public static Vector3 PV(string s)
    {
        var p = (s ?? "").Split(',');
        return p.Length < 3 ? Vector3.zero : new Vector3(NetProtocol.PF(p[0]), NetProtocol.PF(p[1]), NetProtocol.PF(p[2]));
    }

    // ------------------------------------------------------------------ client -> host

    public static void ForwardWork(FieldTool tool, Vector3 a, Vector3 b, float width, bool byHand, bool tractor, WorkContext ctx)
    {
        if (!NetSession.IsClient) return;
        NetSession.Backend.SendToHost(NetProtocol.Make(FieldWork, ((int)tool).ToString(), V(a), V(b), NetProtocol.F(width), byHand ? "1" : "0", tractor ? "1" : "0",
            NetProtocol.Clean(ctx.crop, 40), NetProtocol.Clean(ctx.product, 40), ctx.density.ToString(), NetProtocol.F(ctx.waterMm)), tool != FieldTool.None);
    }

    /// <summary>E on a field on a client: the host does the next hand job at that point (tool None, by hand).</summary>
    public static void ForwardHandJob(Vector3 at)
    {
        if (!NetSession.IsClient) return;
        NetSession.Backend.SendToHost(NetProtocol.Make(FieldWork, "0", V(at), V(at), NetProtocol.F(FarmFields.HandRadius * 2f), "1", "0", "", "", "1", "0"), true);
    }

    public static void ForwardSetting(string key, string value)
    {
        if (NetSession.IsClient) NetSession.Backend.SendToHost(NetProtocol.Make(FieldSet, key, value), true);
    }

    public static void ForwardIrrigation(params string[] fields)
    {
        if (NetSession.IsClient) NetSession.Backend.SendToHost(NetProtocol.Make(IrrEdit, fields), true);
    }

    /// <summary>Host: a client's message for FarmCore. True when handled.</summary>
    public static bool HostReceive(string[] p)
    {
        var ff = FarmFields.Instance;
        string type = p[0];
        if (type == FieldWork)
        {
            if (ff == null) return true;
            var tool = (FieldTool)Mathf.Clamp(NetProtocol.PI(NetProtocol.Field(p, 1)), 0, (int)FieldTool.WateringCan);
            Vector3 a = PV(NetProtocol.Field(p, 2)), b = PV(NetProtocol.Field(p, 3));
            float width = Mathf.Clamp(NetProtocol.PF(NetProtocol.Field(p, 4)), 0.5f, 30f);
            bool byHand = NetProtocol.Field(p, 5) == "1", tractor = NetProtocol.Field(p, 6) == "1";
            if (Vector3.Distance(a, b) > 12f) return true;   // never trust a huge stroke
            var ctx = ff.NewContext(tool, byHand);
            string crop = NetProtocol.Field(p, 7), product = NetProtocol.Field(p, 8);
            if (crop.Length > 0) ctx.crop = crop;
            if (product.Length > 0) ctx.product = product;
            ctx.density = Mathf.Clamp(NetProtocol.PI(NetProtocol.Field(p, 9)), 0, 2);
            float mm = NetProtocol.PF(NetProtocol.Field(p, 10));
            if (mm > 0f) ctx.waterMm = Mathf.Min(mm, 40f);
            if (byHand)
            {
                var f = ff.FieldAt(a, 0.5f);
                if (f != null && tool == FieldTool.None) ff.HandJob(a);   // E: the next job there
                else if (f != null) ff.HandTool(tool, a, ctx.waterMm);
            }
            else ff.Work(tool, a, b, width, false, tractor, ctx);
            return true;
        }
        if (type == FieldSet)
        {
            if (ff != null && NetProtocol.Field(p, 1) == "d") ff.SetDensity(NetProtocol.PI(NetProtocol.Field(p, 2)));
            return true;
        }
        if (type == IrrEdit)
        {
            var irr = IrrigationSystem.Instance;
            if (irr == null) return true;
            string op = NetProtocol.Field(p, 1), why;
            if (op == "add") irr.Place((IrrKind)Mathf.Clamp(NetProtocol.PI(NetProtocol.Field(p, 2)), 0, (int)IrrKind.Flood), PV(NetProtocol.Field(p, 3)), NetProtocol.PF(NetProtocol.Field(p, 4)), out why);
            else if (op == "pipe") irr.Connect(NetProtocol.PI(NetProtocol.Field(p, 2)), NetProtocol.PI(NetProtocol.Field(p, 3)), NetProtocol.PI(NetProtocol.Field(p, 4)), out why);
            else if (op == "del") irr.Remove(NetProtocol.PI(NetProtocol.Field(p, 2)));
            else if (op == "set") irr.Configure(NetProtocol.PI(NetProtocol.Field(p, 2)), (IrrMode)Mathf.Clamp(NetProtocol.PI(NetProtocol.Field(p, 3)), 0, 3), NetProtocol.PI(NetProtocol.Field(p, 4)),
                NetProtocol.PI(NetProtocol.Field(p, 5)), NetProtocol.PF(NetProtocol.Field(p, 6)), (IrrPower)Mathf.Clamp(NetProtocol.PI(NetProtocol.Field(p, 7)), 0, 2));
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ host -> client

    /// <summary>Host: the FarmCore messages to send now (cell deltas every sync, a snapshot of one field every few seconds, the network when it changed).</summary>
    public static List<string> HostMessages(float dt, bool force)
    {
        var list = new List<string>();
        var ff = FarmFields.Instance;
        if (ff == null) return list;
        foreach (var f in ff.Fields)
        {
            var cells = f.TakeNetDirty();
            if (cells.Count > 0) list.Add(EncodeDelta(f, cells));
        }
        snapshotTimer -= dt;
        if ((snapshotTimer <= 0f || force) && ff.Fields.Count > 0)
        {
            snapshotTimer = SnapshotEvery;
            if (force) foreach (var f in ff.Fields) list.Add(EncodeSnapshot(f));
            else list.Add(EncodeSnapshot(ff.Fields[snapshotIndex++ % ff.Fields.Count]));
        }
        var irr = IrrigationSystem.Instance;
        string net = irr != null ? irr.Net.Encode() : "";
        string tank = ff.Tank.crop + "=" + NetProtocol.F(ff.Tank.kg);
        string irrMsg = NetProtocol.Make(IrrSync, net, NetProtocol.F(irr != null ? irr.Net.GroundwaterM : 0f), tank, ff.Density.ToString());
        if (irrMsg != lastIrr || force) { lastIrr = irrMsg; list.Add(irrMsg); }
        return list;
    }

    public static string EncodeSnapshot(FarmField f) => NetProtocol.Make(FieldCells, "s", f.id.ToString(), f.EncodeCells(), f.EncodeMeta());

    /// <summary>Changed cells of a field: per cell index (int), ground, flags, stones (bytes), crop id (table), then every layer (ushort).</summary>
    public static string EncodeDelta(FarmField f, List<int> cells)
    {
        var table = new List<string>();
        var bytes = new List<byte>(cells.Count * 26);
        foreach (int i in cells)
        {
            if (i < 0 || i >= f.Cells) continue;
            bytes.Add((byte)i); bytes.Add((byte)(i >> 8)); bytes.Add((byte)(i >> 16));
            bytes.Add(f.ground[i]); bytes.Add(f.flags[i]); bytes.Add(f.stones[i]);
            string crop = f.CropId(i);
            int t = table.IndexOf(crop);
            if (t < 0) { table.Add(crop); t = table.Count - 1; }
            bytes.Add((byte)t);
            foreach (var l in f.layers) { bytes.Add((byte)l[i]); bytes.Add((byte)(l[i] >> 8)); }
        }
        return NetProtocol.Make(FieldCells, "d", f.id.ToString(), string.Join(",", table.ToArray()), Convert.ToBase64String(bytes.ToArray()));
    }

    /// <summary>Client: apply a FieldCells or IrrSync message. True when handled.</summary>
    public static bool ClientReceive(string[] p)
    {
        var ff = FarmFields.Instance;
        if (p[0] == FieldCells)
        {
            if (ff == null) return true;
            var f = ff.Find(NetProtocol.PI(NetProtocol.Field(p, 2)));
            if (f == null) return true;
            if (NetProtocol.Field(p, 1) == "s") { if (f.DecodeCells(NetProtocol.Field(p, 3))) f.DecodeMeta(NetProtocol.Field(p, 4)); }
            else ApplyDelta(f, NetProtocol.Field(p, 3), NetProtocol.Field(p, 4));
            return true;
        }
        if (p[0] == IrrSync)
        {
            var irr = IrrigationSystem.Instance;
            if (irr != null) irr.ApplyRemote(NetProtocol.Field(p, 1), NetProtocol.PF(NetProtocol.Field(p, 2)));
            if (ff != null)
            {
                string tank = NetProtocol.Field(p, 3);
                int eq = tank.LastIndexOf('=');
                if (eq >= 0) { ff.Tank.crop = tank.Substring(0, eq); ff.Tank.kg = NetProtocol.PF(tank.Substring(eq + 1)); }
                ff.SetDensityLocal(NetProtocol.PI(NetProtocol.Field(p, 4)));
            }
            return true;
        }
        return false;
    }

    public static int ApplyDelta(FarmField f, string tableField, string data)
    {
        byte[] b;
        try { b = Convert.FromBase64String(data ?? ""); } catch (FormatException) { return 0; }
        var table = string.IsNullOrEmpty(tableField) ? new string[0] : tableField.Split(',');
        int rec = 7 + 2 * f.layers.Length, n = 0;
        for (int at = 0; at + rec <= b.Length; at += rec)
        {
            int i = b[at] | (b[at + 1] << 8) | (b[at + 2] << 16);
            if (i < 0 || i >= f.Cells) continue;
            f.ground[i] = b[at + 3]; f.flags[i] = b[at + 4]; f.stones[i] = b[at + 5];
            int t = b[at + 6];
            f.crop[i] = t < table.Length ? FcData.CropIndex(table[t]) : (byte)0;
            for (int l = 0; l < f.layers.Length; l++) f.layers[l][i] = (ushort)(b[at + 7 + 2 * l] | (b[at + 8 + 2 * l] << 8));
            f.chunkDirty[f.ChunkOf(i)] = true;
            n++;
        }
        f.Bump();
        return n;
    }
}
