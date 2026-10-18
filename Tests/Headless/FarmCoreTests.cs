// FarmCore (Assets/Scripts/FarmCore): cell fields painted by implements, the growth model, the irrigation network,
// the field overlay / Fields page / planner, mission adapter, saving, co-op deltas and performance.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Helpers other test sections use to drive FarmCore fields like the old plots.</summary>
static class FC
{
    public static FarmFields FF => FarmFields.Instance;
    public static FarmField F(int i) => FarmFields.Instance.Fields[i];
    public static int Count(FarmField f, Func<int, bool> pred) { int n = 0; for (int i = 0; i < f.Cells; i++) if (pred(i)) n++; return n; }
    public static int Sown(FarmField f) => Count(f, i => f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0);
    public static int Ripe(FarmField f) => Count(f, i => FarmSim.Ripe(f, i));
    public static int Untilled(FarmField f) => Count(f, i => f.ground[i] == (byte)Ground.Untilled);
    public static int Worked(FarmField f) => Count(f, i => f.ground[i] == (byte)Ground.Ploughed || f.ground[i] == (byte)Ground.Cultivated);
    public static bool AllUntilled(FarmField f) => Untilled(f) == f.Cells;
    public static string Crop(FarmField f) => FarmSim.Summarize(f).mainCrop;

    /// <summary>Run a tool over the whole field in strips (machine work, raises the same events as the tractor).</summary>
    public static WorkContext Whole(FarmField f, FieldTool tool, bool tractor = true, string crop = null, string product = null)
    {
        var rot = f.Rotation;
        WorkContext last = null;
        for (float x = -f.width * 0.5f + 1f; x < f.width * 0.5f; x += 2f)
        {
            var ctx = FF.NewContext(tool, false);   // a fresh context per pass, like one tractor frame
            if (crop != null) ctx.crop = crop;
            if (product != null) ctx.product = product;
            Vector3 a = f.center + rot * new Vector3(x, 0, -f.depth * 0.5f - 1f), b = f.center + rot * new Vector3(x, 0, f.depth * 0.5f + 1f);
            last = FF.Work(tool, a, b, 2.01f, false, tractor, ctx);
        }
        FF.FlushHarvests(true);
        return last;
    }

    public static void Prepare(FarmField f) => Whole(f, FieldTool.Cultivator);
    public static WorkContext Sow(FarmField f, string crop) => Whole(f, FieldTool.Seeder, true, crop);

    /// <summary>Make every sown cell ripe now.</summary>
    public static void Ripen(FarmField f, float growth = 1f)
    {
        for (int i = 0; i < f.Cells; i++) if (f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0) { f.Set(FieldLayer.Growth, i, growth); f.SetFlag(i, CellFlags.Withered, false); f.Touch(i); }
    }

    public static void Wet(FarmField f, float m) { for (int i = 0; i < f.Cells; i++) f.Set(FieldLayer.Moisture, i, m); }

    /// <summary>Harvest the whole field by hand (into farm storage). Returns kg.</summary>
    public static float HandHarvest(FarmField f)
    {
        var eco = EconomyManager.Instance;
        float before = Total();
        var ctx = FF.NewContext(FieldTool.HandHarvest, true);
        for (int i = 0; i < f.Cells; i++) if (FieldWork.ApplyCell(f, i, ctx)) f.Touch(i);
        var m = typeof(FarmFields).GetMethod("StoreHarvest", BindingFlags.Instance | BindingFlags.NonPublic);
        var after = typeof(FarmFields).GetMethod("AfterWork", BindingFlags.Instance | BindingFlags.NonPublic);
        after.Invoke(FF, new object[] { f, FieldTool.HandHarvest, Math.Max(1, ctx.cells), ctx, false, null });
        m.Invoke(FF, new object[] { ctx, false });
        FF.FlushHarvests(true);
        return Total() - before;
    }

    static float Total() { var eco = EconomyManager.Instance; return eco == null ? 0f : eco.Data.goods.Sum(g => eco.Stored(g.id)); }

    public static FarmEnv Env(string region = "najd", Season s = Season.Spring, int hour = 12, float temp = 20f, Weather w = Weather.Sunny)
    {
        var r = FcData.Region(region);
        return new FarmEnv { region = r, season = s, weather = w, hour = hour, tempC = temp, et0Day = FarmAgronomy.Et0(r, s) * FarmAgronomy.WeatherEt(w), rainMmPerHour = 0f, growthSpeed = 1f, fieldDry = 1f, withering = false, weeds = true, plowingRequired = true };
    }
}

static class FarmCoreTests
{
    static FarmField NewField(int id, float w, float d, float yaw = 0f, string region = "najd", Vector3? center = null)
    {
        var f = new FarmField(id, "Test " + id, center ?? Vector3.zero, yaw, w, d);
        f.ResetSoil(FcData.Region(region));
        return f;
    }

    static void AllCells(FarmField f, WorkContext ctx) { for (int i = 0; i < f.Cells; i++) if (FieldWork.ApplyCell(f, i, ctx)) f.Touch(i); }

