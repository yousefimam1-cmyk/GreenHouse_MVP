// Core architecture pieces (Assets/Scripts/Core): tick scheduler + driver, typed signals, save schema migrations,
// content-pack registry. Pure logic first, then the pieces plugged into the running game world.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

static class CoreTests
{
    public static void Run()
    {
        Console.WriteLine("Core: tick scheduler, signals, save migrations, content packs");
        Scheduler();
        SignalsTests();
        Migrations();
        Packs();
        InGame();
    }

    // ------------------------------------------------------------------------------------------ TickScheduler
    static void Scheduler()
    {
        var s = new TickScheduler();
        int frames = 0, quarters = 0, seconds = 0, days = 0; float lastDt = 0f;
        s.EveryFrame(dt => { frames++; lastDt = dt; }, null, "f");
        var q = s.EveryQuarter(dt => quarters++, null, "q");
        s.EverySecond(dt => seconds++, null, "s");
        s.EveryDay(d => days += d, null, "d");
        T.Check("Tick: subscriptions counted per rate", s.Count(TickRate.Frame) == 1 && s.Count(TickRate.Quarter) == 1 && s.Count(TickRate.Second) == 1 && s.Count(TickRate.Daily) == 1 && s.Count() == 4);
        for (int i = 0; i < 100; i++) s.Advance(0.1f);   // 10 s
        T.Check("Tick: frame callback every frame with dt", frames == 100 && Math.Abs(lastDt - 0.1f) < 1e-5f, frames + " " + lastDt);
        T.Check("Tick: quarter ~40 and second ~10 calls in 10 s (phase staggered)", quarters >= 38 && quarters <= 41 && seconds >= 9 && seconds <= 11, quarters + " / " + seconds);
        T.Check("Tick: daily not fired by time alone", days == 0);
        s.RaiseDaily(7); s.RaiseDaily(8);
        T.Check("Tick: RaiseDaily passes the day number", days == 15, days + "");
        // dispose stops it
        q.Dispose(); int qBefore = quarters;
        for (int i = 0; i < 20; i++) s.Advance(0.1f);
        T.Check("Tick: disposed handle stops and is removed", quarters == qBefore && s.Count(TickRate.Quarter) == 0 && !q.Active);
        // phase stagger: two second-subscribers do not fire on the same frame
        var s2 = new TickScheduler(); var fired = new List<(int sub, int frame)>(); int fr = 0;
        for (int k = 0; k < 4; k++) { int id = k; s2.EverySecond(dt => fired.Add((id, fr)), null, "s" + k); }
        for (fr = 0; fr < 40; fr++) s2.Advance(1f / 30f);
        var framesUsed = fired.Select(f => f.frame).Distinct().Count();
        T.Check("Tick: 4 one-second systems spread over different frames", fired.Count == 4 && framesUsed == 4, string.Join(",", fired.Select(f => f.frame)));
        // hitch: one long dt runs the callback once with the whole elapsed time
        var s3 = new TickScheduler(); int hits = 0; float got = 0f;
        s3.EverySecond(dt => { hits++; got = dt; });
        s3.Advance(5f);
        T.Check("Tick: a 5 s hitch calls a 1 s subscriber once with dt 5", hits == 1 && Math.Abs(got - 5f) < 1e-4f, hits + " " + got);
        // exceptions are isolated and the offender is dropped after 3 failures
        var s4 = new TickScheduler(); int good = 0; int e0 = FakeEngine.Errors.Count;
        s4.EveryFrame(dt => throw new InvalidOperationException("boom"), null, "bad");
        s4.EveryFrame(dt => good++, null, "good");
        for (int i = 0; i < 5; i++) s4.Advance(0.016f);
        T.Check("Tick: a throwing callback never stops the others", good == 5, good + "");
        T.Check("Tick: offender logged and dropped after 3 failures", s4.Dropped == 1 && s4.Count(TickRate.Frame) == 1 && FakeEngine.Errors.Count == e0 + 3 && FakeEngine.Errors.Last().Contains("dropped"), (FakeEngine.Errors.Count - e0) + "");
        FakeEngine.Errors.RemoveRange(e0, FakeEngine.Errors.Count - e0);
        // owner destroyed -> callback stops
        var s5 = new TickScheduler(); var go = new GameObject("owner"); int owned = 0;
        s5.EveryFrame(dt => owned++, go, "owned");
        s5.Advance(0.1f); UnityEngine.Object.Destroy(go); s5.Advance(0.1f); s5.Advance(0.1f);
        T.Check("Tick: callback stops when its owner is destroyed", owned == 1 && s5.Count() == 0, owned + "");
        // subscribing from inside a callback is safe
        var s6 = new TickScheduler(); int inner = 0;
        s6.EveryFrame(dt => { if (s6.Count() == 1) s6.EveryFrame(d2 => inner++); });
        s6.Advance(0.1f); s6.Advance(0.1f);
        T.Check("Tick: subscribing during a tick runs from the next tick", s6.Count() == 2 && inner == 1, inner + "");
        T.Check("Tick: report line", s6.Report().StartsWith("frame 2"), s6.Report());
    }

