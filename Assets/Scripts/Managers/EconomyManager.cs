using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class GoodDef { public string id, nameEn, nameAr, unitEn, unitAr; public int price; public float weight = 1f; }
[Serializable] public class AnimalDef { public string id, nameEn, nameAr, product; public int price, max = 6; public float amount = 1f, interval = 60f, feedPerCycle = 1f; }
[Serializable] public class FeedDef { public string nameEn, nameAr; public int price = 200; public float units = 10f; }
[Serializable] public class FieldCropDef { public string id, nameEn, nameAr; public int seedPrice = 100; public string[] seasons; public float growSeconds = 200f, yieldKg = 60f, r = 0.5f, g = 0.7f, b = 0.3f; }
[Serializable] public class EconomyFile { public GoodDef[] goods; public AnimalDef[] animals; public FeedDef feed; public FieldCropDef[] fieldCrops; }

/// <summary>
/// Money, farm storage (all goods), feed, animals owned and upgrades. Data from Resources/Data/economy.json.
/// Harvests and animal products go into storage; the truck loads storage at the loading bay and
/// sells it at the market. Saved in SaveData.
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    private static readonly float[] CapacityLevels = { 150f, 300f, 500f };
    private static readonly int[] CapacityCosts = { 1500, 3500 };
    private static readonly float[] GrowthLevels = { 1f, 1.35f, 1.7f, 2.1f };
    private static readonly int[] GrowthCosts = { 800, 2000, 4500 };

    public EconomyFile Data { get; private set; }
    public int Money { get; private set; }
    public float Feed { get; private set; }
    public int CapacityLevel { get; private set; }
    public int GrowthLevel { get; private set; }

    private readonly Dictionary<string, float> storage = new Dictionary<string, float>();
    private readonly Dictionary<string, int> animalsOwned = new Dictionary<string, int>();
    private readonly Dictionary<string, int> seeds = new Dictionary<string, int>();

    public float TruckCapacity => CapacityLevels[Mathf.Clamp(CapacityLevel, 0, CapacityLevels.Length - 1)];
    public static float GrowthSpeed => Instance == null ? 1f : GrowthLevels[Mathf.Clamp(Instance.GrowthLevel, 0, GrowthLevels.Length - 1)];
    public int NextCapacityCost => CapacityLevel < CapacityCosts.Length ? CapacityCosts[CapacityLevel] : -1;
    public int NextGrowthCost => GrowthLevel < GrowthCosts.Length ? GrowthCosts[GrowthLevel] : -1;

    private void Awake()
    {
        Instance = this;
        LoadData();
        Money = SaveData.GetInt("mazraa.money", 500);
        Feed = SaveData.GetFloat("mazraa.feed", 5f);
        CapacityLevel = SaveData.GetInt("mazraa.capLevel", 0);
        GrowthLevel = SaveData.GetInt("mazraa.growLevel", 0);
        foreach (var g in Data.goods) storage[g.id] = SaveData.GetFloat("mazraa.store." + g.id, 0f);
        foreach (var a in Data.animals) animalsOwned[a.id] = SaveData.GetInt("mazraa.animal." + a.id, 0);
        foreach (var c in Data.fieldCrops) seeds[c.id] = SaveData.GetInt("mazraa.seed." + c.id, c.id == "wheat" ? 2 : 0);
    }

    private void LoadData()
    {
        TextAsset json = Resources.Load<TextAsset>("Data/economy");
        try { if (json != null) Data = JsonUtility.FromJson<EconomyFile>(json.text); }
        catch (Exception e) { Debug.LogError("[Economy] Invalid economy.json: " + e.Message); }
        if (Data == null) Data = new EconomyFile();
        if (Data.goods == null || Data.goods.Length == 0)
            Data.goods = new[] { new GoodDef { id = "crops", nameEn = "Vegetables", nameAr = "خضروات", unitEn = "kg", unitAr = "كجم", price = 10 } };
        if (Data.animals == null) Data.animals = new AnimalDef[0];
        if (Data.fieldCrops == null) Data.fieldCrops = new FieldCropDef[0];
        if (Data.feed == null) Data.feed = new FeedDef { nameEn = "Animal feed x10", nameAr = "علف ×10" };
    }

    private void OnEnable() => GameEvents.Harvested += OnHarvested;
    private void OnDisable() => GameEvents.Harvested -= OnHarvested;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Greenhouse beds give "crops"; field plots add their own crop good themselves (bed == null).
    private void OnHarvested(Cropbed bed, float kg) { if (bed != null) AddGood("crops", kg); }

    // ---------------- goods ----------------

    public GoodDef Good(string id) => Array.Find(Data.goods, g => g.id == id);
    public AnimalDef Animal(string id) => Array.Find(Data.animals, a => a.id == id);
    public FieldCropDef FieldCrop(string id) => Array.Find(Data.fieldCrops, c => c.id == id);
    public int Seeds(string cropId) => seeds.TryGetValue(cropId, out var v) ? v : 0;
    public bool UseSeed(string cropId)
    {
        if (Seeds(cropId) <= 0) return false;
        seeds[cropId]--;
        Save();
        return true;
    }
    public float Stored(string id) => storage.TryGetValue(id, out var v) ? v : 0f;
    public int Owned(string animalId) => animalsOwned.TryGetValue(animalId, out var v) ? v : 0;

    public float StorageWeight
    {
        get { float w = 0f; foreach (var g in Data.goods) w += Stored(g.id) * g.weight; return w; }
    }

    public void AddGood(string id, float amount)
    {
        if (amount <= 0f || Good(id) == null) return;
        storage[id] = Stored(id) + amount;
        Save();
    }

    /// <summary>Move goods from storage into a cargo dictionary, up to maxWeight. Returns weight moved.</summary>
    public float LoadInto(Dictionary<string, float> cargo, float maxWeight)
    {
        float moved = 0f;
        foreach (var g in Data.goods)
        {
            float have = Stored(g.id);
            if (have <= 0f || moved >= maxWeight) continue;
            float w = Mathf.Max(0.0001f, g.weight);
            float take = Mathf.Min(have, (maxWeight - moved) / w);
            if (take <= 0f) continue;
            storage[g.id] = have - take;
            cargo[g.id] = (cargo.TryGetValue(g.id, out var c) ? c : 0f) + take;
            moved += take * w;
        }
        Save();
        return moved;
    }

    /// <summary>Sell every good in the cargo. Returns money earned.</summary>
    public int SellAll(Dictionary<string, float> cargo) => SellAll(cargo, new List<string>());

    /// <summary>Sell every good in the cargo at today's market prices (contracts first). Returns money earned.</summary>
    public int SellAll(Dictionary<string, float> cargo, List<string> messages)
    {
        float total = 0f, weight = 0f;
        foreach (var kv in cargo)
        {
            var g = Good(kv.Key);
            if (g == null) continue;
            total += kv.Value * g.price;
            weight += kv.Value * g.weight;
        }
        if (MarketManager.Instance != null) total = MarketManager.Instance.Sell(cargo, messages);
        cargo.Clear();
        int earned = Mathf.RoundToInt(total);
        AddMoney(earned);
        GameEvents.RaiseDelivered(weight, earned);
        return earned;
    }

    public void AddMoney(int amount)
    {
        if (amount == 0) return;
        Money = Mathf.Max(0, Money + amount);
        Save();
        GameEvents.RaiseMoneyChanged(Money);
    }

    // ---------------- feed & animals ----------------

    /// <summary>Add one animal without paying (births).</summary>
    public void AddAnimal(string id)
    {
        var a = Animal(id);
        if (a == null || Owned(id) >= a.max) return;
        animalsOwned[id] = Owned(id) + 1;
        Save();
    }

    public bool ConsumeFeed(float units)
    {
        if (Feed < units) return false;
        Feed -= units;
        Save();
        return true;
    }

    // ---------------- shop ----------------

    /// <summary>Buy something by shop id: "feed", "capacity", "growth" or an animal id.</summary>
    public bool TryBuy(string id)
    {
        int cost = PriceOf(id);
        if (cost < 0 || Money < cost) return false;
        var animal = Animal(id);
        if (animal != null && Owned(id) >= animal.max) return false;

        Money -= cost;
        if (id == "feed") Feed += Data.feed.units;
        else if (id == "capacity") CapacityLevel++;
        else if (id == "growth") GrowthLevel++;
        else if (animal != null) animalsOwned[id] = Owned(id) + 1;
        else if (id.StartsWith("seed_")) seeds[id.Substring(5)] = Seeds(id.Substring(5)) + 1;
        Save();
        GameEvents.RaiseMoneyChanged(Money);
        GameEvents.RaiseUpgradeBought(id);
        return true;
    }

    public int PriceOf(string id)
    {
        if (id == "feed") return Data.feed.price;
        if (id == "capacity") return NextCapacityCost;
        if (id == "growth") return NextGrowthCost;
        if (id.StartsWith("seed_")) { var fc = FieldCrop(id.Substring(5)); return fc != null ? fc.seedPrice : -1; }
        var a = Animal(id);
        return a != null ? a.price : -1;
    }

    public void ResetAll()
    {
        Money = 500; Feed = 5f; CapacityLevel = 0; GrowthLevel = 0;
        foreach (var g in Data.goods) storage[g.id] = 0f;
        foreach (var a in Data.animals) animalsOwned[a.id] = 0;
        foreach (var c in Data.fieldCrops) seeds[c.id] = c.id == "wheat" ? 2 : 0;
        Save();
        GameEvents.RaiseMoneyChanged(Money);
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.money", Money);
        SaveData.SetFloat("mazraa.feed", Feed);
        SaveData.SetInt("mazraa.capLevel", CapacityLevel);
        SaveData.SetInt("mazraa.growLevel", GrowthLevel);
        foreach (var kv in storage) SaveData.SetFloat("mazraa.store." + kv.Key, kv.Value);
        foreach (var kv in animalsOwned) SaveData.SetInt("mazraa.animal." + kv.Key, kv.Value);
        foreach (var kv in seeds) SaveData.SetInt("mazraa.seed." + kv.Key, kv.Value);
        SaveData.Save();
    }

    public static string GoodName(GoodDef g) => g == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(g.nameAr) ? g.nameAr : g.nameEn);
    public static string Unit(GoodDef g) => g == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(g.unitAr) ? g.unitAr : g.unitEn);
    public static string AnimalName(AnimalDef a) => a == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(a.nameAr) ? a.nameAr : a.nameEn);
}

public static class FieldCropText
{
    public static string Name(FieldCropDef c) => c == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(c.nameAr) ? c.nameAr : c.nameEn);
}
