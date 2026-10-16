using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soil, crop rotation, pests and diseases for the open fields (and whitefly in the greenhouses).
/// - Soil fertility per field plot (100% at start): every harvest uses some up, more when the same crop family is
///   grown twice in a row; alfalfa (a legume) puts nitrogen back. Fertilizer and manure (shop, "Crop care") restore it.
///   Yield x (0.55 + 0.45 x fertility). Crop rotation multiplies the yield by the family table in Data/soil.json
///   (legume before a cereal +20%, cereal before a nightshade +15%, nightshade / melons / onions twice in a row -15%).
///   The detailed soil (N, P, K, pH, salinity, water...) is SoilManager; fertilizer and manure spread here feed it too.
/// - Pests roll every morning by season, weather and crop family (aphids, whitefly, blight; desert locust swarms
///   are a rare event after rain in winter/spring). Pests slow growth and add yield damage every day until treated:
///   E on the plot uses a matching cure you own (ladybugs, neem spray, pesticide), which also protects for a few days.
/// - Multi-cut crops (alfalfa) regrow after each cut; alfalfa and sorghum/millet straw also add animal feed.
/// Saved under "mazraa.crops.*"; reset on a new game.
/// </summary>
[MazraaSystem(30)]
public class CropCareManager : MonoBehaviour
{
    public static CropCareManager Instance;

    public const float RotationBonus = 1.15f;
    private const string P = "mazraa.crops.";

