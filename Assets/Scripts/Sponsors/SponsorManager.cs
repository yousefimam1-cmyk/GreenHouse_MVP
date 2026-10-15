using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class SponsorBrand { public string id, module, nameEn, nameAr, taglineEn, taglineAr; }
[Serializable] public class SponsorProduct { public string id, brand, kind, crop, nameEn, nameAr; public int amount, price; public float bonus; }
[Serializable] public class SponsorBrandFile { public SponsorBrand[] brands; public SponsorProduct[] products; }

/// <summary>
/// Sponsor brands from Resources/Data/sponsors.json ("brands" + "products"): branded seed packs, fertilizer and equipment
/// in the shop page "Sponsors" (with bonuses), roadside billboards between the farm and the market, and the
/// "buySponsorItem" mission event. Walking or driving past a billboard (or buying) records the sponsor in the
/// collection book. Saved under mazraa.sponsor.*.
/// </summary>
[MazraaSystem(67)]
public class SponsorManager : MonoBehaviour
{
    public static SponsorManager Instance;
    /// <summary>Raised with the product id when a sponsor item is bought (analytics, missions).</summary>
    public static event Action<string> Purchased;

    public readonly List<SponsorBrand> Brands = new List<SponsorBrand>();
    public readonly List<SponsorProduct> Products = new List<SponsorProduct>();
    public readonly List<GameObject> Billboards = new List<GameObject>();

    public int FertilizerCharges => SaveData.GetInt("mazraa.sponsor.fert", 0);
    private float fertBonus = 0.25f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Purchased = null;

    private void Awake()
    {
        Instance = this;
        var json = Resources.Load<TextAsset>("Data/sponsors");
        try
        {
            var f = json != null ? JsonUtility.FromJson<SponsorBrandFile>(json.text) : null;
            if (f != null && f.brands != null) Brands.AddRange(f.brands);
            if (f != null && f.products != null) Products.AddRange(f.products);
        }
        catch (Exception e) { Debug.LogError("[Sponsors] Invalid sponsors.json: " + e.Message); }
        foreach (var p in Products) RegisterShop(p);
    }

    private void Start()
    {
        FieldPlot.FieldHarvested += OnFieldHarvested;
        GameEvents.Harvested += OnHarvested;
        Invoke(nameof(BuildBillboards), 1.5f);
        InvokeRepeating(nameof(CheckBillboards), 3f, 1f);
    }

    private void OnDestroy()
    {
        FieldPlot.FieldHarvested -= OnFieldHarvested;
        GameEvents.Harvested -= OnHarvested;
        if (Instance == this) Instance = null;
    }

