using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Key/value view of a save file that migrations edit (SaveData implements it over its in-memory map).</summary>
public interface ISaveStore
{
    bool Has(string key);
    string Get(string key);
    /// <summary>type: 0 int, 1 float, 2 string (SaveData's entry tags).</summary>
    void Set(string key, string value, int type);
    void Remove(string key);
    IEnumerable<string> Keys { get; }
}

/// <summary>
/// Save schema versioning. The save file carries <c>version</c>; when the game loads an older file every step from
/// that version up to <see cref="SaveData.SchemaVersion"/> runs in order, then the file is written back with the new
/// version. A step must be idempotent and never throw on missing keys (players skip builds). Register steps with
/// <see cref="Register"/> from a static constructor or at startup; the built-in ones are below.
/// Add a step whenever a key is renamed, a unit changes or a value is split, and bump SaveData.SchemaVersion.
/// </summary>
public static class SaveMigrations
{
    private static readonly SortedDictionary<int, List<Action<ISaveStore>>> steps = new SortedDictionary<int, List<Action<ISaveStore>>>();
    private static bool builtInRegistered;

    /// <summary>Versions applied by the last Apply call (tests, log).</summary>
    public static readonly List<int> LastApplied = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { steps.Clear(); builtInRegistered = false; LastApplied.Clear(); }

    /// <summary>Run <paramref name="step"/> when upgrading a file of version <paramref name="fromVersion"/> to fromVersion + 1.</summary>
    public static void Register(int fromVersion, Action<ISaveStore> step)
    {
        if (step == null) return;
        List<Action<ISaveStore>> l;
        if (!steps.TryGetValue(fromVersion, out l)) steps[fromVersion] = l = new List<Action<ISaveStore>>();
        l.Add(step);
    }

    public static int StepCount { get { int n = 0; foreach (var l in steps.Values) n += l.Count; return n; } }

    /// <summary>
    /// Upgrade <paramref name="store"/> from <paramref name="fileVersion"/> to <paramref name="targetVersion"/>.
    /// Returns the version the store is at afterwards (targetVersion unless a step threw, which stops there).
    /// </summary>
    public static int Apply(int fileVersion, int targetVersion, ISaveStore store)
    {
        EnsureBuiltIn();
        LastApplied.Clear();
        if (store == null) return fileVersion;
        if (fileVersion <= 0) fileVersion = 1;
        if (fileVersion > targetVersion)
        {
            Debug.LogWarning("[SaveMigrations] Save is from a newer game (v" + fileVersion + " > v" + targetVersion + "); loading as is.");
            return fileVersion;
        }
        int v = fileVersion;
        for (; v < targetVersion; v++)
        {
            List<Action<ISaveStore>> l;
            if (steps.TryGetValue(v, out l))
            {
                foreach (var s in l)
                {
                    try { s(store); }
                    catch (Exception e)
                    {
                        Debug.LogError("[SaveMigrations] Step v" + v + " -> v" + (v + 1) + " failed: " + e.Message + ". Keeping the save at v" + v + ".");
                        return v;
                    }
                }
            }
            LastApplied.Add(v);
        }
        return v;
    }

    // ---------------------------------------------------------------------------------------------- helpers
    /// <summary>Rename a key, keeping the newer value when both exist.</summary>
    public static void Rename(ISaveStore s, string from, string to, int type)
    {
        if (!s.Has(from)) return;
        if (!s.Has(to)) s.Set(to, s.Get(from), type);
        s.Remove(from);
    }

    /// <summary>Multiply a float key (unit change), e.g. litres -> m3.</summary>
    public static void Scale(ISaveStore s, string key, float factor)
    {
        float v;
        if (s.Has(key) && float.TryParse(s.Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
            s.Set(key, (v * factor).ToString("R", System.Globalization.CultureInfo.InvariantCulture), 1);
    }

    // ---------------------------------------------------------------------------------------------- built-in steps
    private static void EnsureBuiltIn()
    {
        if (builtInRegistered) return;
        builtInRegistered = true;
        // v1 -> v2: stamp which schema the save came from (support / telemetry). Keys are otherwise unchanged; this
        // step exists so the pipeline is exercised on every old save before a real key change needs it.
        Register(1, s =>
        {
            if (!s.Has("mazraa.save.origin")) s.Set("mazraa.save.origin", "1", 0);
        });
    }
}