    public static void Run()
    {
        Console.WriteLine("FarmCore: data, painting, growth, irrigation, saving, co-op, performance");
        int e0 = FakeEngine.Errors.Count;
        Pure();
        Irrigation();
        Codec();
        Performance();
        Scene();
        Translations();
        T.Check("FarmCore: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }

    // ------------------------------------------------------------------ data + pure field logic

    static void Pure()
    {
        var d = FcData.File;
        T.Check("FarmCore data: 12 crops (incl. Egypt DLC), 2 regions, 5 emitters, 4 pipe sizes, 14 tools", d.crops.Length >= 12 && d.regions.Length == 2 && d.emitters.Length == 5 && d.pipes.Length == 4 && d.tools.Length >= 14,
            d.crops.Length + " " + d.regions.Length + " " + d.emitters.Length + " " + d.pipes.Length + " " + d.tools.Length);
        T.Check("FarmCore data: real yields (wheat 6.5 t/ha, potato 35 t/ha) and paddy rice", FcData.Crop("wheat").yieldT == 6.5f && FcData.Crop("potato").yieldT == 35f && FcData.Crop("rice").flood && FcData.Crop("rice").waterMin > 0.8f);
        T.Check("FarmCore data: Najd sandy (70 mm), Delta clay (140 mm) with canal turns and a shallow water table",
            FcData.Region("najd").capacityMm == 70f && FcData.Region("nile_delta").capacityMm == 140f && FcData.Region("nile_delta").canal && FcData.Region("nile_delta").gwStart < FcData.Region("najd").gwStart);
        T.Check("FarmCore data: rotation table (legume -> grain +20%, same nightshade -15%)", Math.Abs(FcData.Rotation("legume", "grain") - 1.2f) < 0.001f && Math.Abs(FcData.Rotation("nightshade", "nightshade") - 0.85f) < 0.001f && FcData.Rotation("", "grain") == 1f);

        // geometry
        var f = NewField(901, 40f, 30f);
        T.Check("Field: 2 m cells (20 x 15), area 1200 m2", f.cols == 20 && f.rows == 15 && f.Cells == 300 && Math.Abs(f.Area - 1200f) < 0.01f);
        T.Check("Field: point -> cell and back", f.CellAt(f.CellCenter(137)) == 137 && f.CellAt(new Vector3(25f, 0, 0)) == -1 && f.Contains(new Vector3(19.9f, 0, 14.9f)));
        T.Check("Field: fresh Najd soil (N ~220, pH ~7.9, EC ~1.5, untilled, some stones)", Math.Abs(FarmSim.Summarize(f).n - 220f) < 15f && Math.Abs(FarmSim.Summarize(f).ph - 7.9f) < 0.1f && FC.AllUntilled(f) && FC.Count(f, i => f.stones[i] > 0) > 0);

        // painting by working width along a path
        var ctx = new WorkContext { tool = FieldTool.Plough, plowingRequired = true };
        int n = FieldWork.Paint(f, new Vector3(-10f, 0, -20f), new Vector3(-10f, 0, 20f), 3f, ctx);
        int ploughed = FC.Count(f, i => f.ground[i] == (byte)Ground.Ploughed);
        bool strip = Enumerable.Range(0, f.Cells).All(i => (f.ground[i] == (byte)Ground.Ploughed) == (Math.Abs(f.CellCenter(i).x + 10f) <= 1.5f));
        T.Check("Painting: a 3 m plough along a 40 m path works exactly the cells under it (2 columns x 15 rows)", n == 30 && ploughed == 30 && strip, n + " " + ploughed);
        int again = FieldWork.Paint(f, new Vector3(-10f, 0, -20f), new Vector3(-10f, 0, 20f), 3f, ctx);
        T.Check("Painting: going over the same strip again changes nothing", again == 0);
        var ctx6 = new WorkContext { tool = FieldTool.Cultivator };
        int c6 = FieldWork.Paint(f, new Vector3(7f, 0, -3f), new Vector3(7f, 0, 3f), 6f, ctx6);
        T.Check("Painting: wider tool, wider strip (6 m x 6 m = 9 cells)", c6 == 9, c6 + "");
        var fr = NewField(902, 30f, 30f, 30f);
        var ctxr = new WorkContext { tool = FieldTool.Plough };
        Vector3 along = fr.Rotation * Vector3.forward;
        int nr = FieldWork.Paint(fr, -along * 16f, along * 16f, 2.1f, ctxr);
        T.Check("Painting: rotated fields work along their own rows (diagonal path through a turned field)", nr >= 14 && nr <= 17 && FC.Count(fr, i => fr.ground[i] == (byte)Ground.Ploughed) == nr, nr + "");
        float tillComp = f.Get(FieldLayer.Compaction, f.CellAt(new Vector3(-10f, 0, 0))), tillWeeds = f.Get(FieldLayer.Weeds, f.CellAt(new Vector3(-10f, 0, 0)));
        T.Check("Plough: deep tillage breaks compaction and buries weeds", tillComp < 0.05f && tillWeeds < 0.03f, tillComp + " " + tillWeeds);

        // full chain: cultivate -> sow -> fertilise -> grow -> harvest
        var g = NewField(903, 20f, 20f);
        AllCells(g, new WorkContext { tool = FieldTool.Cultivator });
        float seedM2 = 0f;
        var sow = new WorkContext { tool = FieldTool.Seeder, crop = "wheat", density = 1, takeSeed = (c, m2) => { seedM2 += m2; return true; } };
        AllCells(g, sow);
        T.Check("Chain: seeder sows every worked cell (400 m2 of seed)", FC.Sown(g) == 100 && Math.Abs(seedM2 - 400f) < 0.01f && sow.sown["wheat"] == 100, FC.Sown(g) + " " + seedM2);
        var noSeed = NewField(904, 6f, 6f);
        AllCells(noSeed, new WorkContext { tool = FieldTool.Cultivator });
        var empty = new WorkContext { tool = FieldTool.Seeder, crop = "wheat", takeSeed = (c, m2) => false };
        T.Check("Chain: no seed, nothing sown, and the reason is given", FieldWork.Paint(noSeed, new Vector3(0, 0, -4), new Vector3(0, 0, 4), 6f, empty) == 0 && empty.blocked != null);
        var untilledSow = NewField(905, 6f, 6f);
        T.Check("Chain: ploughing required -> no sowing on untilled land", FieldWork.Paint(untilledSow, new Vector3(0, 0, -4), new Vector3(0, 0, 4), 6f, new WorkContext { tool = FieldTool.Seeder, crop = "wheat", plowingRequired = true }) == 0
            && FieldWork.Paint(untilledSow, new Vector3(0, 0, -4), new Vector3(0, 0, 4), 6f, new WorkContext { tool = FieldTool.Seeder, crop = "wheat", plowingRequired = false }) == 9);
        float n0 = FarmSim.Summarize(g).n;
        AllCells(g, new WorkContext { tool = FieldTool.Spreader, product = "urea" });
        T.Check("Chain: urea spreader adds 80 kg N/ha on every cell, once per crop", Math.Abs(FarmSim.Summarize(g).n - n0 - 80f) < 0.5f && FieldWork.Paint(g, new Vector3(0, 0, -11), new Vector3(0, 0, 11), 20f, new WorkContext { tool = FieldTool.Spreader, product = "urea" }) == 0);
        var env = FC.Env();
        int hours = 0;
        while (FC.Ripe(g) < g.Cells && hours < 24 * 20)
        {
            for (int i = 0; i < g.Cells; i++) if (g.Get(FieldLayer.Moisture, i) < 0.6f) FarmAgronomy.Irrigate(g, i, 25f, 27f, FcData.Emitter(IrrKind.Drip), 1.6f, 1f, 70f);
            env.hour = hours % 24;
            FarmSim.Step(g, 1f, env); hours++;
        }
        T.Check("Growth: watered wheat at 20 C ripens in ~5 days (its growDays)", FC.Ripe(g) == g.Cells && hours >= 100 && hours <= 150, hours + " h");
        var sum = FarmSim.Summarize(g);
        float expected = sum.expectedKg;
        var harvest = new WorkContext { tool = FieldTool.HandHarvest, byHand = true };
        AllCells(g, harvest);
        float kg = harvest.HarvestedKg;
        float tPerHa = kg / 1000f / g.Hectares;
        T.Check("Chain: harvest gives the expected kg (cells x yield potential)", Math.Abs(kg - expected) < 0.5f && tPerHa > 4.5f && tPerHa < 7.5f, kg + " vs " + expected + " = " + tPerHa.ToString("F2") + " t/ha");
        T.Check("Chain: harvest leaves stubble with straw and removes nutrients", FC.Count(g, i => g.ground[i] == (byte)Ground.Stubble && g.Has(i, CellFlags.Residue)) == g.Cells && FarmSim.Summarize(g).n < sum.n - 30f);

        // yield factors: salty unfertilised soil gives less
        var bad = NewField(906, 20f, 20f);
        for (int i = 0; i < bad.Cells; i++) { bad.Set(FieldLayer.Salinity, i, 9f); bad.Set(FieldLayer.N, i, 20f); }
        AllCells(bad, new WorkContext { tool = FieldTool.Cultivator });
        AllCells(bad, new WorkContext { tool = FieldTool.Seeder, crop = "potato" });
        var fac = FarmAgronomy.Factors(bad, 0, FcData.Crop("potato"));
        T.Check("Factors: potato on salty, N-poor soil: salt and nitrogen are the limiting factors", fac.salt < 0.3f && fac.n < 0.5f && (fac.Limiting == 4 || fac.Limiting == 0), fac.salt + " " + fac.n + " lim " + fac.Limiting);
        T.Check("Factors: Maas-Hoffman (wheat 6.0 / 7.1 % per dS/m)", Math.Abs(FarmAgronomy.SaltFactor(8f, FcData.Crop("wheat")) - (1f - 0.071f * 2f)) < 0.001f);

        // water stress and withering
        var dry = NewField(907, 10f, 10f);
        AllCells(dry, new WorkContext { tool = FieldTool.Cultivator });
        AllCells(dry, new WorkContext { tool = FieldTool.Seeder, crop = "wheat" });
        FC.Wet(dry, 0f);
        var envW = FC.Env(); envW.withering = true;
        float before = dry.Get(FieldLayer.Growth, 0);
        for (int h = 0; h < 30; h++) FarmSim.Step(dry, 1f, envW);
        T.Check("Water: bone-dry crops don't grow and wither after a day (Withering on)", dry.Get(FieldLayer.Growth, 0) <= before + 0.0001f && FC.Count(dry, i => dry.Has(i, CellFlags.Withered)) == dry.Cells);
        var et = NewField(908, 10f, 10f);
        FC.Wet(et, 1f);
        var envS = FC.Env(s: Season.Summer); envS.et0Day = 9.5f;
        for (int h = 0; h < 24; h++) { envS.hour = h; FarmSim.Step(et, 1f, envS); }
        float lossSummer = 1f - et.Get(FieldLayer.Moisture, 0);
        FC.Wet(et, 1f);
        var envWi = FC.Env(s: Season.Winter); envWi.et0Day = 3.5f;
        for (int h = 0; h < 24; h++) { envWi.hour = h; FarmSim.Step(et, 1f, envWi); }
        float lossWinter = 1f - et.Get(FieldLayer.Moisture, 0);
        T.Check("Water: bare soil loses more water on a summer day than a winter day (ET0 9.5 vs 3.5 mm)", lossSummer > lossWinter * 2f && lossWinter > 0f, lossSummer + " vs " + lossWinter);

        // other tools
        var tools = NewField(909, 10f, 10f);
        AllCells(tools, new WorkContext { tool = FieldTool.Plough });
        int stones = FC.Count(tools, i => tools.stones[i] > 0);
        AllCells(tools, new WorkContext { tool = FieldTool.Roller });
        T.Check("Roller: presses the stones and firms the seedbed", FC.Count(tools, i => tools.stones[i] > 0) == 0 && FC.Count(tools, i => tools.Has(i, CellFlags.Rolled)) == tools.Cells && stones >= 0);
        AllCells(tools, new WorkContext { tool = FieldTool.Seeder, crop = "alfalfa" });
        FC.Ripen(tools);
        var mow = new WorkContext { tool = FieldTool.Mower };
        AllCells(tools, mow);
        T.Check("Mower: forage is cut into a swath and regrows (cut 1 of 6)", tools.swathKg > 0f && FC.Count(tools, i => tools.Has(i, CellFlags.Swath)) == tools.Cells && FC.Sown(tools) == tools.Cells && tools.Get(FieldLayer.Growth, 0) > 0.5f);
        var bale = new WorkContext { tool = FieldTool.Baler };
        float swath = tools.swathKg;
        AllCells(tools, bale);
        T.Check("Baler: picks up the swath as hay + animal feed", Math.Abs(bale.harvested["alfalfa"] - swath) < 0.5f && bale.feed > 0f && tools.swathKg < 0.01f);
        FC.Ripen(tools);
        var combine = new WorkContext { tool = FieldTool.Harvester };
        AllCells(tools, combine);
        T.Check("Harvester: refuses forage (use the mower)", combine.cells == 0 && combine.blocked != null);
        var wh = NewField(910, 10f, 10f);
        AllCells(wh, new WorkContext { tool = FieldTool.Cultivator });
        AllCells(wh, new WorkContext { tool = FieldTool.Seeder, crop = "wheat" });
        FC.Ripen(wh);
        var tank = new WorkContext { tool = FieldTool.Harvester, capacityKg = 10f };
        AllCells(wh, tank);
        T.Check("Harvester: stops when the tank is full", tank.HarvestedKg > 0f && FC.Ripe(wh) > 0 && tank.blocked != null && tank.HarvestedKg <= 10f, FC.Ripe(wh) + " " + tank.HarvestedKg);
        var herb = NewField(911, 10f, 10f);
        for (int i = 0; i < herb.Cells; i++) herb.Set(FieldLayer.Weeds, i, 0.8f);
        AllCells(herb, new WorkContext { tool = FieldTool.Sprayer, product = "herbicide" });
        T.Check("Sprayer: herbicide clears the weeds", FarmSim.Summarize(herb).weeds < 0.01f && herb.weedFreeDays > 0);
        var mulch = NewField(912, 10f, 10f);
        AllCells(mulch, new WorkContext { tool = FieldTool.Cultivator });
        AllCells(mulch, new WorkContext { tool = FieldTool.Seeder, crop = "wheat" });
        FC.Ripen(mulch);
        AllCells(mulch, new WorkContext { tool = FieldTool.HandHarvest, byHand = true });
        AllCells(mulch, new WorkContext { tool = FieldTool.Mulcher });
        T.Check("Mulcher: chops the stubble into straw mulch", FC.Count(mulch, i => mulch.Has(i, CellFlags.Mulched)) == mulch.Cells);
        T.Check("Wet soil: working it when waterlogged compacts and smears it", TillWet());
    }

    static bool TillWet()
    {
        var f = NewField(913, 4f, 4f);
        FC.Wet(f, 1.0f);
        float c0 = f.Get(FieldLayer.Compaction, 0);
        FarmAgronomy.Till(f, 0, false, false);
        return f.Has(0, CellFlags.PoorSeedbed) && f.Get(FieldLayer.Compaction, 0) > c0 - 0.12f + 0.1f;
    }

    // ------------------------------------------------------------------ irrigation network

    static IrrigationNetwork Net(float gw = 110f) => new IrrigationNetwork { GroundwaterM = gw };

    static void Irrigation()
    {
        var env = FC.Env(hour: 12);
        // capacity and pipe limits
        var big = NewField(920, 100f, 100f);   // 1 ha
        var net = Net();
        var well = net.Add(IrrKind.Well, new Vector3(-60f, 0, 0)); well.power = IrrPower.Diesel;
        var flood = net.Add(IrrKind.Flood, new Vector3(-49f, 0, 0), big.id); flood.mode = IrrMode.On;
        net.Connect(well.id, flood.id, 160);
        var fields = new List<FarmField> { big };
        var rep = net.Step(1f, env, fields, false, false, 1f, 1f);
        T.Check("Network: flood on 1 ha wants 30 m3/h, the well gives its 6 m3/h", Math.Abs(flood.demand - 30f) < 0.5f && Math.Abs(flood.delivered - 6f) < 0.01f && Math.Abs(rep.m3 - 6f) < 0.01f && flood.shortage == "Not enough water",
            flood.demand + " " + flood.delivered + " " + rep.m3);
        var net2 = Net();
        var deep = net2.Add(IrrKind.DeepWell, new Vector3(-60f, 0, 0)); deep.power = IrrPower.Diesel;
        var fl2 = net2.Add(IrrKind.Flood, new Vector3(-49f, 0, 0), big.id); fl2.mode = IrrMode.On;
        net2.Connect(deep.id, fl2.id, 50);
        net2.Step(1f, env, fields, false, false, 1f, 1f);
        T.Check("Network: a 50 mm pipe limits the deep well to 6 m3/h", Math.Abs(fl2.delivered - 6f) < 0.01f && fl2.shortage == "Pipe too small", fl2.delivered + " " + fl2.shortage);
        net2.Connect(deep.id, fl2.id, 160);
        net2.Step(1f, env, fields, false, false, 1f, 1f);
        T.Check("Network: a 160 mm pipe lets the deep well give its 20 m3/h", Math.Abs(fl2.delivered - 20f) < 0.01f);
        var junc = net2.Add(IrrKind.Junction, new Vector3(-55f, 0, 0));
        var spr = net2.Add(IrrKind.Sprinkler, new Vector3(0, 0, 0), -1, 9f); spr.mode = IrrMode.On;
        net2.Connect(deep.id, junc.id, 160); net2.Connect(junc.id, spr.id, 75);
        T.Check("Network: one connected network, widest path through the junction", net2.Components().Count == 1 && Math.Abs(net2.Bottleneck(spr, net2.Components()[0]) - 14f) < 0.01f);
        var lonely = net2.Add(IrrKind.Sprinkler, new Vector3(30f, 0, 30f), -1, 9f); lonely.mode = IrrMode.On;
        net2.Step(1f, env, fields, false, false, 1f, 1f);
        T.Check("Network: an emitter without pipes gets nothing and says why", lonely.delivered == 0f && lonely.shortage == "Not connected to a water source" && net2.Components().Count == 2);
        T.Check("Network: dose lands on the field (0.6 mm per m3 x 1 ha ...)", fields[0].cycleWaterM3 > 0f);

        // drip vs flood: water reaching the roots and salt per m3
        var a = NewField(921, 20f, 20f); var b = NewField(922, 20f, 20f);
        foreach (var f in new[] { a, b }) { AllCells(f, new WorkContext { tool = FieldTool.Cultivator }); AllCells(f, new WorkContext { tool = FieldTool.Seeder, crop = "tomato" }); FC.Wet(f, 0.2f); }
        float ecA = FarmSim.Summarize(a).ec, ecB = FarmSim.Summarize(b).ec;
        var n3 = Net();
        var src = n3.Add(IrrKind.DeepWell, new Vector3(-30f, 0, 0)); src.power = IrrPower.Diesel;
        var drip = n3.Add(IrrKind.Drip, a.center, a.id); drip.mode = IrrMode.On;
        var n4 = Net();
        var src4 = n4.Add(IrrKind.DeepWell, new Vector3(-30f, 0, 0)); src4.power = IrrPower.Diesel;
        var fl = n4.Add(IrrKind.Flood, b.center, b.id); fl.mode = IrrMode.On;
        n3.Connect(src.id, drip.id, 160); n4.Connect(src4.id, fl.id, 160);
        var rd = n3.Step(1f, env, new List<FarmField> { a }, false, false, 1f, 1f);
        var rf = n4.Step(1f, env, new List<FarmField> { b }, false, false, 1f, 1f);
        float wetA = (FarmSim.Summarize(a).moisture - 0.2f) / rd.m3, wetB = (FarmSim.Summarize(b).moisture - 0.2f) / rf.m3;
        float saltA = (FarmSim.Summarize(a).ec - ecA) / rd.m3, saltB = (FarmSim.Summarize(b).ec - ecB) / rf.m3;
        T.Check("Drip vs flood: per m3, drip wets the root zone ~1.6x more", wetA > wetB * 1.4f, wetA + " vs " + wetB);
        T.Check("Drip vs flood: flood uses much more water per hour (30 vs 4 m3/h/ha)", rf.m3 > rd.m3 * 5f, rf.m3 + " vs " + rd.m3);
        T.Check("Drip vs flood: per m3, drip leaves less salt behind", saltA < saltB, saltA + " vs " + saltB);

        // night vs day evaporation (sprinklers)
        float day = Gain(13), night = Gain(1);
        T.Check("Evaporation: sprinkling at night puts ~20% more water in the soil than at midday", night > day * 1.15f, night + " vs " + day);

        // schedule and sensor automation
        var sched = new IrrNode { mode = IrrMode.Schedule, startHour = 22, hours = 6 };
        T.Check("Schedule: 22:00 for 6 h runs over midnight", IrrigationNetwork.InSchedule(sched, 23) && IrrigationNetwork.InSchedule(sched, 3) && !IrrigationNetwork.InSchedule(sched, 4) && !IrrigationNetwork.InSchedule(sched, 12));
        var af = NewField(930, 20f, 20f);
        var an = Net();
        var asrc = an.Add(IrrKind.DeepWell, new Vector3(-20f, 0, 0)); asrc.power = IrrPower.Diesel;
        var sensor = an.Add(IrrKind.Sensor, af.center, af.id);
        var pivot = an.Add(IrrKind.Pivot, af.center, -1, 15f); pivot.mode = IrrMode.Auto;
        an.Connect(asrc.id, pivot.id, 110);
        var al = new List<FarmField> { af };
        FC.Wet(af, 0.3f); an.Step(0.1f, FC.Env(hour: 12), al, false, false, 1f, 1f);
        bool onDry = pivot.running && sensor.reading < 0.45f;
        FC.Wet(af, 0.6f); an.Step(0.1f, FC.Env(hour: 12), al, false, false, 1f, 1f);
        bool keepsOn = pivot.running;
        FC.Wet(af, 0.95f); an.Step(0.1f, FC.Env(hour: 12), al, false, false, 1f, 1f);
        bool offWet = !pivot.running;
        FC.Wet(af, 0.6f); an.Step(0.1f, FC.Env(hour: 12), al, false, false, 1f, 1f);
        bool staysOff = !pivot.running;
        T.Check("Sensor automation: starts below 45%, keeps going until 90%, then waits (hysteresis)", onDry && keepsOn && offWet && staysOff, onDry + " " + keepsOn + " " + offWet + " " + staysOff);
        T.Check("Water ban: nothing runs", BanStops(an, al));

        // pivot: only the cells under the turning arm get water this step
        var pf = NewField(931, 60f, 60f);
        FC.Wet(pf, 0.2f);
        var pn = Net();
        var ps = pn.Add(IrrKind.DeepWell, new Vector3(-40f, 0, 0)); ps.power = IrrPower.Diesel;
        var pv = pn.Add(IrrKind.Pivot, pf.center, -1, 30f); pv.mode = IrrMode.On; pv.revHours = 24f;
        pn.Connect(ps.id, pv.id, 160);
        pn.Step(1f, FC.Env(), new List<FarmField> { pf }, false, false, 1f, 1f);
        int wetCells = FC.Count(pf, i => pf.Get(FieldLayer.Moisture, i) > 0.21f);
        int reach = IrrigationNetwork.Cover(pv, pf, null);
        T.Check("Pivot: one hour of a 24 h revolution waters about 1/24 of its circle, the arm turned 15 degrees", wetCells > 0 && wetCells < reach / 6 && Math.Abs(pv.angle - 15f) < 0.01f, wetCells + " of " + reach);
        for (int h = 0; h < 23; h++) pn.Step(1f, FC.Env(), new List<FarmField> { pf }, false, false, 1f, 1f);
        T.Check("Pivot: a full revolution waters its whole circle", FC.Count(pf, i => pf.Get(FieldLayer.Moisture, i) > 0.21f) >= reach - 2, FC.Count(pf, i => pf.Get(FieldLayer.Moisture, i) > 0.21f) + " of " + reach);

        // groundwater depletion and recharge
        var najd = FcData.Region("najd");
        var gn = Net(110f);
        var gw = gn.Add(IrrKind.DeepWell, new Vector3(-60f, 0, 0)); gw.power = IrrPower.Diesel;
        var gfl = gn.Add(IrrKind.Flood, new Vector3(-49f, 0, 0), big.id); gfl.mode = IrrMode.On;
        gn.Connect(gw.id, gfl.id, 160);
        float pumped = 0f, cost = 0f;
        for (int h = 0; h < 50; h++) { var r = gn.Step(1f, env, fields, false, false, 1f, 1f); pumped += r.groundM3; cost += r.cost; }
        T.Check("Groundwater: 1000 m3 from the Saq aquifer lowers the water table ~1 m", Math.Abs(pumped - 1000f) < 1f && Math.Abs(gn.GroundwaterM - 111f) < 0.05f, pumped + " m3 -> " + gn.GroundwaterM);
        T.Check("Groundwater: pumping is metered and costs money (fee + diesel)", cost > pumped * najd.waterCost && cost > 0f, cost + "");
        gn.Recharge(najd, false);
        T.Check("Groundwater: slow recharge, never above the natural level", Math.Abs(gn.GroundwaterM - (111f - najd.gwRecharge)) < 0.001f);
        gn.GroundwaterM = 175f;
        var wq = gn.Add(IrrKind.Well, new Vector3(-70f, 0, 0)); wq.power = IrrPower.Diesel;
        T.Check("Groundwater: a shallow well near its limit gives a fraction of its flow, a deep well still pumps", gn.SourceFlow(wq, najd, false, 1f, 1f, 0f, 1f) < 1.2f && gn.SourceFlow(gw, najd, false, 1f, 1f, 0f, 1f) > 19f);
        T.Check("Pump energy: deeper water needs more power", IrrigationNetwork.PumpKw(10f, 175f) > IrrigationNetwork.PumpKw(10f, 110f) * 1.5f);
        var solar = new IrrNode { kind = IrrKind.Well, power = IrrPower.Solar };
        T.Check("Solar pump: works by day, stops at night", gn.SourceFlow(solar, najd, false, 1f, 1f, 20f, 1f) > 0f && gn.SourceFlow(solar, najd, false, 0f, 1f, 20f, 1f) == 0f);
        var gridPump = new IrrNode { kind = IrrKind.Well, power = IrrPower.Grid };
        gn.GroundwaterM = 110f;
        T.Check("Grid pump: runs at the grid's power ratio", Math.Abs(gn.SourceFlow(gridPump, najd, false, 1f, 0.5f, 20f, 1f) - 3f) < 0.01f);

        // canal turns (Delta)
        var delta = FcData.Region("nile_delta");
        var canal = new IrrNode { kind = IrrKind.Canal, power = IrrPower.Diesel };
        T.Check("Canal: flows only on the canal turn (Delta), never in Najd", gn.SourceFlow(canal, delta, true, 1f, 1f, 0f, 1f) == 40f && gn.SourceFlow(canal, delta, false, 1f, 1f, 0f, 1f) == 0f && gn.SourceFlow(canal, najd, true, 1f, 1f, 0f, 1f) == 0f);
        var tnet = Net(3f);
        var tc = tnet.Add(IrrKind.Canal, new Vector3(-60f, 0, 0));
        var tk = tnet.Add(IrrKind.Tank, new Vector3(-55f, 0, 0));
        var tfl = tnet.Add(IrrKind.Flood, new Vector3(-49f, 0, 0), big.id); tfl.mode = IrrMode.Off;
        tnet.Connect(tc.id, tk.id, 160); tnet.Connect(tk.id, tfl.id, 160);
        var denv = FC.Env("nile_delta");
        tnet.Step(2f, denv, fields, true, false, 1f, 1f);
        float stored = tk.stored;
        tfl.mode = IrrMode.On;
        tnet.Step(1f, denv, fields, false, false, 1f, 1f);
        T.Check("Tank: fills from the canal on the turn and waters the field on dry days", stored >= 49.9f && tfl.delivered > 19f && tk.stored < stored - 19f, stored + " -> " + tk.stored + ", delivered " + tfl.delivered);

        // codec
        var copy = new IrrigationNetwork();
        copy.Decode(tnet.Encode());
        T.Check("Network: saved and loaded (parts, pipes, settings, tank stock)", copy.Encode() == tnet.Encode() && copy.Nodes.Count == 3 && copy.Pipes.Count == 2 && Math.Abs(copy.Node(tk.id).stored - tk.stored) < 0.01f);
        T.Check("Network: prices (pivot by span, drip by field area, pipes by metre and size)", IrrigationNetwork.Price(IrrKind.Pivot, 30f, 0f) == 8000 + 150 * 30 && IrrigationNetwork.Price(IrrKind.Drip, 0f, 1500f) == 300 + 2000
            && IrrigationNetwork.PipePrice(100f, 160) > IrrigationNetwork.PipePrice(100f, 50) * 4);
    }

    /// <summary>Soil water gained per m3 of sprinkling in one hour at this clock hour.</summary>
    static float Gain(int hour)
    {
        var f = NewField(923 + hour, 12f, 12f);
        FC.Wet(f, 0.2f);
        var nn = Net();
        var src = nn.Add(IrrKind.DeepWell, new Vector3(-20f, 0, 0)); src.power = IrrPower.Diesel;
        var sp = nn.Add(IrrKind.Sprinkler, Vector3.zero, -1, 9f); sp.mode = IrrMode.On;
        nn.Connect(src.id, sp.id, 110);
        var e = FC.Env(hour: hour);
        var r = nn.Step(1f, e, new List<FarmField> { f }, false, false, 1f, 1f);
        return (FarmSim.Summarize(f).moisture - 0.2f) / Math.Max(0.0001f, r.m3);
    }

    static bool BanStops(IrrigationNetwork an, List<FarmField> al)
    {
        FC.Wet(al[0], 0.2f);
        var r = an.Step(1f, FC.Env(), al, false, true, 1f, 1f);
        return r.m3 == 0f && an.Nodes.All(n => !n.running || !n.IsEmitter);
    }

    // ------------------------------------------------------------------ save codec, co-op deltas

    static void Codec()
    {
        var f = NewField(940, 30f, 20f);
        AllCells(f, new WorkContext { tool = FieldTool.Cultivator });
        var half = new WorkContext { tool = FieldTool.Seeder, crop = "onion" };
        FieldWork.Paint(f, new Vector3(-5f, 0, -12f), new Vector3(-5f, 0, 12f), 10f, half);
        f.Set(FieldLayer.Growth, 7, 0.42f); f.pest = "aphids"; f.lastCrop = "wheat"; f.history = "3|wheat|91|102|180|4.5|20|6"; f.cycleWaterM3 = 12.5f;
        string cells = f.EncodeCells(), meta = f.EncodeMeta();
        var g = new FarmField(940, "copy", Vector3.zero, 0f, 30f, 20f);
        bool ok = g.DecodeCells(cells);
        g.DecodeMeta(meta);
        bool same = ok && f.ground.SequenceEqual(g.ground) && f.flags.SequenceEqual(g.flags) && f.stones.SequenceEqual(g.stones) && Enumerable.Range(0, f.layers.Length).All(l => f.layers[l].SequenceEqual(g.layers[l]))
            && Enumerable.Range(0, f.Cells).All(i => f.CropId(i) == g.CropId(i));
        T.Check("Save: every cell layer comes back exactly (ground, crop, flags, stones, 10 layers)", same);
        T.Check("Save: field values come back (pest, last crop, history, water used)", g.pest == "aphids" && g.lastCrop == "wheat" && g.history == f.history && Math.Abs(g.cycleWaterM3 - 12.5f) < 0.01f);
        T.Check("Save: the saved crop table doesn't depend on the session (crop ids by name)", cells.Split(';')[3] == "onion");
        var wrong = new FarmField(941, "other size", Vector3.zero, 0f, 20f, 20f);
        T.Check("Save: data for another field size is refused (field starts fresh)", !wrong.DecodeCells(cells) && !wrong.DecodeCells("junk") && !wrong.DecodeCells(""));
        var uni = new FarmField(942, "uniform", Vector3.zero, 0f, 200f, 200f);   // 4 ha of bare land
        string u = uni.EncodeCells();
        T.Check("Save: run-length coding keeps a uniform 4 ha field tiny (< 200 bytes for 10,000 cells)", u.Length < 200, u.Length + "");
        var r = NewField(943, 100f, 100f);
        int raw = r.Cells * (4 + 2 * r.layers.Length);
        T.Check("Save: a patchy real field still saves smaller than its raw cells", r.EncodeCells().Length < raw * 1.4f, r.EncodeCells().Length + " vs raw " + raw);

        // co-op deltas
        var host = NewField(944, 30f, 20f); var client = new FarmField(944, "client", Vector3.zero, 0f, 30f, 20f);
        client.DecodeCells(host.EncodeCells());
        host.TakeNetDirty();
        FieldWork.Paint(host, new Vector3(0, 0, -12f), new Vector3(0, 0, 12f), 4f, new WorkContext { tool = FieldTool.Plough });
        var dirty = host.TakeNetDirty();
        string msg = FarmNet.EncodeDelta(host, dirty);
        var parts = NetProtocol.Split(msg);
        int applied = FarmNet.ApplyDelta(client, NetProtocol.Field(parts, 3), NetProtocol.Field(parts, 4));
        bool synced = host.ground.SequenceEqual(client.ground) && Enumerable.Range(0, host.layers.Length).All(l => host.layers[l].SequenceEqual(client.layers[l]));
        T.Check("Co-op: only the changed cells are sent and the client matches the host", dirty.Count == 30 && applied == 30 && synced, dirty.Count + " " + applied);
        T.Check("Co-op: compact deltas (< 40 chars a cell)", msg.Length < 40 * dirty.Count + 30, msg.Length + " for " + dirty.Count);
        T.Check("Co-op: nothing changed, nothing to send", host.TakeNetDirty().Count == 0);
    }

    // ------------------------------------------------------------------ performance

    static void Performance()
    {
        var big = NewField(950, 450f, 445f);   // ~20 ha
        T.Check("Performance: a 20 ha field is ~50,000 cells in ~1.2 MB", big.Cells > 49000 && big.Cells * (4 + 2 * big.layers.Length) < 1.3e6, big.Cells + " cells");
        var ctx = new WorkContext { tool = FieldTool.Cultivator };
        AllCells(big, ctx);
        AllCells(big, new WorkContext { tool = FieldTool.Seeder, crop = "wheat" });
        var env = FC.Env();
        FarmSim.Step(big, 0.25f, env);
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 4; k++) FarmSim.Step(big, 0.25f, env);
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / 4;
        T.Check("Performance: a 20 ha farm ticks in < 60 ms per step (every in-game 15 min = 15 real s by default)", ms < 60, ms.ToString("F1") + " ms");
        var net = Net();
        var s = net.Add(IrrKind.DeepWell, new Vector3(-240f, 0, 0)); s.power = IrrPower.Diesel;
        var p = net.Add(IrrKind.Pivot, Vector3.zero, -1, 60f); p.mode = IrrMode.On;
        net.Connect(s.id, p.id, 160);
        var sw2 = Stopwatch.StartNew();
        net.Step(0.25f, env, new List<FarmField> { big }, false, false, 1f, 1f);
        sw2.Stop();
        T.Check("Performance: irrigation step over 20 ha < 80 ms", sw2.Elapsed.TotalMilliseconds < 80, sw2.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
        var sw3 = Stopwatch.StartNew();
        var sum = FarmSim.Summarize(big);
        sw3.Stop();
        T.Check("Performance: summary of 20 ha < 60 ms", sw3.Elapsed.TotalMilliseconds < 60 && sum.sown == big.Cells, sw3.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
    }

    // ------------------------------------------------------------------ in the game

    static void Scene()
    {
        // runs in the main world (after the core sections): start the fields, supplies and network clean
        Loc.SetLanguage(Language.English);
        var ff = FarmFields.Instance; var eco = EconomyManager.Instance; var mm = MissionManager.Instance;
        int mission0 = mm.Index; float progress0 = mm.Progress;
        foreach (var f in ff.Fields) f.ResetSoil(FcData.CurrentRegion);
        ff.UnloadTank();
        IrrigationSystem.Instance.Net.Clear();
        FarmFields.PreferredSeed = ""; ff.SetSowCrop("wheat");
        eco.AddSeeds("wheat", 12); eco.AddSeeds("potato", 4);
        T.Check("Scene: FarmCore fields, irrigation system, planner and crop renderer created", ff != null && ff.Ready && IrrigationSystem.Instance != null && IrrigationPlanner.Instance != null && FarmFieldRenderer.Instance != null);
        T.Check("Scene: the farm has two 10 x 14 m fields where the old plots were", ff.Fields.Count(f => f.source == "farm") == 2 && ff.Fields.Where(f => f.source == "farm").All(f => f.width == 10f && f.depth == 14f && f.View != null && f.View.GetComponent<BoxCollider>() != null));
        var f0 = ff.Fields[0]; var f1 = ff.Fields[1];
        T.Check("Scene: ground texture 4 px per cell, field icon on the map", f0.View.GroundTexture != null && f0.View.GroundTexture.width == f0.cols * 4 && f0.View.GetComponent<MinimapIcon>() != null);
        T.Check("Scene: the tractor and the land office see the fields", FarmTools.OverField(f0.center) && !FarmTools.OverField(f0.center + new Vector3(80f, 0, 0)) && ff.FieldAt(f1.center) == f1);
        eco.AddMoney(50000);

        // hand work with E: hoe, plant, water, harvest
        var obj = f0.View;
        string prompt = obj.PromptRaw(f0.center);
        T.Check("Prompt: untilled field offers the hoe and shows the field info", prompt.Contains("[E] Hoe the soil") && prompt.Contains("Field 1") && prompt.Contains("140 m2"), prompt);
        ff.HandJob(f0.center);
        int worked = FC.Worked(f0);
        T.Check("E: hoe works the ~6 x 6 m around you", worked >= 6 && worked <= 12, worked + "");
        float seedM2 = eco.Seeds("wheat") * FarmFields.BagM2("wheat") + ff.SeedLeft("wheat");
        ff.HandJob(f0.center);
        float used = seedM2 - (eco.Seeds("wheat") * FarmFields.BagM2("wheat") + ff.SeedLeft("wheat"));
        T.Check("E: plants wheat on the hoed patch (seed by area, from the bags)", FC.Sown(f0) == worked && Math.Abs(used - worked * 4f) < 0.01f && FC.Crop(f0) == "wheat", used + " m2");
        FC.Wet(f0, 0.1f);
        var well = UnityEngine.Object.FindAnyObjectByType<WellController>();
        if (!well.IsRunning) well.StartWell();
        ff.HandJob(f0.center);
        T.Check("E: waters the dry crop (well running)", FarmSim.Summarize(f0).moisture > 0.15f && f0.Get(FieldLayer.Moisture, f0.CellAt(f0.center)) > 0.3f);
        FC.Ripen(f0);
        float wheat = eco.Stored("wheat");
        string ripePrompt = obj.PromptRaw(f0.center);
        ff.HandJob(f0.center);
        T.Check("E: harvests the ripe patch into storage", ripePrompt.Contains("[E] Harvest") && eco.Stored("wheat") > wheat && FC.Sown(f0) == 0, ripePrompt);

        // the field info overlay
        FC.Prepare(f1);
        FC.Sow(f1, "wheat");
        string info = FieldOverlay.Line(f1, f1.center);
        T.Check("Overlay: crop, stage, expected yield and soil water", info.Contains("Wheat") && info.Contains("Germination") && info.Contains("Expected") && info.Contains("Water"), info);
        T.Check("Overlay: soil numbers only after a soil test", !info.Contains("pH"));
        eco.TryBuy("soil_kit");
        bool sampled = ff.TakeSample(f1);
        string info2 = FieldOverlay.Line(f1, f1.center);
        T.Check("Overlay: soil test shows N, P, K, pH, EC and the limiting factor", sampled && info2.Contains("pH") && info2.Contains("EC") && ff.Items(FarmFields.KitId) == 0, info2);
        string page = FieldsPage.Text();
        T.Check("Fields page: settings rows and both fields with water, soil test and irrigation", page.Contains("Seeder") && page.Contains("Sowing density") && page.Contains("Field 2") && page.Contains("Soil test") && page.Contains("By hand"), page);
        FieldsPage.Select(f1);
        T.Check("Fields page: Q on a field selects it", FieldsPage.SelectedField == f1);
        int density = ff.Density;
        FieldsPage.Move(-50);
        FieldsPage.Move(3);
        FieldsPage.Adjust(1);
        T.Check("Fields page: Left / Right changes the sowing density", ff.Density == (density + 1) % 3);
        ff.SetDensity(1);

        // tractor: implements paint the field; mission adapter
        var tractor = Tractor.Instance;
        T.Check("Tractor: 11 implement choices with visuals for the new ones", FarmTools.ImplementCount == 11 && FarmTools.Width(Implement.Sprayer) == 12f);
        int idx = Array.FindIndex(mm.Missions, m => m.type == "tractorPlow");
        T.Set(mm, "<Index>k__BackingField", idx); T.Set(mm, "<Progress>k__BackingField", 0f);
        var fresh = ff.Fields[0];
        foreach (var f in new[] { fresh }) { f.ResetSoil(FcData.CurrentRegion); }
        int units = 0; Action<Implement> count = i => { if (i == Implement.Plow) units++; };
        GameEvents.TractorWorked += count;
        T.SetProp(tractor, "Speed", 3f);
        for (float x = -4f; x <= 4f; x += 2.4f)
        {
            Vector3 a = fresh.center + fresh.Rotation * new Vector3(x, 0, -8f), b = fresh.center + fresh.Rotation * new Vector3(x, 0, 8f);
            FarmTools.ResetPass(tractor);
            FarmTools.TractorWork(tractor, Implement.Plow, a, 3f);
            for (float t = 0.05f; t <= 1.0001f; t += 0.05f) FarmTools.TractorWork(tractor, Implement.Plow, Vector3.Lerp(a, b, t), 3f);
        }
        GameEvents.TractorWorked -= count;
        T.SetProp(tractor, "Speed", 0f);
        int pl = FC.Count(fresh, i => fresh.ground[i] == (byte)Ground.Ploughed);
        T.Check("Tractor: driving the plough over the field ploughs the strips under it", pl >= fresh.Cells - 2, pl + " of " + fresh.Cells);
        T.Check("Missions: 'plow a field' counts one unit per 36 m2 (an old plot) and completes", units >= (int)(pl * 4f / 36f) && units <= (int)(pl * 4f / 36f) + 1 && mm.Index > idx, units + " units, mission " + mm.Current?.id);
        T.Check("Tractor: ploughing stops at walking speed and off the field", FarmTools.TractorWork(tractor, Implement.Plow, fresh.center, 0.2f) == 0 && FarmTools.TractorWork(tractor, Implement.Plow, fresh.center + new Vector3(0, 0, 60f), 3f) == 0);
        bool lowered = false; FarmTools.Lowered = t => lowered;
        FarmTools.ResetPass(tractor);
        fresh.ResetSoil(FcData.CurrentRegion);
        FarmTools.TractorWork(tractor, Implement.Plow, fresh.center - new Vector3(0, 0, 4f), 3f);
        int raised = FarmTools.TractorWork(tractor, Implement.Plow, fresh.center + new Vector3(0, 0, 4f), 3f);
        FarmTools.Lowered = t => true;
        T.Check("Tractor: a raised implement doesn't touch the ground (vehicle hook)", raised == 0 && FC.AllUntilled(fresh));

        // seeder -> plant missions; harvester -> tank -> loading bay; harvest missions
        int plantIdx = Array.FindIndex(mm.Missions, m => m.type == "plantField");
        T.Set(mm, "<Index>k__BackingField", plantIdx); T.Set(mm, "<Progress>k__BackingField", 0f);
        string planted = null; Action<FarmField, string> onPlant = (f, c) => planted = c;
        FieldEvents.Planted += onPlant;
        eco.AddSeeds("wheat", 4);
        FC.Prepare(fresh);
        FC.Sow(fresh, "wheat");
        FieldEvents.Planted -= onPlant;
        T.Check("Missions: sowing a field raises 'planted' once per crop cycle ('plant a field' done)", planted == "wheat" && mm.Index > plantIdx, planted + " " + mm.Current?.id);
        FC.Ripen(fresh);
        int hIdx = Array.FindIndex(mm.Missions, m => m.type == "harvestField");
        T.Set(mm, "<Index>k__BackingField", hIdx); T.Set(mm, "<Progress>k__BackingField", 0f);
        float harvestedKg = 0f; Action<FarmField, string, float> onH = (f, c, k) => harvestedKg += k;
        FieldEvents.Harvested += onH;
        float expect = FarmSim.Summarize(fresh).expectedKg;
        ff.UnloadTank();
        FC.Whole(fresh, FieldTool.Harvester, true);
        FieldEvents.Harvested -= onH;
        T.Check("Harvester: the grain goes into the tank (one harvest event with the field's kg)", Math.Abs(ff.Tank.kg - expect) < 1f && Math.Abs(harvestedKg - expect) < 1f && ff.Tank.crop == "wheat", ff.Tank.kg + " vs " + expect + ", event " + harvestedKg);
        T.Check("Missions: 'harvest X kg on the fields' counts the field harvest", mm.Index > hIdx || Math.Abs(mm.Progress - harvestedKg) < 1f, mm.Progress + " " + mm.Current?.id);
        float stored = eco.Stored("wheat");
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position;
        FakeEngine.Run(0.2f);
        T.Check("Harvester: the tank empties into farm storage at the loading bay", ff.Tank.kg < 0.01f && eco.Stored("wheat") > stored + expect - 1f);
        T.Check("History: the harvest is recorded with yield %, N and water", fresh.history.Contains("wheat") && fresh.lastCrop == "wheat");

        // renderer
        FC.Prepare(f1); FC.Sow(f1, "wheat");
        FakeEngine.Run(0.3f);
        T.Check("Crops are drawn instanced per chunk (no GameObjects per plant)", FarmFieldRenderer.Instance.InstanceCount >= FC.Sown(f1) && f1.View.TextureUploads > 0, FarmFieldRenderer.Instance.InstanceCount + "");

        // growth in real time over a day (the game's clock)
        FC.Wet(f1, 0.8f);
        float g0 = FarmSim.Summarize(f1).growth;
        T.SetProp(WorldClock.Instance, "DayProgress", 0.3f);   // stay inside the day: a day roll would start the forecast event the restart test expects
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 0.15f, 0.5f);
        T.Check("Growth: fields grow with the in-game clock", FarmSim.Summarize(f1).growth > g0 + 0.005f, g0 + " -> " + FarmSim.Summarize(f1).growth);
        float sim0 = ff.SimHours;
        T.Set(ff, "hoursToday", 2f);   // skipping from 2 h into the day (the clock's day roll, without starting farm events here)
        typeof(FarmFields).GetMethod("OnDay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ff, new object[] { WorldClock.Instance.Day });
        T.Check("Growth: a skipped day is simulated (sleeping, day jump)", ff.SimHours > sim0 + 10f, sim0 + " -> " + ff.SimHours);

        // pests
        FC.Prepare(f1); FC.Sow(f1, "potato");
        T.Check("Pests: aphids infest a growing field and slow it", FieldPests.Infest(f1, "aphids") && FieldPests.GrowthMultiplier(f1) < 0.7f);
        eco.TryBuy("care_neem");
        string pp = obj == null ? "" : f1.View.PromptRaw(f1.center);
        T.Check("Pests: the prompt offers the treatment", pp.Contains("[E] Treat Aphids"), pp);
        ff.HandJob(f1.center);
        T.Check("Pests: E treats the field (uses the spray, protects for days)", f1.pest == "" && CropCareManager.Instance.Items("neem") == 0 && f1.protectDays > 0 && !FieldPests.Infest(f1, "aphids"));
        f1.protectDays = 0;
        FieldPests.Infest(f1, "aphids");
        eco.TryBuy("care_pesticide");
        ff.SetProduct(true, "pesticide");
        FC.Whole(f1, FieldTool.Sprayer);
        T.Check("Pests: the sprayer with pesticide covers the field", f1.pest == "", f1.pest);
        f1.protectDays = 0;
        CropCareManager.Instance.LocustSwarm();
        T.Check("Pests: a locust swarm sets back growth and leaves locusts", f1.pest == "locust");

        // irrigation in the scene
        var irr = IrrigationSystem.Instance;
        int money0 = eco.Money;
        string why;
        var w = irr.Place(IrrKind.Well, f1.center + new Vector3(-12f, 0, 0), -1f, out why);
        var sp = irr.Place(IrrKind.Sprinkler, f1.center, 8f, out why);
        var sens = irr.Place(IrrKind.Sensor, f1.center + new Vector3(1f, 0, 1f), -1f, out why);
        var pipe = irr.Connect(w.id, sp.id, 75, out why);
        FakeEngine.Tick(0.02f);
        T.Check("Irrigation: parts and pipes are bought (well, sprinkler, sensor, 12 m of pipe)", w != null && sp != null && sens != null && pipe != null && eco.Money < money0 - 3000 && irr.VisualCount >= 4, why);
        T.Check("Irrigation: an emitter on a field with a sensor runs automatically", sp.mode == IrrMode.Auto);
        irr.Configure(sp.id, IrrMode.On, 22, 6, 24f, IrrPower.Diesel);
        irr.Net.Node(w.id).power = IrrPower.Diesel;
        FC.Wet(f1, 0.2f);
        int money1 = eco.Money;
        float cw = f1.cycleWaterM3;
        ff.Simulate(1f);
        T.Check("Irrigation: water reaches the field and is metered", irr.Last.fieldM3.ContainsKey(f1.id) && f1.cycleWaterM3 > cw && irr.WaterM3Today > 0f && irr.Last.m3 > 0f, irr.Last.m3 + " m3");
        for (int h = 0; h < 60; h++) { FC.Wet(f1, 0.2f); ff.Simulate(1f); }
        T.Check("Irrigation: the water and pump bill is paid from the farm money", eco.Money < money1 && irr.CostToday > 1f, money1 + " -> " + eco.Money + ", " + irr.CostToday);
        IrrigationPlanner.Instance.Open(f1);
        IrrigationPlanner.Instance.Cycle(1);
        string panel = IrrigationPlanner.Instance.PanelText();
        T.Check("Planner: panel shows the part, price, coverage and every network's supply vs demand", panel.Contains("Irrigation planner") && panel.Contains("Network 1") && panel.Contains("Groundwater"), panel);
        T.Check("Planner: coverage rings shown while open", irr.ShowCoverage);
        IrrigationPlanner.Instance.Close();
        FarmModifiers.IrrigationBanned = true;
        FC.Wet(f1, 0.2f);
        ff.Simulate(1f);
        T.Check("Irrigation: a water ban stops the network", irr.Last.m3 == 0f || FarmSim.Summarize(f1).moisture < 0.21f);
        FarmModifiers.IrrigationBanned = false;
        T.Check("Missions: efficient irrigation = drip or pivot connected to a source", !irr.HasEfficient());
        var dr = irr.Place(IrrKind.Drip, f0.center, -1f, out why);
        irr.Connect(w.id, dr.id, 50, out why);
        T.Check("Missions: drip lines on a connected network count as efficient irrigation", irr.HasEfficient() && MissionManager.StateMissions["installEfficientIrrigation"]() == 1f);

        // hired field hand
        var wm = WorkerManager.Instance;
        f0.ResetSoil(FcData.CurrentRegion);
        eco.AddSeeds("wheat", 3);
        int v0 = ff.Fields.Sum(f => f.Version);
        for (int k = 0; k < 6; k++) wm.Work(WorkerRole.FieldHand);
        T.Check("Workers: the field hand works the fields by hand (water, hoe, sow, harvest)", ff.Fields.Sum(f => f.Version) > v0);

        T.Set(mm, "<Index>k__BackingField", mission0); T.Set(mm, "<Progress>k__BackingField", progress0);
        SaveData.SetInt("mazraa.mission", mission0); SaveData.SetFloat("mazraa.missionProgress", progress0);   // what MissionManager saved while the scene jumped around
    }

    static string savedCells, savedNet;

    /// <summary>Right before the main save: what the restart must bring back.</summary>
    public static void BeforeSave()
    {
        var ff = FarmFields.Instance;
        ff.SaveAll();
        IrrigationSystem.Instance.Save();
        savedCells = ff.Fields[1].EncodeCells();
        savedNet = IrrigationSystem.Instance.Net.Encode();
    }

    public static void AfterRestart()
    {
        var ff = FarmFields.Instance;
        T.Check("FarmCore: field cells restored after a restart", ff.Fields.Count >= 2 && ff.Fields[1].EncodeCells() == savedCells && FC.Sown(ff.Fields[1]) > 0);
        T.Check("FarmCore: irrigation network restored after a restart", IrrigationSystem.Instance.Net.Encode() == savedNet);
    }

    public static void AfterNewGame()
    {
        var ff = FarmFields.Instance;
        T.Check("FarmCore: new game: fresh untilled fields, no network, no supplies", ff.Fields.Count == 2 && ff.Fields.All(FC.AllUntilled) && IrrigationSystem.Instance.Net.Nodes.Count == 0 && ff.Items(FarmFields.KitId) == 0 && ff.Tank.kg == 0f);
    }

    // ------------------------------------------------------------------ translations

    static void Translations()
    {
        var src = string.Join("\n", Directory.GetFiles("../../Assets/Scripts/FarmCore", "*.cs").Select(File.ReadAllText));
        var rx = new System.Text.RegularExpressions.Regex("FcText\\.[RT]\\(\"([^\"]+)\"");
        var keys = rx.Matches(src).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
        keys.AddRange(CellFactors.Names);
        keys.AddRange(Enumerable.Range(0, 10).Select(i => CellFactors.Advice(i)).Where(s => s != ""));
        keys.AddRange(Enum.GetNames(typeof(IrrKind)));
        keys.AddRange(Enum.GetNames(typeof(IrrMode)));
        keys.AddRange(Enum.GetNames(typeof(IrrPower)));
        keys.AddRange(Enum.GetNames(typeof(FieldMapLayer)));
        keys.AddRange(FarmAgronomy.DensityNames);
        keys.AddRange(IrrigationPlanner.EditRows);
        keys.AddRange(new[] { "Germination", "Vegetative", "Flowering", "Ripening", "Ripe", "Pipe", "Edit" });
        keys.AddRange(Enum.GetNames(typeof(Implement)).Where(n => n != "None"));
        keys.AddRange(new[] { "Not enough water", "Pipe too small", "Not connected to a water source", "No seeds - buy them at a Farm Shop", "Choose a product on the Fields page", "This product doesn't fit this machine",
            "The machine is empty - buy more at the Farm Shop", "Cut this crop with the mower", "The harvester tank is full - unload it at the loading bay", "Unload the tank first: it holds another crop",
            "Place it on a field", "There is no canal on this map", "This field already has one", "Not enough money", "Choose two different parts", "Aim at a part to join", "Sent to the host" });
        keys = keys.Distinct().ToList();
        var missing = keys.Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("FarmCore: every text key has Arabic (Loc/89_farmcore.json and older packs)", keys.Count > 150 && missing.Count == 0, keys.Count + " keys, missing: " + string.Join(" | ", missing.Take(40)));
        Loc.SetLanguage(Language.Arabic);
        var f = FarmFields.Instance != null && FarmFields.Instance.Fields.Count > 0 ? FarmFields.Instance.Fields[1] : null;
        string ar = f != null ? f.View.InteractionPrompt + FieldsPage.Text() : "";
        T.Check("FarmCore: prompt and Fields page in Arabic", ar.Any(c => c >= 'ﹰ'), ar.Length > 120 ? ar.Substring(0, 120) : ar);
        Loc.SetLanguage(Language.English);
    }
}
