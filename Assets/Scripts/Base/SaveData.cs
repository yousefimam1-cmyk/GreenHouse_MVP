using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// One JSON save file (persistentDataPath/mazraa_save.json) with a PlayerPrefs-style API.
/// Old PlayerPrefs values are read once as a fallback, so existing progress carries over.
/// Writes are batched: Save() marks the file dirty and it is flushed at most every 2 s and on quit.
/// </summary>
public static class SaveData
{
    [Serializable] private class Entry { public string k; public string v; public int t; } // t: 0 int, 1 float, 2 string
    [Serializable] private class FileData { public int version = 1; public string savedAt; public List<Entry> entries = new List<Entry>(); }

    // [Core] schema versioning hook (Assets/Scripts/Core/SaveMigrations.cs): bump when a key is renamed/changed and add a step there.
    public const int SchemaVersion = 2;
    /// <summary>Version of the file that was read (before migration); 0 when no file existed.</summary>
    public static int LoadedVersion { get; private set; }
    private sealed class Store : ISaveStore
    {
        public bool Has(string key) => map.ContainsKey(key);
        public string Get(string key) { Entry e; return map.TryGetValue(key, out e) ? e.v : null; }
        public void Set(string key, string value, int type) { map[key] = new Entry { k = key, v = value, t = type }; }
        public void Remove(string key) => map.Remove(key);
        public IEnumerable<string> Keys => new List<string>(map.Keys);
    }

    private static Dictionary<string, Entry> map;
    private static Dictionary<string, Entry> homeMap;   // the player's own save while a co-op session uses a throwaway map
    private static bool dirty;
    private static float lastFlush;
    private static GameObject flusher;
    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "mazraa_save.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { map = null; homeMap = null; dirty = false; flusher = null; mainGood = false; LoadedFromBackup = false; }

    /// <summary>The previous good save (kept by every flush) and where a save that could not be read is moved.</summary>
    public static string BackupPath => Path + ".bak";
    public static string CorruptPath => Path + ".corrupt";
    /// <summary>True when the last load had to use the backup (the main file was missing a value or unreadable).</summary>
    public static bool LoadedFromBackup { get; private set; }
    private static bool mainGood;   // the main file on disk was read or written by us this run (safe to keep as the backup)

