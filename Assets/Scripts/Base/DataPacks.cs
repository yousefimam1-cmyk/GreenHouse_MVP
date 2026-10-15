using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extension points so features can be added as separate files instead of editing shared ones:
/// - Data packs: every JSON in Resources/Data/&lt;folder&gt;/ is loaded in file-name order
///   (Data/Loc: translations, Data/Missions: extra missions, Data/Economy: extra goods/animals/field crops).
/// - [MazraaSystem]: any MonoBehaviour with this attribute is added to the GameManager automatically.
/// </summary>
public static class DataPacks
{
    public static TextAsset[] Load(string folder)
    {
        var a = Resources.LoadAll<TextAsset>("Data/" + folder);
        if (a == null) return new TextAsset[0];
        Array.Sort(a, (x, y) => string.CompareOrdinal(x.name, y.name));
        return a;
    }

    /// <summary>Parse each pack in a folder with JsonUtility; bad files are logged and skipped.</summary>
    public static List<T> LoadAs<T>(string folder) where T : class
    {
        var list = new List<T>();
        foreach (var t in Load(folder))
        {
            try { var o = JsonUtility.FromJson<T>(t.text); if (o != null) list.Add(o); }
            catch (Exception e) { Debug.LogError("[DataPacks] Invalid " + folder + "/" + t.name + ": " + e.Message); }
        }
        return list;
    }
}

/// <summary>Marks a MonoBehaviour that GameManager adds automatically at startup (lower Order first).</summary>
[AttributeUsage(AttributeTargets.Class)]
public class MazraaSystemAttribute : Attribute
{
    public int Order;
    public MazraaSystemAttribute(int order = 0) { Order = order; }
}

[Serializable] public class LocEntry { public string en, ar; }
[Serializable] public class LocPack { public LocEntry[] entries; }

/// <summary>
/// Shop items added by features. Each entry shows up in the Farm Shop under its category page;
/// EconomyManager.PriceOf/TryBuy use it for ids it doesn't know itself.
/// </summary>
public class ShopEntry
{
    public string id;
    public string category = "Other";            // English key, translated with Loc
    public Func<string> name;                   // already translated
    public Func<string> detail;                 // optional, e.g. "(2/4)"
    public Func<int> price;                     // < 0 = not available / fully upgraded
    public Func<string> blockReason;            // optional: English Loc key when it can't be bought (e.g. "Pen is full")
    public Action onBought;
}

public static class ShopCatalog
{
    public static readonly List<ShopEntry> Entries = new List<ShopEntry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Entries.Clear();

    public static void Register(ShopEntry e)
    {
        if (e == null || string.IsNullOrEmpty(e.id)) return;
        Entries.RemoveAll(x => x.id == e.id);
        Entries.Add(e);
    }

    public static ShopEntry Find(string id) => Entries.Find(e => e.id == id);
}

/// <summary>Extra lines for the HUD status panel, registered by features (return null or "" to skip).</summary>
public static class HudLines
{
    public static readonly List<Func<string>> Status = new List<Func<string>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Status.Clear();
}