    // ------------------------------------------------------------------------------------------ Signals
    static void SignalsTests()
    {
        Signals.ClearAll();
        var sig = Signals.Of<MoneyChangedSignal>();
        int a = 0, b = 0, once = 0; int e0 = FakeEngine.Errors.Count;
        var ta = sig.Subscribe(v => a += v.Delta);
        sig.Subscribe(v => throw new Exception("handler bug"));
        var tb = sig.Subscribe(v => b += v.Delta);
        sig.Once(v => once++);
        Signals.Raise(new MoneyChangedSignal { Balance = 110, Delta = 10, Reason = "test" });
        Signals.Raise(new MoneyChangedSignal { Balance = 120, Delta = 10 });
        T.Check("Signals: same instance per payload type", ReferenceEquals(sig, Signals.Of<MoneyChangedSignal>()) && Signals.Topics == 1);
        T.Check("Signals: all handlers run even when one throws", a == 20 && b == 20 && sig.Raised == 2, a + " " + b);
        T.Check("Signals: throwing handler logged, not propagated", FakeEngine.Errors.Count == e0 + 2 && FakeEngine.Errors.Last().Contains("handler bug"));
        FakeEngine.Errors.RemoveRange(e0, FakeEngine.Errors.Count - e0);
        T.Check("Signals: Once runs a single time", once == 1);
        ta.Dispose();
        Signals.Raise(new MoneyChangedSignal { Balance = 130, Delta = 10 });
        T.Check("Signals: disposed token stops delivery", a == 20 && b == 30 && sig.Last.Balance == 130);
        int replay = 0; sig.SubscribeReplay(v => replay = v.Balance);
        T.Check("Signals: SubscribeReplay delivers the last value immediately", replay == 130);
        var go = new GameObject("listener"); int owned = 0;
        sig.Subscribe(v => owned++, go);
        Signals.Raise(new MoneyChangedSignal()); UnityEngine.Object.Destroy(go); Signals.Raise(new MoneyChangedSignal());
        T.Check("Signals: handler tied to a destroyed object is dropped", owned == 1 && sig.Count == 3, owned + " " + sig.Count);
        // subscribe during raise: not called in the same raise
        int late = 0; IDisposable lateTok = null;
        var sub = sig.Subscribe(v => { if (lateTok == null) lateTok = sig.Subscribe(x => late++); });
        Signals.Raise(new MoneyChangedSignal());
        T.Check("Signals: subscribing inside a handler takes effect next raise", late == 0);
        Signals.Raise(new MoneyChangedSignal());
        T.Check("Signals: ... and then receives", late == 1);
        var bag = new Subscriptions(); bag += sub; bag += lateTok; bag += tb;
        T.Check("Signals: Subscriptions bag collects tokens", bag.Count == 3);
        int before = sig.Count; bag.Dispose(); Signals.Raise(new MoneyChangedSignal());
        T.Check("Signals: disposing the bag unsubscribes all of them", sig.Count == before - 3 && bag.Count == 0, before + " -> " + sig.Count);
        Signals.ClearAll();
        T.Check("Signals: ClearAll empties the hub", Signals.Topics == 0);
    }