    /// <summary>Fertilizer or manure from the Crop care page was spread on a plot (SoilManager adds its nutrients).</summary>
    public static event Action<FieldPlot, CureDef> Fertilized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Fertilized = null; }

    public class PlotCare
    {
        public float fertility = 1f, damage, regrow = -1f, rotation = 1f;
        public string lastFamily = "", pest = "";
        public bool rotated;
        public int protectDays, cuts;
    }

    public class BedCare { public string pest = ""; public float damage; public int protectDays; }

    private readonly Dictionary<int, PlotCare> plots = new Dictionary<int, PlotCare>();
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
        FieldPlot.FieldPlanted += OnPlanted;
        FieldPlot.FieldHarvested += OnHarvested;
        GameEvents.UpgradeBought += OnBought;
        GameEvents.NewGameStarted += OnNewGame;
        AddOnce(FieldPlot.GrowthModifiers, PlotGrowth);
        AddOnce(FieldPlot.YieldModifiers, PlotYield);
        AddOnce(FieldPlot.PromptExtras, PlotExtras);
        AddOnce(FieldPlot.PromptOverrides, PlotOverride);
        AddOnce(FieldPlot.InteractHandlers, PlotInteract);
        AddOnce(FieldPlot.RegrowHooks, PlotRegrow);
        AddOnce(Cropbed.YieldModifiers, BedYield);
        AddOnce(Cropbed.PromptExtras, BedExtras);
        AddOnce(Cropbed.InteractHandlers, BedInteract);
        AddOnce(HudLines.Status, HudLine);
    }

    private void OnDisable()
    {
        WorldClock.DayStarted -= OnDay;
        FieldPlot.FieldPlanted -= OnPlanted;
        FieldPlot.FieldHarvested -= OnHarvested;
        GameEvents.UpgradeBought -= OnBought;
        GameEvents.NewGameStarted -= OnNewGame;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Hooks are static methods so re-enabling (or a scene reload) never registers them twice.
    private static void AddOnce<T>(List<T> list, T f) { if (!list.Contains(f)) list.Add(f); }

    // ---------------------------------------------------------------- state

    public PlotCare Plot(int index)
    {
        if (plots.TryGetValue(index, out var c)) return c;
        string k = P + "plot." + index + ".";
        c = new PlotCare
        {
            fertility = SaveData.GetFloat(k + "fert", 1f),
            damage = SaveData.GetFloat(k + "dmg", 0f),
            regrow = -1f,
            lastFamily = SaveData.GetString(k + "last", ""),
            pest = SaveData.GetString(k + "pest", ""),
            rotated = SaveData.GetInt(k + "rot", 0) == 1,
            rotation = SaveData.GetFloat(k + "rotm", -1f),
            protectDays = SaveData.GetInt(k + "prot", 0),
            cuts = SaveData.GetInt(k + "cuts", 0),
        };
        if (c.rotation < 0f) c.rotation = c.rotated ? RotationBonus : 1f;   // saves from before the rotation table
        plots[index] = c;
        return c;
    }

    private void SavePlot(int index)
    {
        var c = Plot(index); string k = P + "plot." + index + ".";
        SaveData.SetFloat(k + "fert", c.fertility); SaveData.SetFloat(k + "dmg", c.damage);
        SaveData.SetString(k + "last", c.lastFamily); SaveData.SetString(k + "pest", c.pest);
        SaveData.SetInt(k + "rot", c.rotated ? 1 : 0); SaveData.SetInt(k + "prot", c.protectDays); SaveData.SetInt(k + "cuts", c.cuts);
        SaveData.SetFloat(k + "rotm", c.rotation);
        SaveData.Save();
    }

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
        foreach (var c in CropsData.Data.cures) if (CropsData.Has(c.cures, pestId) && Items(c.id) > 0) return c;
        return null;
    }

    private CureDef OwnedFertilizer()
    {
        foreach (var c in CropsData.Data.cures) if (c.fertility > 0f && Items(c.id) > 0) return c;
        return null;
    }

    // ---------------------------------------------------------------- multipliers

    public float GrowthMultiplier(FieldPlot p)
    {
        var c = Plot(p.index);
        float m = 1f;
        var pest = CropsData.Pest(c.pest);
        if (pest != null) m *= pest.growthMult;
        if (c.fertility < 0.3f) m *= 0.75f;
        return m;
    }

    public float YieldMultiplier(FieldPlot p)
    {
        var c = Plot(p.index);
        return (0.55f + 0.45f * Mathf.Clamp01(c.fertility)) * c.rotation * (1f - Mathf.Clamp01(c.damage));
    }

    private static float PlotGrowth(FieldPlot p) => Instance != null ? Instance.GrowthMultiplier(p) : 1f;
    private static float PlotYield(FieldPlot p) => Instance != null ? Instance.YieldMultiplier(p) : 1f;
    private static float PlotRegrow(FieldPlot p) => Instance != null ? Instance.Plot(p.index).regrow : -1f;

    // ---------------------------------------------------------------- field events

    private void OnPlanted(FieldPlot p)
    {
        var c = Plot(p.index);
        string family = CropsData.Family(p.CropId);
        c.rotation = SoilData.Rotation(c.lastFamily, family);
        c.rotated = !string.IsNullOrEmpty(c.lastFamily) && c.lastFamily != family && c.rotation > 1.001f;
        c.cuts = 0; c.damage = 0f; c.pest = ""; c.regrow = -1f;
        SavePlot(p.index);
        if (c.rotated)
        {
            GameEvents.RaiseCustom("cropsRotate", 1f);
            CropsText.Say(CropsText.R("Crop rotation") + ": " + RotationText(c.rotation) + " " + CropsText.R("yield"));
        }
        else if (c.rotation < 0.999f)
            CropsText.Say(CropsText.R("Same crop family again: soil-borne disease") + " " + RotationText(c.rotation) + " " + CropsText.R("yield"));
        if (p.CropId == "alfalfa") GameEvents.RaiseCustom("cropsPlantAlfalfa", 1f);
    }

    private void OnHarvested(FieldPlot p, string cropId, float kg)
    {
        var c = Plot(p.index);
        var meta = CropsData.Meta(cropId);
        string family = CropsData.Family(cropId);
        c.cuts++;
        var eco = EconomyManager.Instance;
        if (meta != null && meta.feedPerKg > 0f && eco != null)
        {
            float feed = kg * meta.feedPerKg;
            eco.AddFeed(feed);
            CropsText.Say(CropsText.R("Animal feed") + " +" + feed.ToString("F1"));
        }
        bool more = meta != null && meta.cuts > 1 && c.cuts < meta.cuts;
        if (family == "legume") c.fertility = Mathf.Min(1f, c.fertility + (more ? 0.04f : 0.1f));
        else c.fertility = Mathf.Max(0.1f, c.fertility - 0.12f - (family == c.lastFamily ? 0.1f : 0f));
        if (more) { c.regrow = meta.regrowFrom; c.damage *= 0.5f; }
        else
        {
            c.regrow = -1f; c.lastFamily = family; c.cuts = 0; c.damage = 0f; c.pest = ""; c.rotated = false; c.rotation = 1f;
        }
        SavePlot(p.index);
    }

    private void OnBought(string id)
    {
        if (id != null && id.StartsWith("seed_")) FieldPlot.PreferredSeed = id.Substring(5);
    }

    // ---------------------------------------------------------------- daily pests

    private void OnDay(int day)
    {
        var clock = WorldClock.Instance;
        string season = clock != null ? WorldClock.SeasonId(clock.Season) : "spring";
        Weather w = clock != null ? clock.Weather : Weather.Sunny;
        var fm = FieldManager.Instance;
        if (fm != null)
            foreach (var p in fm.Plots)
            {
                if (p == null) continue;
                var c = Plot(p.index);
                if (c.protectDays > 0) c.protectDays--;
                bool growing = p.State == PlotState.Growing || p.State == PlotState.Ripe;
                var pest = CropsData.Pest(c.pest);
                if (pest != null && growing) c.damage = Mathf.Min(0.6f, c.damage + pest.damagePerDay);
                else if (growing && c.protectDays <= 0)
                {
                    string family = CropsData.Family(p.CropId);
                    foreach (var d in CropsData.Data.pests)
                    {
                        if (d.id == "locust" || !CropsData.Has(d.seasons, season) || !CropsData.Has(d.families, family)) continue;
                        if (Rng.NextDouble() < d.chance * WeatherFactor(d, w)) { Infest(p, d.id); break; }
                    }
                }
                SavePlot(p.index);
            }
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

    private static float WeatherFactor(PestDef d, Weather w)
    {
        if (w == Weather.Heatwave) return d.heatwave;
        if (w == Weather.Rain) return d.rain;
        if (w == Weather.Cloudy) return (1f + d.rain) * 0.5f;
        return 1f;
    }

    /// <summary>Put a pest on a field plot (ignored while the plot is protected or not planted).</summary>
    public bool Infest(FieldPlot p, string pestId)
    {
        var c = Plot(p.index);
        var pest = CropsData.Pest(pestId);
        if (pest == null || c.protectDays > 0 || (p.State != PlotState.Growing && p.State != PlotState.Ripe)) return false;
        c.pest = pestId;
        SavePlot(p.index);
        CropsText.Say(CropsData.Name(pest) + " - " + p.name.Replace("Field", CropsText.R("Field")));
        return true;
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
        var fm = FieldManager.Instance;
        if (fm != null)
            foreach (var p in fm.Plots)
            {
                if (p == null || (p.State != PlotState.Growing && p.State != PlotState.Ripe)) continue;
                var c = Plot(p.index);
                if (c.protectDays > 0) continue;
                p.ReduceGrowth(0.25f);
                c.pest = "locust";
                c.damage = Mathf.Min(0.6f, c.damage + 0.1f);
                SavePlot(p.index);
            }
        if (OrchardManager.Instance != null) OrchardManager.Instance.LocustDamage(0.1f);
        CropsText.Say(CropsText.R("Desert locust swarm! Spray pesticide on the fields"));
    }

    // ---------------------------------------------------------------- prompts and E

    private static string PlotOverride(FieldPlot p)
    {
        var me = Instance; if (me == null) return null;
        var c = me.Plot(p.index);
        if ((p.State == PlotState.Growing || p.State == PlotState.Ripe) && c.pest != "")
        {
            var cure = me.OwnedCureFor(c.pest);
            if (cure != null) return CropsText.R("[E] Treat") + " " + CropsData.Name(CropsData.Pest(c.pest)) + ": " + CropsData.Name(cure) + " (" + me.Items(cure.id) + ")";
        }
        if (p.State == PlotState.Plowed && c.fertility < 0.8f)
        {
            var f = me.OwnedFertilizer();
            if (f != null) return CropsText.R("[E] Spread") + " " + CropsData.Name(f) + " (" + me.Items(f.id) + ") - " + CropsText.R("Soil") + " " + CropsText.Pct(c.fertility);
        }
        return null;
    }

    private static string PlotExtras(FieldPlot p)
    {
        var me = Instance; if (me == null) return null;
        var c = me.Plot(p.index);
        string s = CropsText.R("Soil") + " " + CropsText.Pct(c.fertility);
        if (p.State == PlotState.Growing || p.State == PlotState.Ripe)
        {
            var pest = CropsData.Pest(c.pest);
            if (pest != null) s += " | " + CropsData.Name(pest) + "! " + (me.OwnedCureFor(c.pest) == null ? CropsText.R("Buy a cure (Crop care)") : "");
            if (Mathf.Abs(c.rotation - 1f) > 0.001f) s += " | " + CropsText.R("Rotation") + " " + RotationText(c.rotation);
            var meta = CropsData.Meta(p.CropId);
            if (meta != null && meta.cuts > 1) s += " | " + CropsText.R("Cut") + " " + (c.cuts + 1) + "/" + meta.cuts;
        }
        else if (p.State != PlotState.Untilled && !string.IsNullOrEmpty(c.lastFamily))
            s += " | " + CropsText.R("Last crop family") + ": " + CropsText.R(FamilyName(c.lastFamily));
        return s.TrimEnd();
    }

    private static bool PlotInteract(FieldPlot p)
    {
        var me = Instance; if (me == null) return false;
        var c = me.Plot(p.index);
        if ((p.State == PlotState.Growing || p.State == PlotState.Ripe) && c.pest != "")
        {
            var cure = me.OwnedCureFor(c.pest);
            if (cure == null || !me.UseItem(cure.id)) return false;
            string pestName = CropsData.Name(CropsData.Pest(c.pest));
            c.pest = ""; c.protectDays = Mathf.Max(c.protectDays, cure.protectDays);
            me.SavePlot(p.index);
            GameEvents.RaiseCustom("cropsCurePest", 1f);
            CropsText.Say(CropsData.Name(cure) + ": " + pestName + " - " + CropsText.R("treated"));
            return true;
        }
        if (p.State == PlotState.Plowed && c.fertility < 0.8f)
        {
            var f = me.OwnedFertilizer();
            if (f == null || !me.UseItem(f.id)) return false;
            c.fertility = Mathf.Min(1f, c.fertility + f.fertility);
            me.SavePlot(p.index);
            Fertilized?.Invoke(p, f);
            GameEvents.RaiseCustom("cropsFertilize", 1f);
            CropsText.Say(CropsData.Name(f) + " - " + CropsText.R("Soil") + " " + CropsText.Pct(c.fertility));
            return true;
        }
        return false;
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
        var fm = FieldManager.Instance;
        if (fm != null)
            foreach (var p in fm.Plots)
            {
                if (p == null || (p.State != PlotState.Growing && p.State != PlotState.Ripe)) continue;
                var pest = CropsData.Pest(me.Plot(p.index).pest);
                if (pest == null) continue;
                count++;
                string n = CropsData.Name(pest);
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
        plots.Clear(); beds.Clear();
        foreach (var c in CropsData.Data.cures) items[c.id] = 0;
        FieldPlot.PreferredSeed = "";
    }
}
