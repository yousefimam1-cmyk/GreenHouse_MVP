using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Parts of an irrigation network: water sources, storage, junctions, sensors and emitters.</summary>
public enum IrrKind { Well, DeepWell, Canal, Tank, Junction, Sensor, Pivot, Linear, Sprinkler, Drip, Flood }
/// <summary>What drives a source's pump: the farm grid (Utilities), its own solar panels, or a diesel engine.</summary>
public enum IrrPower { Grid, Solar, Diesel }
/// <summary>When an emitter waters: never, always, by its schedule, or automatically from a soil-moisture sensor on its field.</summary>
public enum IrrMode { Off, On, Schedule, Auto }

public class IrrNode
{
    public int id;
    public IrrKind kind;
    public Vector3 pos;
    /// <summary>Field the emitter / sensor belongs to (drip, flood, linear, sensor), or -1.</summary>
    public int field = -1;
    /// <summary>Radius (pivot, sprinkler) or width (linear) in metres.</summary>
    public float size;
    public IrrPower power = IrrPower.Grid;
    public IrrMode mode = IrrMode.Schedule;
    public int startHour = 22, hours = 6;
    public float autoLow = 0.45f, autoHigh = 0.9f;
    /// <summary>Hours per revolution / pass (pivot, linear): slower = bigger dose per pass.</summary>
    public float revHours = 24f;
    // ---- running state
    public float angle, stored;
    public bool running;
    public float demand, delivered, reading = -1f;
    public string shortage = "";

    public bool IsSource => kind == IrrKind.Well || kind == IrrKind.DeepWell || kind == IrrKind.Canal || kind == IrrKind.Tank;
    public bool IsEmitter => kind >= IrrKind.Pivot;
}

public class IrrPipe
{
    public int a, b, diameterMm = 75;
    public float length;
    public float Capacity => FcData.Pipe(diameterMm).capacity;
}

/// <summary>What one network step pumped, used and cost.</summary>
public class IrrReport
{
    public float m3, groundM3, canalM3, tseM3, kwhGrid, kwhSolar, dieselL, cost, gridKw;
    public readonly Dictionary<int, float> fieldM3 = new Dictionary<int, float>();
}

/// <summary>
/// The irrigation network (pure logic, tested headless). Sources (wells limited by the groundwater level, deep wells, canal
/// intakes that only flow on the canal turn, tanks that store surplus) feed emitters (centre pivot, linear move, sprinklers,
/// drip lines, flood / furrow) through pipes laid by the player. Each step:
/// 1. each emitter decides whether it runs (mode: on / schedule / auto from a soil sensor on its field, with hysteresis);
/// 2. per connected network, demand (design flow x area covered) is compared with what the sources can give (pump power:
///    grid / solar by sun / diesel; groundwater; canal turn; tank stock) and every emitter gets the same share, capped by the
///    narrowest pipe on its best path from a source;
/// 3. delivered water lands on the cells under the emitter (pivots and linear moves only under the moving arm), minus
///    spray / surface evaporation (much less at night) and the method's efficiency; salt arrives with it (FarmAgronomy.Irrigate);
/// 4. groundwater falls with what wells pump; energy and water are metered for the bill.
/// </summary>
public class IrrigationNetwork
{
    public readonly List<IrrNode> Nodes = new List<IrrNode>();
    public readonly List<IrrPipe> Pipes = new List<IrrPipe>();
    public int NextId = 1;
    /// <summary>Groundwater depth (m below ground) for the region.</summary>
    public float GroundwaterM;
    public int Version { get; private set; }

    public void Changed() => Version++;

    // ------------------------------------------------------------------ editing

