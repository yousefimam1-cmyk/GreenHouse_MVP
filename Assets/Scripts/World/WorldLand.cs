using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Expansion farmland on the open world map (WorldLayout.LandDefs): each parcel has a "for sale" sign; buying it
/// (E at the sign, or Buy() from code) pays the price and turns the parcel into a FarmCore field covering its plots
/// (FarmFields.AddWorldField, field id = the parcel's indexBase from FarmFields.WorldFieldBase, so its saved cells never
/// clash with the farm's fields or the land office). Counts as
/// buying land for missions and achievements ("buyLand") and raises "buyWorldLand" for the open-world story.
/// Owned parcels: mazraa.world.land.&lt;map&gt;.&lt;id&gt; (per map; a new game sells them back).
/// </summary>
[MazraaSystem(43)]
public class WorldLand : MonoBehaviour
{
    public static WorldLand Instance { get; private set; }

    private readonly Dictionary<string, FarmField> plots = new Dictionary<string, FarmField>();
    private readonly Dictionary<string, WorldLandSign> signs = new Dictionary<string, WorldLandSign>();
    private OpenWorld world;

    public static string Key(string region, string id) => "mazraa.world.land." + region + "." + id;

    private void Awake() => Instance = this;
    private void OnEnable() => GameEvents.NewGameStarted += OnNewGame;
    private void OnDisable() => GameEvents.NewGameStarted -= OnNewGame;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public bool Owned(LandParcel p) => p != null && world != null && SaveData.GetInt(Key(world.BuiltRegion, p.id), 0) == 1;
    public int OwnedCount { get { int n = 0; if (world?.Layout != null) foreach (var p in world.Layout.Parcels) if (Owned(p)) n++; return n; } }
    /// <summary>The FarmCore field of an owned parcel, or null.</summary>
    public FarmField FieldOf(LandParcel p) => p != null && plots.TryGetValue(p.id, out var f) ? f : null;

    /// <summary>(Re)build signs and owned plots for the world that was just built.</summary>
    public void Apply(OpenWorld w)
    {
        world = w;
        RemovePlots();
        foreach (var s in signs.Values) if (s != null) Destroy(s.gameObject);
        signs.Clear();
        if (w?.Layout == null) return;
        foreach (var p in w.Layout.Parcels)
        {
            if (Owned(p)) CreatePlots(p);
            else CreateSign(p);
            UpdateIcon(p);
        }
    }

    private void CreateSign(LandParcel p)
    {
        var poi = world.Layout.Pois.Find(x => x.parcel == p);
        Vector3 front = poi != null ? poi.gate : p.center;
        Vector3 pos = world.OnGround(front + p.rot * new Vector3(4f, 0f, 0f));
        var sign = WorldLandSign.Create(pos, p.rot, p);
        signs[p.id] = sign;
    }

    private void CreatePlots(LandParcel p)
    {
        var ff = FarmFields.Instance;
        if (ff == null) return;
        plots[p.id] = ff.AddWorldField(p, world.OnGround(p.center));
    }

    private void RemovePlots()
    {
        var ff = FarmFields.Instance;
        foreach (var f in plots.Values) if (ff != null && f != null) ff.RemoveField(f, false);   // cells stay saved (the world can be rebuilt)
        plots.Clear();
    }

    private void UpdateIcon(LandParcel p)
    {
        var poi = world.Layout.Pois.Find(x => x.parcel == p);
        if (poi == null || !world.Places.TryGetValue(poi.id, out var t)) return;
        var icon = t.GetComponent<MinimapIcon>();
        if (icon == null) return;
        icon.color = Owned(p) ? new Color(0.35f, 0.85f, 0.35f) : WorldLayout.KindColor(PoiKind.Land);
        icon.letter = Owned(p) ? "" : "$";
    }

    public string BlockReason(LandParcel p)
    {
        if (p == null || world == null) return "Not for sale";
        if (Owned(p)) return "You already own this land";
        if (FarmFields.Instance == null) return "Not for sale";
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Money < p.price) return "Not enough money";
        return null;
    }

    /// <summary>Buy a parcel: pay, add its field plots, take down the sign. False when it can't be bought.</summary>
    public bool Buy(LandParcel p)
    {
        string why = BlockReason(p);
        if (why != null) { Alert(Loc.T(why)); return false; }
        var eco = EconomyManager.Instance;
        using (Ledger.As(Ledger.Purchases)) eco.AddMoney(-p.price);
        SaveData.SetInt(Key(world.BuiltRegion, p.id), 1);
        SaveData.Save();
        if (signs.TryGetValue(p.id, out var s) && s != null) Destroy(s.gameObject);
        signs.Remove(p.id);
        CreatePlots(p);
        UpdateIcon(p);
        GameEvents.RaiseCustom("buyLand", 1f);
        GameEvents.RaiseCustom("buyWorldLand", 1f);
        Alert(Loc.T("New farmland: {0} plots added beyond the fields", p.Plots));
        return true;
    }

    /// <summary>The sign of the cheapest parcel still for sale (objective marker of the land mission).</summary>
    public Transform NextForSaleSign()
    {
        WorldLandSign best = null;
        foreach (var s in signs.Values) if (s != null && (best == null || s.Parcel.price < best.Parcel.price)) best = s;
        return best != null ? best.transform : null;
    }

    private void OnNewGame()
    {
        foreach (var region in new[] { "najd", "nile_delta" })
            foreach (var l in WorldLayout.LandDefs) SaveData.DeleteKey(Key(region, l.id));
        if (world != null) Apply(world);
    }

    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