    /// <summary>Read one save file; false if it is missing, empty or not a save (truncated by a crash, edited by hand...).</summary>
    private static bool TryRead(string file, Dictionary<string, Entry> into)
    {
        try
        {
            if (!File.Exists(file)) return false;
            string text = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(text)) return false;
            var f = JsonUtility.FromJson<FileData>(text);
            if (f == null || f.entries == null) return false;
            foreach (var e in f.entries) if (e != null && e.k != null) into[e.k] = e;
            if (ReferenceEquals(into, map)) LoadedVersion = f.version;   // [Core] schema version of the file that was read
            return true;
        }
        catch (Exception ex) { Debug.LogWarning("[SaveData] Could not read " + System.IO.Path.GetFileName(file) + ": " + ex.Message); return false; }
    }

    /// <summary>True if the file holds a readable save (used before a slot is copied over the main save).</summary>
    public static bool IsReadable(string file) => TryRead(file, new Dictionary<string, Entry>());

    private static void Load()
    {
        if (map != null) return;
        map = new Dictionary<string, Entry>();
        LoadedFromBackup = false; LoadedVersion = 0;
        mainGood = TryRead(Path, map);
        if (!mainGood)
        {
            // Damaged main file: keep it aside (the next flush would overwrite it). Then use a finished temp write
            // (a crash between swapping files) or the last good save.
            map.Clear();
            bool damaged = File.Exists(Path);
            if (damaged) { try { File.Copy(Path, CorruptPath, true); } catch (Exception) { } }
            if (TryRead(Path + ".tmp", map) || (damaged && TryRead(BackupPath, map)))
            {
                LoadedFromBackup = true; dirty = true;
                Debug.LogWarning("[SaveData] Save file was " + (damaged ? "damaged" : "missing") + "; recovered the last good save.");
            }
            else if (damaged) { map.Clear(); LoadedVersion = 0; Debug.LogWarning("[SaveData] Save file was damaged and no backup could be read; starting fresh (kept " + CorruptPath + ")."); }
        }
        // [Core] migrate older files (Assets/Scripts/Core/SaveMigrations.cs), then write them back at the current version
        if (LoadedVersion > 0 && LoadedVersion < SchemaVersion && SaveMigrations.Apply(LoadedVersion, SchemaVersion, new Store()) == SchemaVersion) dirty = true;
        if (flusher != null) return;
        flusher = new GameObject("SaveData Flusher") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(flusher);
        flusher.AddComponent<SaveFlusher>();
    }

    /// <summary>Forget the in-memory values and read the file again (after a save slot was copied over it).</summary>
    public static void Reload() { map = null; dirty = false; LoadedVersion = 0; }

    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    // A key that is in the file but holds a bad value (hand edit, type changed between versions) reads as the default,
    // never as an old PlayerPrefs value from before the save file existed.
    public static int GetInt(string k, int d = 0)
    {
        Load();
        if (!map.TryGetValue(k, out var e)) return homeMap != null ? d : PlayerPrefs.GetInt(k, d);
        if (int.TryParse(e.v, System.Globalization.NumberStyles.Integer, Inv, out var r) || int.TryParse(e.v, out r)) return r;
        if (float.TryParse(e.v, System.Globalization.NumberStyles.Float, Inv, out var fl) && !float.IsNaN(fl) && Math.Abs(fl) < int.MaxValue) return (int)Math.Round(fl);
        return d;
    }
    public static float GetFloat(string k, float d = 0f)
    {
        Load();
        if (!map.TryGetValue(k, out var e)) return homeMap != null ? d : PlayerPrefs.GetFloat(k, d);
        return float.TryParse(e.v, System.Globalization.NumberStyles.Float, Inv, out var r) && !float.IsNaN(r) && !float.IsInfinity(r) ? r : d;
    }
    public static string GetString(string k, string d = "") { Load(); return map.TryGetValue(k, out var e) ? e.v : homeMap != null ? d : PlayerPrefs.GetString(k, d); }
    public static bool HasKey(string k) { Load(); return map.ContainsKey(k) || (homeMap == null && PlayerPrefs.HasKey(k)); }

    /// <summary>True while a co-op client session is active (writes go to a copy that is never saved).</summary>
    public static bool InSession => homeMap != null;

    /// <summary>
    /// Co-op client joined someone's farm: from now on reads and writes use a copy of the save, and the file on disk
    /// keeps the player's own farm. EndSession() switches back (the copy is thrown away).
    /// </summary>
    public static void BeginSession()
    {
        Load();
        if (homeMap != null) return;
        homeMap = map;
        map = new Dictionary<string, Entry>(homeMap);
    }

    public static void EndSession()
    {
        if (homeMap == null) return;
        map = homeMap;
        homeMap = null;
    }

    public static void SetInt(string k, int v) => Set(k, v.ToString(Inv), 0);
    public static void SetFloat(string k, float v) => Set(k, v.ToString("R", System.Globalization.CultureInfo.InvariantCulture), 1);
    public static void SetString(string k, string v) => Set(k, v ?? "", 2);
    public static void DeleteKey(string k) { Load(); map.Remove(k); if (homeMap == null) PlayerPrefs.DeleteKey(k); dirty = true; }

    private static void Set(string k, string v, int t)
    {
        Load();
        if (map.TryGetValue(k, out var e) && e.v == v) return;
        map[k] = new Entry { k = k, v = v, t = t };
        dirty = true;
    }

    /// <summary>Mark for writing (flushed within ~2 s).</summary>
    public static void Save() => dirty = true;

    public static void Flush(bool force = false)
    {
        if (map == null || (!dirty && !force)) return;
        try
        {
            var f = new FileData { version = SchemaVersion, savedAt = DateTime.UtcNow.ToString("o") };   // [Core] schema version
            f.entries.AddRange((homeMap ?? map).Values);   // during a co-op session only the player's own farm is written
            WriteAtomic(Path, JsonUtility.ToJson(f, true));
            dirty = false;
        }
        catch (Exception ex) { Debug.LogWarning("[SaveData] Could not write save: " + ex.Message); }
    }

    /// <summary>
    /// Write a temp file, then swap it in, keeping the old file as the backup. A crash or power cut mid-write leaves
    /// either the old save or the new one on disk, never a half-written file (which used to wipe the farm on next start).
    /// </summary>
    private static void WriteAtomic(string file, string text)
    {
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(file))
        {
            // Only a good save becomes the backup, so a damaged main file can't push out the last good copy.
            if (mainGood) File.Copy(file, BackupPath, true);
            File.Delete(file);
        }
        File.Move(tmp, file);
        mainGood = true;
    }

    /// <summary>Delete the save file and all keys (new game).</summary>
    public static void DeleteAll()
    {
        Load();
        foreach (var k in new List<string>(map.Keys))
        {
            if (k == "mazraa.language" || k.StartsWith("mazraa.gfx") || k.StartsWith("mazraa.vol.")) continue; // keep settings
            if (homeMap == null) PlayerPrefs.DeleteKey(k);
            map.Remove(k);
        }
        dirty = true;
        Flush();
    }

    /// <summary>Called every frame by SaveFlusher.</summary>
    public static void Tick() { if (dirty && Time.unscaledTime - lastFlush > 2f) { lastFlush = Time.unscaledTime; Flush(); } }
}