    public IrrNode Add(IrrKind kind, Vector3 pos, int field = -1, float size = -1f)
    {
        var e = FcData.Emitter(kind);
        var n = new IrrNode { id = NextId++, kind = kind, pos = pos, field = field, size = size >= 0f ? size : e.size, revHours = e.revHours > 0f ? e.revHours : 24f };
        if (kind == IrrKind.Pivot || kind == IrrKind.Sprinkler) n.size = Mathf.Clamp(n.size, Mathf.Max(1f, e.minSize), e.maxSize > 0f ? e.maxSize : 999f);
        if (kind == IrrKind.Canal) n.power = IrrPower.Diesel;   // a small lift pump at the canal bank
        Nodes.Add(n);
        Changed();
        return n;
    }

    public IrrNode Node(int id) => Nodes.Find(n => n.id == id);

    public IrrPipe Connect(int a, int b, int diameterMm)
    {
        var na = Node(a); var nb = Node(b);
        if (na == null || nb == null || a == b) return null;
        var old = Pipes.Find(p => (p.a == a && p.b == b) || (p.a == b && p.b == a));
        if (old != null) { old.diameterMm = diameterMm; Changed(); return old; }
        var pipe = new IrrPipe { a = a, b = b, diameterMm = diameterMm, length = Flat(na.pos - nb.pos) };
        Pipes.Add(pipe);
        Changed();
        return pipe;
    }

    public void Remove(int id)
    {
        Nodes.RemoveAll(n => n.id == id);
        Pipes.RemoveAll(p => p.a == id || p.b == id);
        Changed();
    }

    public void Clear() { Nodes.Clear(); Pipes.Clear(); NextId = 1; Changed(); }

    public static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// <summary>Price of a part (SAR): base price plus per-metre for drip lines (field area) and pivot / linear spans.</summary>
    public static int Price(IrrKind kind, float size, float fieldAreaM2)
    {
        if (kind >= IrrKind.Pivot)
        {
            var e = FcData.Emitter(kind);
            float metres = kind == IrrKind.Drip ? fieldAreaM2 / 1.5f : kind == IrrKind.Pivot || kind == IrrKind.Linear ? size : 0f;
            return Mathf.RoundToInt(e.price + e.pricePerM * metres);
        }
        return FcData.Source(kind).price;
    }

    public static int PipePrice(float length, int diameterMm) => Mathf.RoundToInt(length * FcData.Pipe(diameterMm).pricePerM);

    // ------------------------------------------------------------------ topology

