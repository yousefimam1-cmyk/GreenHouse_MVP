using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crop care supplies (shop "Crop care": cures, fertilizer, manure) and pests in the greenhouses (whitefly on the beds).
/// Pests, rotation and soil on the open fields are FarmCore (FieldPests, FarmFields): they use the cures owned here.
/// Pests roll every morning by season and weather; E on an infested bed uses a matching cure you own, which also
/// protects for a few days. Desert locust swarms hit the fields (FieldPests) and the orchard.
/// Saved under "mazraa.crops.*"; reset on a new game.
/// </summary>
[MazraaSystem(30)]
public class CropCareManager : MonoBehaviour
{
    public static CropCareManager Instance;

    public const float RotationBonus = 1.15f;
    private const string P = "mazraa.crops.";

    public class BedCare { public string pest = ""; public float damage; public int protectDays; }

    private readonly Dictionary<string, BedCare> beds = new Dictionary<string, BedCare>();
    private readonly Dictionary<string, int> items = new Dictionary<string, int>();
    public System.Random Rng = new System.Random();

    private void Awake()
    {
        Instance = this;
        foreach (var c in CropsData.Data.cures) items[c.id] = SaveData.GetInt(P + "item." + c.id, 0);
        RegisterShop();
    }

    private void OnEnable()
    {
        WorldClock.DayStarted += OnDay;
        GameEvents.NewGameStarted += OnNewGame;
        AddOnce(Cropbed.YieldModifiers, BedYield);
        AddOnce(Cropbed.PromptExtras, BedExtras);
        AddOnce(Cropbed.InteractHandlers, BedInteract);
        AddOnce(HudLines.Status, HudLine);
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.NewGameStarted -= OnNewGame;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Hooks are static methods so re-enabling (or a scene reload) never registers them twice.
    private static void AddOnce<T>(List<T> list, T f) { if (!list.Contains(f)) list.Add(f); }

    // ---------------------------------------------------------------- state

    public BedCare Bed(Cropbed bed)
    {
        string key = bed != null ? bed.ProfileKey : "";
        if (beds.TryGetValue(key, out var c)) return c;
        string k = P + "bed." + key + ".";
        c = new BedCare { pest = SaveData.GetString(k + "pest", ""), damage = SaveData.GetFloat(k + "dmg", 0f), protectDays = SaveData.GetInt(k + "prot", 0) };
        beds[key] = c;
        return c;
    }

    private void SaveBed(Cropbed bed)
    {
        var c = Bed(bed); string k = P + "bed." + bed.ProfileKey + ".";
        SaveData.SetString(k + "pest", c.pest); SaveData.SetFloat(k + "dmg", c.damage); SaveData.SetInt(k + "prot", c.protectDays);
        SaveData.Save();
    }

    public int Items(string id) => items.TryGetValue(id, out var n) ? n : 0;
    public void AddItem(string id, int n = 1) { items[id] = Items(id) + n; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save(); }
    public bool UseItem(string id)
    {
        if (Items(id) <= 0) return false;
        items[id]--; SaveData.SetInt(P + "item." + id, items[id]); SaveData.Save();
        return true;
    }

    /// <summary>First cure you own that treats this pest (in the order listed in the data file), or null.</summary>
    public CureDef OwnedCureFor(string pestId)
    {
        if (string.IsNullOrEmpty(pestId)) return null;
        var pest = CropsData.Pest(pestId);
        foreach (var c in CropsData.Data.cures) if ((CropsData.Has(c.cures, pestId) || (pest != null && CropsData.Has(pest.curedBy, c.id))) && Items(c.id) > 0) return c;
        return null;
    }

    // ---------------------------------------------------------------- daily pests

    private void OnDay(int day)
    {
        var clock = WorldClock.Instance;
        string season = clock != null ? WorldClock.SeasonId(clock.Season) : "spring";
        Weather w = clock != null ? clock.Weather : Weather.Sunny;
        if (GameManager.Instance != null)
            foreach (var gh in GameManager.Instance.Greenhouses)
                if (gh != null)
                    foreach (var bed in gh.Cropbeds)
                    {
                        if (bed == null) continue;
                        var b = Bed(bed);
                        if (b.protectDays > 0) b.protectDays--;
                        var pest = CropsData.Pest(b.pest);
                        if (pest != null) b.damage = Mathf.Min(0.5f, b.damage + pest.damagePerDay);
                        else if (b.protectDays <= 0 && bed.IsActive)
                            foreach (var d in CropsData.Data.pests)
                                if (d.bedChance > 0f && CropsData.Has(d.seasons, season) && Rng.NextDouble() < d.bedChance * WeatherFactor(d, w)) { InfestBed(bed, d.id); break; }
                        SaveBed(bed);
                    }
        var locust = CropsData.Pest("locust");
        if (locust != null && CropsData.Has(locust.seasons, season) && Rng.NextDouble() < locust.chance * WeatherFactor(locust, w)) LocustSwarm();
    }

    public static float WeatherFactor(PestDef d, Weather w)
    {
        if (w == Weather.Heatwave) return d.heatwave;
        if (w == Weather.Rain) return d.rain;
        if (w == Weather.Cloudy) return (1f + d.rain) * 0.5f;
        return 1f;
    }

    public bool InfestBed(Cropbed bed, string pestId)
    {
        var b = Bed(bed);
        if (CropsData.Pest(pestId) == null || b.protectDays > 0) return false;
        b.pest = pestId;
        SaveBed(bed);
        CropsText.Say(CropsData.Name(CropsData.Pest(pestId)) + " - " + CropsText.R("Greenhouse"));
        return true;
    }

    /// <summary>A desert locust swarm: every planted field loses growth and gets locusts (unless protected); trees are hit too.</summary>
    public void LocustSwarm()
    {
        FieldPests.LocustSwarm();
        if (OrchardManager.Instance != null) OrchardManager.Instance.LocustDamage(0.1f);
        CropsText.Say(CropsText.R("Desert locust swarm! Spray pesticide on the fields"));
    }

    /// <summary>"+15%" / "-15%" for a rotation multiplier.</summary>
    public static string RotationText(float mult)
    {
        int pct = Mathf.RoundToInt((mult - 1f) * 100f);
        return (pct >= 0 ? "+" : "") + pct + "%";
    }

    public static string FamilyName(string family)
    {
        switch (family)
        {
            case "grain": return "Grains";
            case "legume": return "Legumes";
            case "nightshade": return "Nightshades";
            case "allium": return "Onions & garlic";
            case "cucurbit": return "Melons & squash";
            default: return family;
        }
    }

    // ---------------------------------------------------------------- greenhouse beds

    private static float BedYield(Cropbed bed)
    {
        var me = Instance; if (me == null || bed == null) return 1f;
        var b = me.Bed(bed);
        float m = (1f - Mathf.Clamp01(b.damage)) * (b.pest != "" ? 0.8f : 1f);
        if (b.damage > 0f) { b.damage = 0f; me.SaveBed(bed); } // a new planting starts clean
        return m;
    }

    private static string BedExtras(Cropbed bed)
    {
        var me = Instance; if (me == null || bed == null) return null;
        var b = me.Bed(bed);
        var pest = CropsData.Pest(b.pest);
        if (pest == null) return null;
        var cure = me.OwnedCureFor(b.pest);
        return CropsData.Name(pest) + "! " + (cure != null ? CropsText.R("[E] Treat") + ": " + CropsData.Name(cure) : CropsText.R("Buy a cure (Crop care)"));
    }

    private static bool BedInteract(Cropbed bed)
    {
        var me = Instance; if (me == null || bed == null) return false;
        var b = me.Bed(bed);
        if (b.pest == "") return false;
        var cure = me.OwnedCureFor(b.pest);
        if (cure == null || !me.UseItem(cure.id)) return false;
        string pestName = CropsData.Name(CropsData.Pest(b.pest));
        b.pest = ""; b.protectDays = Mathf.Max(b.protectDays, cure.protectDays);
        me.SaveBed(bed);
        GameEvents.RaiseCustom("cropsCurePest", 1f);
        CropsText.Say(CropsData.Name(cure) + ": " + pestName + " - " + CropsText.R("treated"));
        return true;
    }

    // ---------------------------------------------------------------- shop + HUD

    private void RegisterShop()
    {
        foreach (var cure in CropsData.Data.cures)
        {
            var c = cure;
            ShopCatalog.Register(new ShopEntry
            {
                id = "care_" + c.id,
                category = "Crop care",
                name = () => CropsData.Name(c),
                detail = () => "(" + (Instance != null ? Instance.Items(c.id) : 0) + ")",
                price = () => c.price,
                onBought = () => { if (Instance != null) Instance.AddItem(c.id); },
            });
        }
    }

    private static string HudLine()
    {
        var me = Instance; if (me == null) return null;
        var names = new List<string>();
        int count = 0;
        foreach (var f in FieldPests.Infested())
        {
            count++;
            string n = CropsData.Name(CropsData.Pest(f.pest));
            if (!names.Contains(n)) names.Add(n);
        }
        foreach (var kv in me.beds)
        {
            var pest = CropsData.Pest(kv.Value.pest);
            if (pest == null) continue;
            count++;
            string n = CropsData.Name(pest);
            if (!names.Contains(n)) names.Add(n);
        }
        if (count == 0) return null;
        return Loc.Display(CropsText.R("Pests") + ": " + string.Join(", ", names.ToArray()) + " (" + count + ")");
    }

    private void OnNewGame()
    {
        beds.Clear();
        foreach (var c in CropsData.Data.cures) items[c.id] = 0;
    }
}
