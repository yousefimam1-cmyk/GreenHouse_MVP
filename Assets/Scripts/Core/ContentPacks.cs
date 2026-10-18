using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manifest of one content pack (Resources/Data/Packs/*.json or registered from code). A pack is the unit of
/// shipping content: the base game, a region DLC (Nile Delta), a free seasonal drop. It names the regions it serves,
/// the DLC entitlement it needs, the packs it builds on and the file prefix its JSON data uses in the Data/* folders
/// (e.g. "93_" -> Data/Economy/93_egypt.json, Data/Loc/93_egypt.json ...), so tools and tests can tell which files
/// belong to which pack without a central list.
/// </summary>
[Serializable]
public class ContentPackDef
{
    public string id;
    public string nameEn, nameAr;
    public string version = "1.0";
    /// <summary>Regions this pack adds content for (empty = every region, e.g. the base rules).</summary>
    public string[] regions;
    /// <summary>Entitlement id checked with Dlc.Owns (empty = free / part of the base game).</summary>
    public string dlcId;
    /// <summary>Microsoft Store add-on id (release UI shows it; empty while unpublished).</summary>
    public string storeId;
    /// <summary>Packs that must be active first (load order and validation).</summary>
    public string[] requires;
    /// <summary>File-name prefixes of this pack's files in Data/Economy, Data/Loc, Data/Missions ... (e.g. "93_").</summary>
    public string[] filePrefixes;
    /// <summary>Not yet shipped: listed, never active.</summary>
    public bool comingSoon;
    /// <summary>Lowest save schema this pack's data was written for (SaveData.SchemaVersion); informational.</summary>
    public int minSchema = 1;
}

[Serializable] public class ContentPackFile { public ContentPackDef[] packs; }

/// <summary>
/// Registry of content packs: loads the manifests, resolves which are active for the current region and the owned
/// DLC, orders them by dependency and validates the set (missing dependency, duplicate id, unknown region, prefix
/// clash). Pure data: it does not load the packs' content itself (DataPacks and the systems still do that), so it can
/// be adopted step by step - first by the region/DLC menus and tests, then as the filter in DataPacks.Load.
/// </summary>
public static class ContentPacks
{
    public const string Folder = "Packs";
    public const string BaseId = "base";

    private static List<ContentPackDef> all;
    private static readonly List<ContentPackDef> registered = new List<ContentPackDef>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { all = null; registered.Clear(); }

    /// <summary>Add a pack from code (a feature's static constructor, a test). Replaces a manifest with the same id.</summary>
    public static void Register(ContentPackDef def)
    {
        if (def == null || string.IsNullOrEmpty(def.id)) return;
        registered.RemoveAll(p => p.id == def.id);
        registered.Add(def);
        all = null;
    }

    /// <summary>Forget the loaded manifests (after a DLC key changed or a pack was registered).</summary>
    public static void Invalidate() => all = null;

    /// <summary>Every known pack (manifests from Resources/Data/Packs plus registered ones), in manifest order.</summary>
    public static IReadOnlyList<ContentPackDef> All
    {
        get
        {
            if (all != null) return all;
            all = new List<ContentPackDef>();
            foreach (var f in DataPacks.LoadAs<ContentPackFile>(Folder))
                if (f.packs != null) foreach (var p in f.packs) if (p != null && !string.IsNullOrEmpty(p.id)) Add(p);
            foreach (var p in registered) Add(p);
            if (all.Count == 0) all.Add(new ContentPackDef { id = BaseId, nameEn = "Mazraa", nameAr = "المزرعة", filePrefixes = new string[0] });
            return all;
        }
    }

    private static void Add(ContentPackDef p) { all.RemoveAll(x => x.id == p.id); all.Add(p); }

    public static ContentPackDef Find(string id) { foreach (var p in All) if (p.id == id) return p; return null; }

    /// <summary>The player may use this pack: shipped and (free or owned).</summary>
    public static bool IsOwned(ContentPackDef p) => p != null && !p.comingSoon && (string.IsNullOrEmpty(p.dlcId) || Dlc.Owns(p.dlcId));

    /// <summary>The pack has content for this region (or for every region).</summary>
    public static bool ServesRegion(ContentPackDef p, string regionId) => p != null && (p.regions == null || p.regions.Length == 0 || Array.IndexOf(p.regions, regionId) >= 0);

    /// <summary>Packs that would be active on <paramref name="regionId"/>: owned, serving the region, dependencies met; in load order.</summary>
    public static List<ContentPackDef> ForRegion(string regionId)
    {
        var candidates = new List<ContentPackDef>();
        foreach (var p in All) if (IsOwned(p) && ServesRegion(p, regionId)) candidates.Add(p);
        // drop packs whose requirements are not in the set (repeat until stable)
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                if (p.requires == null) continue;
                foreach (var r in p.requires)
                    if (candidates.Find(c => c.id == r) == null) { candidates.RemoveAt(i--); changed = true; break; }
            }
        }
        return Order(candidates);
    }

    /// <summary>Packs active right now (current region, owned DLC), in load order.</summary>
    public static List<ContentPackDef> Active => ForRegion(Regions.CurrentId);

    public static bool IsActive(string packId) { foreach (var p in Active) if (p.id == packId) return true; return false; }

    /// <summary>Dependency order (requirements first), stable for packs without a relation. Cycles keep manifest order.</summary>
    public static List<ContentPackDef> Order(List<ContentPackDef> packs)
    {
        var result = new List<ContentPackDef>();
        var state = new Dictionary<string, int>();   // 0 new, 1 visiting, 2 done
        var byId = new Dictionary<string, ContentPackDef>();
        foreach (var p in packs) byId[p.id] = p;
        foreach (var p in packs) Visit(p, byId, state, result);
        return result;
    }

    private static void Visit(ContentPackDef p, Dictionary<string, ContentPackDef> byId, Dictionary<string, int> state, List<ContentPackDef> result)
    {
        int s; state.TryGetValue(p.id, out s);
        if (s != 0) return;
        state[p.id] = 1;
        if (p.requires != null) foreach (var r in p.requires) { ContentPackDef d; if (byId.TryGetValue(r, out d)) Visit(d, byId, state, result); }
        state[p.id] = 2;
        result.Add(p);
    }

    /// <summary>Which pack a data file belongs to, by its name prefix (e.g. "93_egypt" -> nile_delta pack); null = base/unknown.</summary>
    public static ContentPackDef OwnerOfFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        ContentPackDef best = null; int bestLen = 0;
        foreach (var p in All)
            if (p.filePrefixes != null)
                foreach (var pre in p.filePrefixes)
                    if (!string.IsNullOrEmpty(pre) && fileName.StartsWith(pre, StringComparison.Ordinal) && pre.Length > bestLen) { best = p; bestLen = pre.Length; }
        return best;
    }

    /// <summary>Problems in the manifest set: duplicate ids, missing requirements, unknown regions, shared prefixes, cycles.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>();
        var prefixOwner = new Dictionary<string, string>();
        var regionIds = new HashSet<string>();
        foreach (var r in Regions.All) regionIds.Add(r.id);
        foreach (var p in All)
        {
            if (!seen.Add(p.id)) problems.Add("duplicate pack id " + p.id);
            if (p.requires != null) foreach (var r in p.requires) if (Find(r) == null) problems.Add(p.id + " requires unknown pack " + r);
            if (p.regions != null) foreach (var r in p.regions) if (!regionIds.Contains(r)) problems.Add(p.id + " names unknown region " + r);
            if (p.filePrefixes != null) foreach (var pre in p.filePrefixes)
            {
                string o;
                if (prefixOwner.TryGetValue(pre, out o) && o != p.id) problems.Add("prefix " + pre + " used by " + o + " and " + p.id);
                prefixOwner[pre] = p.id;
            }
            if (!string.IsNullOrEmpty(p.dlcId) && Regions.Find(p.dlcId) == null && (p.regions == null || p.regions.Length == 0)) problems.Add(p.id + " is DLC but serves every region and names no region entitlement");
        }
        // cycles: a pack that is not reachable after ordering its own requires
        foreach (var p in All) if (p.requires != null && HasCycle(p, new HashSet<string>())) problems.Add("dependency cycle through " + p.id);
        return problems;
    }

    private static bool HasCycle(ContentPackDef p, HashSet<string> path)
    {
        if (!path.Add(p.id)) return true;
        if (p.requires != null) foreach (var r in p.requires) { var d = Find(r); if (d != null && HasCycle(d, path)) return true; }
        path.Remove(p.id);
        return false;
    }

    public static string Name(ContentPackDef p) => p == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(p.nameAr) ? p.nameAr : p.nameEn ?? p.id;

    /// <summary>Tell listeners the active set changed (call after Regions.Select or a DLC unlock).</summary>
    public static void Announce()
    {
        var ids = new List<string>();
        foreach (var p in Active) ids.Add(p.id);
        Signals.Raise(new ContentChangedSignal { RegionId = Regions.CurrentId, PackIds = ids.ToArray() });
    }
}