    public SponsorBrand Brand(string id) => Brands.Find(b => b.id == id);
    public string BrandName(string id) { var b = Brand(id); return b == null ? id : (Loc.IsArabic && !string.IsNullOrEmpty(b.nameAr) ? b.nameAr : b.nameEn); }
    public static string Tagline(SponsorBrand b) => b == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(b.taglineAr) ? b.taglineAr : b.taglineEn);
    public static string ProductName(SponsorProduct p) => p == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(p.nameAr) ? p.nameAr : p.nameEn);
    public bool Owns(string productId) => SaveData.GetInt("mazraa.sponsor.own." + productId, 0) > 0;

    // ---------------- shop ----------------

    private static void RegisterShop(SponsorProduct p)
    {
        string id = p.id;
        ShopCatalog.Register(new ShopEntry
        {
            id = id,
            category = "Sponsors",
            name = () => { var m = Instance; var pr = m != null ? m.Products.Find(x => x.id == id) : null; return ProductName(pr) + (m != null && pr != null ? " - " + m.BrandName(pr.brand) : ""); },
            detail = () => Instance != null ? Instance.Detail(id) : "",
            price = () => Instance != null ? Instance.PriceOf(id) : -1,
            blockReason = () => Instance != null && Instance.Owns(id) && Instance.IsEquipment(id) ? "Already owned" : null,
            onBought = () => { if (Instance != null) Instance.Bought(id); },
        });
    }

    private bool IsEquipment(string id) { var p = Products.Find(x => x.id == id); return p != null && p.kind == "equipment"; }

    private int PriceOf(string id)
    {
        var p = Products.Find(x => x.id == id);
        if (p == null || (p.kind == "equipment" && Owns(id))) return -1;
        return p.price;
    }

    private string Detail(string id)
    {
        var p = Products.Find(x => x.id == id);
        if (p == null) return "";
        if (p.kind == "fertilizer") return "(" + Loc.Raw("charges") + ": " + FertilizerCharges + ")";
        if (p.kind == "seeds" && EconomyManager.Instance != null) return "(" + EconomyManager.Instance.Seeds(p.crop) + ")";
        return "";
    }

    /// <summary>Apply a bought sponsor product (also used by tests).</summary>
    public void Bought(string id)
    {
        var p = Products.Find(x => x.id == id);
        if (p == null) return;
        var eco = EconomyManager.Instance;
        switch (p.kind)
        {
            case "seeds": if (eco != null) eco.AddSeeds(p.crop, Mathf.Max(1, p.amount)); break;
            case "fertilizer":
                fertBonus = p.bonus > 0f ? p.bonus : 0.25f;
                SaveData.SetInt("mazraa.sponsor.fert", FertilizerCharges + Mathf.Max(1, p.amount));
                SaveData.SetFloat("mazraa.sponsor.fertBonus", fertBonus);
                break;
            case "equipment": SaveData.SetInt("mazraa.sponsor.own." + p.id, 1); break;
        }
        int bought = SaveData.GetInt("mazraa.sponsor.purchases", 0) + 1;
        SaveData.SetInt("mazraa.sponsor.purchases", bought);
        SaveData.Save();
        CollectionBook.Record("sponsors", p.brand);
        GameEvents.RaiseCustom("buySponsorItem", 1f);
        if (Purchased != null) Purchased(id);
    }

    // ---------------- bonuses ----------------

    private float EquipmentBonus(string target)
    {
        float b = 0f;
        foreach (var p in Products) if (p.kind == "equipment" && p.crop == target && Owns(p.id)) b += p.bonus;
        return b;
    }

    private void OnFieldHarvested(FieldPlot plot, string crop, float kg)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || kg <= 0f) return;
        float bonus = EquipmentBonus("field");
        int charges = FertilizerCharges;
        if (charges > 0)
        {
            bonus += SaveData.GetFloat("mazraa.sponsor.fertBonus", fertBonus);
            SaveData.SetInt("mazraa.sponsor.fert", charges - 1);
            SaveData.Save();
        }
        if (bonus > 0f) eco.AddGood(crop, kg * bonus);
    }

    private void OnHarvested(Cropbed bed, float kg)
    {
        if (bed == null || EconomyManager.Instance == null) return;
        float bonus = EquipmentBonus("greenhouse");
        if (bonus > 0f) EconomyManager.Instance.AddGood("crops", kg * bonus);
    }

    // ---------------- billboards ----------------

    private void BuildBillboards()
    {
        var vs = VehicleSpawner.Instance;
        if (vs == null || Brands.Count == 0) return;
        Vector3 a = vs.LoadingBay, b = vs.MarketPos;
        Vector3 dir = b - a; dir.y = 0f;
        if (dir.sqrMagnitude < 1f) return;
        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
        int n = Mathf.Min(Brands.Count, 4);
        for (int i = 0; i < n; i++)
        {
            float t = (i + 1f) / (n + 1f);
            Vector3 pos = Build.OnGround(Vector3.Lerp(a, b, t) + side * (i % 2 == 0 ? 10f : -10f));
            Billboards.Add(BuildBillboard(Brands[i], pos, Quaternion.LookRotation(-side * (i % 2 == 0 ? 1f : -1f))));
        }
    }

    private GameObject BuildBillboard(SponsorBrand brand, Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Billboard " + brand.id);
        root.transform.SetPositionAndRotation(pos, rot);
        var steel = Build.Mat(new Color(0.4f, 0.4f, 0.42f), 0.5f, 0.5f);
        Color c = brand.module == "Irrigation" ? new Color(0.15f, 0.45f, 0.75f) : brand.module == "Energy" ? new Color(0.95f, 0.65f, 0.1f) : new Color(0.2f, 0.55f, 0.25f);
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(-1.6f, 1.8f, 0), new Vector3(0.15f, 1.8f, 0.15f), steel, "Pole");
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(1.6f, 1.8f, 0), new Vector3(0.15f, 1.8f, 0.15f), steel, "Pole");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 4.1f, 0), new Vector3(4.6f, 2.0f, 0.15f), Build.Mat(c, 0.3f), "Board");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 4.1f, 0.09f), new Vector3(4.3f, 1.7f, 0.02f), Build.Mat(new Color(0.97f, 0.95f, 0.9f), 0.2f), "Face");
        string id = brand.id;
        var name = CozyFx.DataLabel(root.transform, new Vector3(0, 4.5f, 0.2f), () => Instance != null ? Instance.BrandName(id) : id, 3f, new Color(0.1f, 0.1f, 0.1f));
        name.GetComponent<Billboard>().enabled = false;
        name.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var tag = CozyFx.DataLabel(root.transform, new Vector3(0, 3.7f, 0.2f), () => Tagline(Instance != null ? Instance.Brand(id) : null), 2.2f, c * 0.6f);
        tag.GetComponent<Billboard>().enabled = false;
        tag.transform.localRotation = Quaternion.Euler(0, 180, 0);
        return root;
    }

    private void CheckBillboards()
    {
        Transform p = Vehicle.Current != null ? Vehicle.Current.transform : CozyFx.Player;
        if (p == null) return;
        foreach (var bb in Billboards)
            if (bb != null && Vector3.Distance(bb.transform.position, p.position) < 18f)
                CollectionBook.Record("sponsors", bb.name.Substring("Billboard ".Length));
    }
}