    /// <summary>Connected networks (lists of node ids).</summary>
    public List<List<IrrNode>> Components()
    {
        var seen = new HashSet<int>();
        var result = new List<List<IrrNode>>();
        foreach (var start in Nodes)
        {
            if (seen.Contains(start.id)) continue;
            var comp = new List<IrrNode>();
            var queue = new Queue<IrrNode>();
            queue.Enqueue(start); seen.Add(start.id);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue(); comp.Add(n);
                foreach (var p in Pipes)
                {
                    int o = p.a == n.id ? p.b : p.b == n.id ? p.a : -1;
                    if (o < 0 || seen.Contains(o)) continue;
                    var on = Node(o); if (on == null) continue;
                    seen.Add(o); queue.Enqueue(on);
                }
            }
            result.Add(comp);
        }
        return result;
    }

    /// <summary>Widest path capacity (m3/h) from any source to this node (0 = not connected to a source).</summary>
    public float Bottleneck(IrrNode target, List<IrrNode> comp)
    {
        var best = new Dictionary<int, float>();
        var open = new List<IrrNode>();
        foreach (var n in comp) if (n.IsSource) { best[n.id] = float.MaxValue; open.Add(n); }
        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (best[open[i].id] > best[open[bi].id]) bi = i;
            var n = open[bi]; open.RemoveAt(bi);
            if (n == target) return best[n.id];
            foreach (var p in Pipes)
            {
                int o = p.a == n.id ? p.b : p.b == n.id ? p.a : -1;
                if (o < 0) continue;
                float w = Mathf.Min(best[n.id], p.Capacity);
                float cur;
                if (best.TryGetValue(o, out cur) && cur >= w) continue;
                best[o] = w;
                var on = Node(o);
                if (on != null && !open.Contains(on)) open.Add(on);
            }
        }
        return target.IsSource ? float.MaxValue : 0f;
    }

    // ------------------------------------------------------------------ coverage

    /// <summary>Area an emitter waters (m2) on the given fields.</summary>
    public float CoveredM2(IrrNode n, IList<FarmField> fields)
    {
        float m2 = 0f;
        foreach (var f in fields) m2 += Cover(n, f, null) * f.CellArea;
        return m2;
    }

    /// <summary>Cells of a field under an emitter (all of its reach, not just under a moving arm). Returns the count; fills 'into' when given.</summary>
    public static int Cover(IrrNode n, FarmField f, List<int> into)
    {
        int count = 0;
        switch (n.kind)
        {
            case IrrKind.Pivot:
            case IrrKind.Sprinkler:
            {
                if (!f.Contains(n.pos, n.size)) return 0;
                float r2 = n.size * n.size;
                // only the cells in the circle's bounding box (local field frame: distances don't change with the yaw)
                Vector3 l = f.ToLocal(n.pos);
                int cx0 = Mathf.Max(0, Mathf.FloorToInt((l.x - n.size + f.width * 0.5f) / f.cellSize)), cx1 = Mathf.Min(f.cols - 1, Mathf.FloorToInt((l.x + n.size + f.width * 0.5f) / f.cellSize));
                int cz0 = Mathf.Max(0, Mathf.FloorToInt((l.z - n.size + f.depth * 0.5f) / f.cellSize)), cz1 = Mathf.Min(f.rows - 1, Mathf.FloorToInt((l.z + n.size + f.depth * 0.5f) / f.cellSize));
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    float dz = (cz + 0.5f) * f.cellSize - f.depth * 0.5f - l.z;
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        float dx = (cx + 0.5f) * f.cellSize - f.width * 0.5f - l.x;
                        if (dx * dx + dz * dz <= r2) { count++; if (into != null) into.Add(cz * f.cols + cx); }
                    }
                }
                return count;
            }
            case IrrKind.Drip:
            case IrrKind.Flood:
            case IrrKind.Linear:
                if (n.field != f.id) return 0;
                for (int i = 0; i < f.Cells; i++) { count++; if (into != null) into.Add(i); }
                return count;
        }
        return 0;
    }

    /// <summary>Design flow of an emitter (m3/h) for the area it covers.</summary>
    public float DesignFlow(IrrNode n, IList<FarmField> fields) => FcData.Emitter(n.kind).flowHa * CoveredM2(n, fields) / 10000f;

    // ------------------------------------------------------------------ control

    public static bool InSchedule(IrrNode n, int hour)
    {
        int h = ((hour - n.startHour) % 24 + 24) % 24;
        return h < Mathf.Clamp(n.hours, 0, 24);
    }

    /// <summary>Average moisture of the 3 x 3 cells around a sensor (or -1 off the field).</summary>
    public static float Read(IrrNode sensor, FarmField f)
    {
        if (f == null) return -1f;
        int i = f.CellAt(sensor.pos);
        if (i < 0) return -1f;
        int cx = i % f.cols, cz = i / f.cols, n = 0; float sum = 0f;
        for (int z = cz - 1; z <= cz + 1; z++)
            for (int x = cx - 1; x <= cx + 1; x++)
                if (x >= 0 && z >= 0 && x < f.cols && z < f.rows) { sum += f.Get(FieldLayer.Moisture, z * f.cols + x); n++; }
        return n > 0 ? sum / n : -1f;
    }

    /// <summary>Does this emitter want to run now?</summary>
    public bool Wants(IrrNode n, int hour, IList<FarmField> fields)
    {
        switch (n.mode)
        {
            case IrrMode.Off: return false;
            case IrrMode.On: return true;
            case IrrMode.Auto:
            {
                int fieldId = n.field >= 0 ? n.field : FieldUnder(n, fields);
                float reading = -1f;
                foreach (var s in Nodes)
                    if (s.kind == IrrKind.Sensor && s.field == fieldId && s.reading >= 0f) reading = reading < 0f ? s.reading : Mathf.Min(reading, s.reading);
                if (reading < 0f) return InSchedule(n, hour);   // no sensor on the field: fall back to the schedule
                if (n.running) return reading < n.autoHigh;
                return reading < n.autoLow;
            }
            default: return InSchedule(n, hour);
        }
    }

    public static int FieldUnder(IrrNode n, IList<FarmField> fields)
    {
        foreach (var f in fields) if (f.Contains(n.pos)) return f.id;
        foreach (var f in fields) if (f.Contains(n.pos, n.size)) return f.id;
        return -1;
    }

    // ------------------------------------------------------------------ sources

    /// <summary>Pump head (m) the source lifts: groundwater depth for wells, a few metres at a canal bank or tank.</summary>
    public float LiftM(IrrNode s) => s.kind == IrrKind.Well || s.kind == IrrKind.DeepWell ? GroundwaterM : 3f;

    /// <summary>Share of the well's rated flow it still gives at the current groundwater level.</summary>
    public float WellFactor(IrrNode s, FcRegionDef r)
    {
        if (s.kind == IrrKind.Well) return Mathf.Clamp01((r.gwWellMax - GroundwaterM) / 30f);
        if (s.kind == IrrKind.DeepWell) return Mathf.Clamp01((r.gwDeepMax - GroundwaterM) / 50f);
        return 1f;
    }

    /// <summary>What a source can give now (m3/h).</summary>
    public float SourceFlow(IrrNode s, FcRegionDef r, bool canalOpen, float sun, float gridRatio, float headM, float hours)
    {
        var d = FcData.Source(s.kind);
        float q = d.capacity;
        switch (s.kind)
        {
            case IrrKind.Well: case IrrKind.DeepWell: q *= WellFactor(s, r); break;
            case IrrKind.Canal: if (!canalOpen || !r.canal) q = 0f; break;
            case IrrKind.Tank: q = Mathf.Min(q, s.stored / Mathf.Max(0.01f, hours)); return q;   // gravity-fed from its own stock
        }
        if (q <= 0f) return 0f;
        switch (s.power)
        {
            case IrrPower.Solar:
            {
                float kwNeeded = PumpKw(q, LiftM(s) + headM);
                float kw = FcData.Power(IrrPower.Solar).kwp * Mathf.Clamp01(sun);
                if (kwNeeded > 0.01f) q *= Mathf.Clamp01(kw / kwNeeded);
                break;
            }
            case IrrPower.Grid: q *= Mathf.Clamp01(gridRatio); break;
        }
        return q;
    }

    /// <summary>Pump power (kW) for a flow (m3/h) against a head (m): rho g Q H / efficiency.</summary>
    public static float PumpKw(float m3h, float headM) => m3h * Mathf.Max(1f, headM) / 367f / 0.6f;

    // ------------------------------------------------------------------ step

    /// <summary>Run the network for some hours: decide, share, deliver, meter. Returns what was used and what it cost.</summary>
    /// <param name="tseM3h">Treated wastewater line (Utilities) feeding the first network that needs water, m3/h.</param>
    public IrrReport Step(float hours, FarmEnv env, IList<FarmField> fields, bool canalOpen, bool banned, float sun, float gridRatio, float tseM3h = 0f)
    {
        var rep = new IrrReport();
        var r = env.region ?? FcData.CurrentRegion;
        foreach (var n in Nodes)
        {
            n.delivered = 0f; n.demand = 0f; n.shortage = "";
            if (n.kind == IrrKind.Sensor) { var f = Find(fields, n.field >= 0 ? n.field : FieldUnder(n, fields)); n.reading = Read(n, f); }
        }
        foreach (var comp in Components())
        {
            var emitters = new List<IrrNode>(); var sources = new List<IrrNode>();
            foreach (var n in comp) { if (n.IsEmitter) emitters.Add(n); else if (n.IsSource) sources.Add(n); }
            float demand = 0f, head = 0f;
            foreach (var e in emitters)
            {
                bool want = !banned && Wants(e, env.hour, fields);
                e.running = want;
                if (!want) continue;
                e.demand = DesignFlow(e, fields);
                demand += e.demand;
                head = Mathf.Max(head, FcData.Emitter(e.kind).headM);
            }
            // supply: pumps first, tanks cover the gap
            float pumpCap = 0f, tankCap = 0f;
            var caps = new Dictionary<int, float>();
            foreach (var s in sources)
            {
                float q = banned && s.kind != IrrKind.Tank ? 0f : SourceFlow(s, r, canalOpen, sun, gridRatio, head, hours);
                if (banned && s.kind == IrrKind.Tank) q = 0f;
                caps[s.id] = q;
                if (s.kind == IrrKind.Tank) tankCap += q; else pumpCap += q;
            }
            float tse = 0f;
            if (tseM3h > 0f && demand > 0f && !banned && sources.Count > 0) { tse = Mathf.Min(tseM3h, demand); tseM3h -= tse; }
            float served = Mathf.Min(demand, pumpCap + tankCap + tse);
            float ratio = demand > 1e-4f ? served / demand : 0f;
            float used = 0f;
            foreach (var e in emitters)
            {
                if (!e.running || e.demand <= 0f) continue;
                float bottleneck = Bottleneck(e, comp);
                float q = Mathf.Min(e.demand * ratio, bottleneck);
                if (bottleneck <= 0f) e.shortage = "Not connected to a water source";
                else if (q < e.demand * 0.98f) e.shortage = bottleneck < e.demand * ratio ? "Pipe too small" : "Not enough water";
                e.delivered = q;
                used += q * hours;
                Deliver(e, q * hours, hours, env, fields, rep);
            }
            // meter the sources: pumps in proportion to their capacity, tanks for the rest; surplus fills the tanks
            float fromTse = Mathf.Min(used, tse * hours);
            if (fromTse > 0f) { rep.tseM3 += fromTse; rep.m3 += fromTse; rep.cost += fromTse * UtilityModel.TsePrice; used -= fromTse; }
            float fromPumps = Mathf.Min(used, pumpCap * hours), fromTanks = used - fromPumps;
            float tankRoom = 0f;
            foreach (var s in sources) if (s.kind == IrrKind.Tank) tankRoom += Mathf.Max(0f, FcData.Source(IrrKind.Tank).storage - s.stored);
            float refill = Mathf.Min(tankRoom, pumpCap * hours - fromPumps);
            float pumped = fromPumps + Mathf.Max(0f, refill);
            foreach (var s in sources)
            {
                if (s.kind == IrrKind.Tank)
                {
                    float share = tankCap > 0f ? caps[s.id] / tankCap : 0f;
                    s.stored = Mathf.Max(0f, s.stored - fromTanks * share);
                    float room = FcData.Source(IrrKind.Tank).storage - s.stored;
                    if (refill > 0f && tankRoom > 0f) s.stored += refill * room / tankRoom;
                    continue;
                }
                float shareP = pumpCap > 0f ? caps[s.id] / pumpCap : 0f;
                float m3 = pumped * shareP;
                s.delivered = m3 / Mathf.Max(0.01f, hours);
                s.running = m3 > 0.001f;
                if (m3 <= 0f) continue;
                rep.m3 += m3;
                float kwh = PumpKw(m3 / hours, LiftM(s) + head) * hours;
                if (s.kind == IrrKind.Canal) { rep.canalM3 += m3; rep.cost += m3 * r.canalFee; }
                else { rep.groundM3 += m3; rep.cost += m3 * r.waterCost; GroundwaterM += m3 * r.gwDrawPerM3; }
                switch (s.power)
                {
                    case IrrPower.Grid: rep.kwhGrid += kwh; rep.gridKw += kwh / hours; break;
                    case IrrPower.Solar: rep.kwhSolar += kwh; break;
                    default: rep.dieselL += kwh * UtilityModel.DieselPerKwh; rep.cost += kwh * UtilityModel.DieselPerKwh * UtilityModel.DieselPrice; break;
                }
            }
        }
        return rep;
    }

    private static FarmField Find(IList<FarmField> fields, int id) { foreach (var f in fields) if (f.id == id) return f; return null; }

    private static readonly List<int> cells = new List<int>();

    /// <summary>Put m3 of water on the cells under an emitter now (pivot / linear: under the moving arm only).</summary>
    private void Deliver(IrrNode e, float m3, float hours, FarmEnv env, IList<FarmField> fields, IrrReport rep)
    {
        if (m3 <= 0f) return;
        var def = FcData.Emitter(e.kind);
        float a0 = e.angle;
        if (e.kind == IrrKind.Pivot) e.angle = (e.angle + 360f * hours / Mathf.Max(1f, e.revHours)) % 360f;
        if (e.kind == IrrKind.Linear) e.angle = (e.angle + 2f * hours / Mathf.Max(1f, e.revHours)) % 2f;   // 0..1 out, 1..2 back
        float sweep = e.kind == IrrKind.Pivot ? 360f * hours / Mathf.Max(1f, e.revHours) : e.kind == IrrKind.Linear ? 2f * hours / Mathf.Max(1f, e.revHours) : 0f;
        // which cells get water this step
        var hit = new List<KeyValuePair<FarmField, int>>();
        foreach (var f in fields)
        {
            cells.Clear();
            if (Cover(e, f, cells) == 0) continue;
            bool rowsOnly = e.kind == IrrKind.Drip && SownCount(f) > 0;
            foreach (int i in cells)
            {
                if (rowsOnly && f.ground[i] != (byte)Ground.Sown) continue;   // drip lines run along the crop rows
                if (e.kind == IrrKind.Pivot && sweep < 360f && !InSector(f.CellCenter(i) - e.pos, a0, sweep)) continue;
                if (e.kind == IrrKind.Linear && sweep < 2f && !InBand(f, i, a0, sweep)) continue;
                hit.Add(new KeyValuePair<FarmField, int>(f, i));
            }
        }
        if (hit.Count == 0) return;
        float evap = env.Daytime ? def.evapDay : def.evapNight;
        if (env.Daytime && env.weather == Weather.Heatwave) evap *= 1.4f;
        float perCellM3 = m3 / hit.Count;
        float heat = env.weather == Weather.Heatwave || env.season == Season.Summer ? 1.4f : 1f;
        float cap = Mathf.Max(10f, (env.region ?? FcData.CurrentRegion).capacityMm);
        float ecWater = (env.region ?? FcData.CurrentRegion).ecWater;
        foreach (var kv in hit)
        {
            var f = kv.Key; int i = kv.Value;
            float appliedMm = perCellM3 / f.CellArea * 1000f;
            float rootMm = appliedMm * (1f - evap) * def.eff;
            FarmAgronomy.Irrigate(f, i, rootMm, appliedMm, def, ecWater, heat, cap);
            if (def.waterlog && f.Get(FieldLayer.Moisture, i) > 1.15f) f.Set(FieldLayer.Moisture, i, 1.15f);
            else if (!def.waterlog && f.Get(FieldLayer.Moisture, i) > 1.08f) f.Set(FieldLayer.Moisture, i, 1.08f);
            float fm; rep.fieldM3.TryGetValue(f.id, out fm); rep.fieldM3[f.id] = fm + perCellM3;
            f.cycleWaterM3 += perCellM3;
            f.SaveDirty = true;
        }
    }

    private static int SownCount(FarmField f) { int n = 0; for (int i = 0; i < f.Cells; i++) if (f.ground[i] == (byte)Ground.Sown) n++; return n; }

    private static bool InSector(Vector3 d, float from, float sweep)
    {
        float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;
        float rel = ((ang - from) % 360f + 360f) % 360f;
        return rel <= sweep + 2f;   // a little overlap so no cell falls between two steps
    }

    private static bool InBand(FarmField f, int i, float from, float sweep)
    {
        float x = (i % f.cols + 0.5f) / f.cols;   // 0..1 across the field
        float a = from, b = from + sweep;
        for (int k = 0; k < 2; k++)
        {
            float pos = k == 0 ? x : 2f - x;   // out (0..1) then back (1..2)
            if (pos >= a - 0.02f && pos <= b + 0.02f) return true;
            if (b > 2f && pos + 2f >= a && pos + 2f <= b + 0.02f) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ groundwater

    /// <summary>Daily aquifer recharge (more on rainy days); never above the region's natural level.</summary>
    public void Recharge(FcRegionDef r, bool rain)
    {
        GroundwaterM = Mathf.Max(r.gwStart, GroundwaterM - r.gwRecharge * (rain ? 5f : 1f));
    }

    // ------------------------------------------------------------------ save

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static float PF(string s) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f; }
    private static int PI(string s) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }

    /// <summary>"next;node|node...;pipe|pipe..." (nodes: id,kind,x,y,z,field,size,power,mode,start,hours,low,high,rev,stored).</summary>
    public string Encode()
    {
        var sb = new StringBuilder();
        sb.Append(NextId).Append(';');
        for (int k = 0; k < Nodes.Count; k++)
        {
            var n = Nodes[k];
            if (k > 0) sb.Append('|');
            sb.Append(n.id).Append(',').Append((int)n.kind).Append(',').Append(F(n.pos.x)).Append(',').Append(F(n.pos.y)).Append(',').Append(F(n.pos.z)).Append(',')
              .Append(n.field).Append(',').Append(F(n.size)).Append(',').Append((int)n.power).Append(',').Append((int)n.mode).Append(',').Append(n.startHour).Append(',')
              .Append(n.hours).Append(',').Append(F(n.autoLow)).Append(',').Append(F(n.autoHigh)).Append(',').Append(F(n.revHours)).Append(',').Append(F(n.stored));
        }
        sb.Append(';');
        for (int k = 0; k < Pipes.Count; k++)
        {
            var p = Pipes[k];
            if (k > 0) sb.Append('|');
            sb.Append(p.a).Append(',').Append(p.b).Append(',').Append(p.diameterMm);
        }
        return sb.ToString();
    }

    public void Decode(string s)
    {
        Nodes.Clear(); Pipes.Clear(); NextId = 1;
        if (!string.IsNullOrEmpty(s))
        {
            var parts = s.Split(';');
            if (parts.Length >= 3)
            {
                NextId = Mathf.Max(1, PI(parts[0]));
                if (parts[1].Length > 0)
                    foreach (var item in parts[1].Split('|'))
                    {
                        var v = item.Split(',');
                        if (v.Length < 15) continue;
                        int kind = PI(v[1]);
                        if (kind < 0 || kind > (int)IrrKind.Flood) continue;
                        Nodes.Add(new IrrNode
                        {
                            id = PI(v[0]), kind = (IrrKind)kind, pos = new Vector3(PF(v[2]), PF(v[3]), PF(v[4])), field = PI(v[5]), size = PF(v[6]),
                            power = (IrrPower)Mathf.Clamp(PI(v[7]), 0, 2), mode = (IrrMode)Mathf.Clamp(PI(v[8]), 0, 3), startHour = PI(v[9]), hours = PI(v[10]),
                            autoLow = PF(v[11]), autoHigh = PF(v[12]), revHours = Mathf.Max(1f, PF(v[13])), stored = PF(v[14]),
                        });
                        NextId = Mathf.Max(NextId, PI(v[0]) + 1);
                    }
                if (parts[2].Length > 0)
                    foreach (var item in parts[2].Split('|'))
                    {
                        var v = item.Split(',');
                        if (v.Length < 3) continue;
                        var na = Node(PI(v[0])); var nb = Node(PI(v[1]));
                        if (na == null || nb == null) continue;
                        Pipes.Add(new IrrPipe { a = na.id, b = nb.id, diameterMm = PI(v[2]), length = Flat(na.pos - nb.pos) });
                    }
            }
        }
        Changed();
    }
}