    // ------------------------------------------------------------------------------------------ Save migrations
    sealed class MemStore : ISaveStore
    {
        public readonly Dictionary<string, (string v, int t)> D = new Dictionary<string, (string, int)>();
        public bool Has(string k) => D.ContainsKey(k);
        public string Get(string k) => D.TryGetValue(k, out var e) ? e.v : null;
        public void Set(string k, string v, int t) => D[k] = (v, t);
        public void Remove(string k) => D.Remove(k);
        public IEnumerable<string> Keys => D.Keys.ToList();
    }

    static void Migrations()
    {
        T.Static(typeof(SaveMigrations), "Reset");
        var m = new MemStore(); m.Set("mazraa.day", "12", 0); m.Set("old.water", "2500", 1);
        T.Check("Save: schema version is 2 and the file class writes it", SaveData.SchemaVersion == 2);
        int v = SaveMigrations.Apply(1, SaveData.SchemaVersion, m);
        T.Check("Save: built-in v1->v2 step stamps the origin and keeps data", v == 2 && m.Get("mazraa.save.origin") == "1" && m.Get("mazraa.day") == "12" && SaveMigrations.LastApplied.SequenceEqual(new[] { 1 }));
        T.Check("Save: migrating an up-to-date store is a no-op", SaveMigrations.Apply(2, 2, m) == 2 && SaveMigrations.LastApplied.Count == 0);
        T.Check("Save: a newer file is loaded as is (warning, no steps)", SaveMigrations.Apply(5, 2, m) == 5);
        // custom chain with helpers
        SaveMigrations.Register(2, s => SaveMigrations.Rename(s, "old.water", "mazraa.water.m3", 1));
        SaveMigrations.Register(3, s => SaveMigrations.Scale(s, "mazraa.water.m3", 0.001f));
        v = SaveMigrations.Apply(2, 4, m);
        T.Check("Save: steps chain v2->v3->v4 (rename, unit scale)", v == 4 && !m.Has("old.water") && Math.Abs(float.Parse(m.Get("mazraa.water.m3"), System.Globalization.CultureInfo.InvariantCulture) - 2.5f) < 1e-4f && SaveMigrations.LastApplied.SequenceEqual(new[] { 2, 3 }), m.Get("mazraa.water.m3"));
        int e0 = FakeEngine.Errors.Count;
        SaveMigrations.Register(4, s => throw new Exception("bad step"));
        v = SaveMigrations.Apply(4, 6, m);
        T.Check("Save: a failing step stops at its version and logs", v == 4 && FakeEngine.Errors.Count == e0 + 1);
        FakeEngine.Errors.RemoveRange(e0, FakeEngine.Errors.Count - e0);
        T.Static(typeof(SaveMigrations), "Reset");

        // real file: a v1 save on disk is upgraded on load and written back as v2
        SaveData.Flush(true);
        var path = SaveData.Path;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "{\"version\":1,\"savedAt\":\"x\",\"entries\":[{\"k\":\"mazraa.day\",\"v\":\"42\",\"t\":0},{\"k\":\"mazraa.money\",\"v\":\"777\",\"t\":0}]}");
        SaveData.Reload();
        int day = SaveData.GetInt("mazraa.day", 0);
        T.Check("Save: v1 file loads (values intact) and reports its version", day == 42 && SaveData.LoadedVersion == 1 && SaveData.GetInt("mazraa.save.origin", -1) == 1, day + " v" + SaveData.LoadedVersion);
        SaveData.Flush();
        var written = File.ReadAllText(path);
        T.Check("Save: migrated file is written back at schema v2", written.Contains("\"version\": 2") || written.Contains("\"version\":2"), written.Substring(0, Math.Min(60, written.Length)));
        SaveData.Reload();
        SaveMigrations.LastApplied.Clear(); SaveData.Reload();
        T.Check("Save: reloading the v2 file runs no migration", SaveData.GetInt("mazraa.day", 0) == 42 && SaveData.LoadedVersion == 2 && SaveMigrations.LastApplied.Count == 0, SaveData.LoadedVersion + " " + SaveMigrations.LastApplied.Count);
        // no file at all
        File.Delete(path); SaveData.Reload(); SaveData.GetInt("x", 0);
        T.Check("Save: no file -> version 0, nothing to migrate", SaveData.LoadedVersion == 0);
    }

    // ------------------------------------------------------------------------------------------ Content packs
    static void Packs()
    {
        T.Static(typeof(ContentPacks), "Reset");
        var all = ContentPacks.All;
        T.Check("Packs: manifests loaded from Resources/Data/Packs (base, nile_delta, 4 coming soon)", all.Count == 6 && ContentPacks.Find("base") != null && ContentPacks.Find("nile_delta") != null, string.Join(",", all.Select(p => p.id)));
        T.Check("Packs: every manifest names regions that exist, no clashes", ContentPacks.Validate().Count == 0, string.Join(" | ", ContentPacks.Validate()));
        T.Check("Packs: Egypt pack requires base, is DLC, owns the 93_ files", ContentPacks.Find("nile_delta").requires.Contains("base") && ContentPacks.Find("nile_delta").dlcId == "nile_delta" && ContentPacks.OwnerOfFile("93_egypt").id == "nile_delta" && ContentPacks.OwnerOfFile("30_crops").id == "base" && ContentPacks.OwnerOfFile("zz_unknown") == null);
        bool unlock = Dlc.UnlockAllInDevBuilds;
        Dlc.UnlockAllInDevBuilds = true;   // editor: everything owned
        var najd = ContentPacks.ForRegion("najd"); var delta = ContentPacks.ForRegion("nile_delta");
        T.Check("Packs: Najd gets only base; Delta gets base then nile_delta (dependency order)", najd.Select(p => p.id).SequenceEqual(new[] { "base" }) && delta.Select(p => p.id).SequenceEqual(new[] { "base", "nile_delta" }), string.Join(",", delta.Select(p => p.id)));
        T.Check("Packs: coming-soon packs never active", ContentPacks.ForRegion("souss").Select(p => p.id).SequenceEqual(new[] { "base" }));
        Dlc.UnlockAllInDevBuilds = false; Dlc.Refresh();
        string keyFile = Dlc.KeyFilePath; bool hadKey = File.Exists(keyFile); string keyText = hadKey ? File.ReadAllText(keyFile) : null;
        if (hadKey) File.Delete(keyFile); Dlc.Refresh();
        T.Check("Packs: without the DLC the Delta pack is not active", ContentPacks.ForRegion("nile_delta").Select(p => p.id).SequenceEqual(new[] { "base" }));
        File.WriteAllText(keyFile, "nile_delta\n"); Dlc.Refresh();
        T.Check("Packs: a DLC key activates it", ContentPacks.ForRegion("nile_delta").Any(p => p.id == "nile_delta"));
        if (hadKey) File.WriteAllText(keyFile, keyText); else File.Delete(keyFile);
        Dlc.Refresh(); Dlc.UnlockAllInDevBuilds = unlock;
        // code registration, dependency pruning, ordering, validation
        ContentPacks.Register(new ContentPackDef { id = "ramadan_2027", nameEn = "Ramadan Nights", regions = new string[0], requires = new[] { "weather_plus" }, filePrefixes = new[] { "r27_" } });
        T.Check("Packs: registered pack listed; missing requirement reported and keeps it inactive", ContentPacks.Find("ramadan_2027") != null && ContentPacks.Validate().Any(x => x.Contains("unknown pack weather_plus")) && !ContentPacks.IsActive("ramadan_2027"));
        ContentPacks.Register(new ContentPackDef { id = "weather_plus", nameEn = "Weather+", regions = new string[0], requires = new[] { "base" }, filePrefixes = new[] { "wp_" } });
        var order = ContentPacks.ForRegion("najd").Select(p => p.id).ToList();
        T.Check("Packs: dependency order base < weather_plus < ramadan_2027", order.IndexOf("base") < order.IndexOf("weather_plus") && order.IndexOf("weather_plus") < order.IndexOf("ramadan_2027") && ContentPacks.Validate().Count == 0, string.Join(",", order));
        ContentPacks.Register(new ContentPackDef { id = "dup_prefix", regions = new[] { "atlantis" }, filePrefixes = new[] { "93_" } });
        var problems = ContentPacks.Validate();
        T.Check("Packs: unknown region and prefix clash are reported", problems.Any(x => x.Contains("unknown region atlantis")) && problems.Any(x => x.Contains("prefix 93_")), string.Join(" | ", problems));
        ContentPacks.Register(new ContentPackDef { id = "a", requires = new[] { "b" } }); ContentPacks.Register(new ContentPackDef { id = "b", requires = new[] { "a" } });
        T.Check("Packs: dependency cycle reported, ordering still terminates", ContentPacks.Validate().Any(x => x.Contains("cycle")) && ContentPacks.Order(ContentPacks.All.ToList()).Count == ContentPacks.All.Count);
        Signals.ClearAll(); ContentChangedSignal got = default; Signals.On<ContentChangedSignal>(v => got = v);
        ContentPacks.Announce();
        T.Check("Packs: Announce raises ContentChangedSignal with the active ids", got.RegionId == Regions.CurrentId && got.PackIds != null && got.PackIds.Contains("base"));
        T.Static(typeof(ContentPacks), "Reset");
        T.Check("Packs: Reset forgets registrations", ContentPacks.All.Count == 6);
        T.Check("Packs: Arabic name", ContentPacks.Name(ContentPacks.Find("nile_delta")) == (Loc.IsArabic ? "دلتا النيل" : "Nile Delta"));
    }

    // ------------------------------------------------------------------------------------------ in the running game
    static void InGame()
    {
        var gm = GameManager.Instance;
        var driver = gm != null ? gm.GetComponent<TickDriver>() : null;
        T.Check("Core: TickDriver added to the GameManager by [MazraaSystem]", driver != null && TickDriver.Instance == driver);
        if (driver == null) return;
        int frames = 0, secs = 0, days = 0; float dtSum = 0f;
        using (var f = Ticks.EveryFrame(dt => { frames++; dtSum += dt; }, driver, "test.frame"))
        using (var s = Ticks.EverySecond(dt => secs++, driver, "test.sec"))
        using (var d = Ticks.EveryDay(day => days = day, driver, "test.day"))
        {
            int f0 = Time.frameCount; FakeEngine.Run(3f, 0.1f); int ran = Time.frameCount - f0;
            T.Check("Core: scheduler advances with the engine (one call per frame, dt summed, 2-3 second ticks)", frames == ran && Math.Abs(dtSum - ran * 0.1f) < 0.01f && secs >= 2 && secs <= 3, frames + "/" + ran + " " + dtSum + " " + secs);
            var clock = WorldClock.Instance; int before = clock.Day;
            clock.NextDay();
            T.Check("Core: WorldClock.DayStarted drives the Daily tick", days == before + 1, days + " vs " + (before + 1));
            Time.timeScale = 0f; int fBefore = frames, sBefore = secs;
            FakeEngine.Run(2f, 0.1f);
            T.Check("Core: paused game (timeScale 0) still ticks frames but no game-time ticks", frames == fBefore + 20 && secs == sBefore, (frames - fBefore) + " " + (secs - sBefore));
            Time.timeScale = 1f;
            T.Check("Core: callback time measured per frame and stays inside the 2 ms budget in tests", TickScheduler.Main.LastFrameMs >= 0f && driver.OverBudgetFrames == 0, TickScheduler.Main.Report());
        }
        int n = TickScheduler.Main.Count(); FakeEngine.Tick(0.1f);
        T.Check("Core: using-blocks unsubscribe the test callbacks (removed on the next tick)", TickScheduler.Main.Count() == n - 3, n + " -> " + TickScheduler.Main.Count());
    }
}
