using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Three villagers with friendship hearts (0-5): Abu Saleh the shopkeeper (farm shop), Abu Fahad the market buyer and
/// Abu Nasser the neighbour farmer. Talking (E) once a day adds a heart. Perks: shopkeeper 3/5 hearts = 10%/15% back
/// on shop purchases; buyer 3/5 = +5%/+10% market prices; neighbour 3 = date cookie (kleija) recipe tip and learn card,
/// 5 = a seed gift each day you visit. Saved under mazraa.cozy.v.*.
/// </summary>
[MazraaSystem(66)]
public class Villagers : MonoBehaviour
{
    public static Villagers Instance;
    public const int MaxHearts = 5;
    public static readonly string[] Ids = { "shopkeeper", "buyer", "neighbour" };

    public readonly Dictionary<string, GameObject> Npcs = new Dictionary<string, GameObject>();

    private void Awake()
    {
        Instance = this;
        MissionManager.StateMissions["reachHearts"] = BestHearts;
        MarketManager.PriceMultipliers.Remove(BuyerPrice);
        MarketManager.PriceMultipliers.Add(BuyerPrice);
    }

    private void Start()
    {
        Invoke(nameof(Spawn), 1.6f);
        GameEvents.UpgradeBought += OnShopPurchase;
    }

    private void OnDestroy()
    {
        GameEvents.UpgradeBought -= OnShopPurchase;
        MarketManager.PriceMultipliers.Remove(BuyerPrice);
        if (Instance == this) Instance = null;
    }

    public static float BestHearts()
    {
        int best = 0;
        foreach (var id in Ids) best = Mathf.Max(best, Hearts(id));
        return best;
    }

    public static int Hearts(string id) => Mathf.Clamp(SaveData.GetInt("mazraa.cozy.v." + id + ".hearts", 0), 0, MaxHearts);
    public static bool TalkedToday(string id) => SaveData.GetInt("mazraa.cozy.v." + id + ".day", 0) == CozyFx.Today;

    public static string NameOf(string id)
    {
        switch (id)
        {
            case "shopkeeper": return Loc.Raw("Abu Saleh (shopkeeper)");
            case "buyer": return Loc.Raw("Abu Fahad (market buyer)");
            default: return Loc.Raw("Abu Nasser (neighbour farmer)");
        }
    }

    // ---------------- spawning ----------------

    private void Spawn()
    {
        Vector3 start = CozyFx.FarmStart;
        // shopkeeper beside the farm shop (the stall closest to the farm)
        ShopStall farmShop = null; float best = float.MaxValue;
        foreach (var s in FindObjectsByType<ShopStall>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(s.transform.position, start);
            if (d < best) { best = d; farmShop = s; }
        }
        Vector3 shopPos = farmShop != null ? farmShop.transform.position + farmShop.transform.right * 2.4f : start + new Vector3(-6f, 0, 4f);
        Vector3 marketPos = DeliveryZone.Market != null ? DeliveryZone.Market.transform.position + new Vector3(-4f, 0, -7f) : start + new Vector3(0, 0, 40f);
        Vector3 fieldPos = FieldManager.Instance != null && FieldManager.Instance.Plots.Count > 0 ? FieldManager.Instance.Plots[0].transform.position + new Vector3(-6f, 0, -4f) : start + new Vector3(10f, 0, 6f);
        Make("shopkeeper", "Characters/character-male-c", shopPos, ActorMode.Wander, false);
        Make("buyer", "Characters/character-male-a", marketPos, ActorMode.Wander, false);
        Make("neighbour", "Characters/character-male-c", fieldPos, ActorMode.Wander, false);
    }

    private void Make(string id, string model, Vector3 pos, ActorMode mode, bool female)
    {
        pos = Build.OnGround(pos);
        GameObject go;
        var actor = Actor.Spawn(model, 1.55f, true, mode, pos);
        if (actor != null)
        {
            go = actor.gameObject;
            actor.radius = id == "neighbour" ? 3.5f : 0.8f; actor.speed = id == "neighbour" ? 0.8f : 0.5f;   // the shopkeeper and buyer stay near their spot
        }
        else
        {
            go = new GameObject("Villager " + id);
            go.transform.position = pos;
            CozyFx.PrimitivePerson(go.transform, Appearance.ThobeColors[0]);
        }
        go.name = "Villager " + id + " (" + model + ")";
        AppearancePanel.DressNpc(go, female, id.Length);
        CozyFx.Label(go.transform, new Vector3(0, 2.15f, 0), id == "shopkeeper" ? "Abu Saleh" : id == "buyer" ? "Abu Fahad" : "Abu Nasser", 2f, new Color(1f, 0.9f, 0.7f));
        string vid = id;
        CozyFx.MakeInteractable(go, new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f), () => Prompt(vid), () => Talk(vid));
        Npcs[id] = go;
    }

    private static string Prompt(string id)
    {
        string hearts = new string('*', Hearts(id)) + new string('-', MaxHearts - Hearts(id));
        return Loc.Display(Loc.Raw("[E] Talk to") + " " + NameOf(id) + "  " + hearts);
    }

    // ---------------- talking ----------------

    /// <summary>Chat with a villager: once per day adds a heart (max 5) and may unlock a perk. Returns the line said.</summary>
    public string Talk(string id)
    {
        GameObject go;
        if (Npcs.TryGetValue(id, out go) && go != null)
        {
            var a = go.GetComponent<Actor>();
            if (a != null) a.React("emote-yes", 1.2f);
        }
        string line;
        if (TalkedToday(id))
        {
            line = NameOf(id) + ": " + Loc.Raw("Good to see you again! Come by tomorrow for a proper chat.");
            CozyFx.Alert(Loc.Display(line));
            return line;
        }
        int hearts = Mathf.Min(MaxHearts, Hearts(id) + 1);
        SaveData.SetInt("mazraa.cozy.v." + id + ".hearts", hearts);
        SaveData.SetInt("mazraa.cozy.v." + id + ".day", CozyFx.Today);
        SaveData.Save();
        if (go != null) CozyFx.Hearts(go.transform.position + Vector3.up * 2f);
        line = NameOf(id) + ": " + Loc.Raw(Line(id, hearts));
        if (hearts == 3) line += "  " + Loc.Raw(PerkText(id, 3));
        if (hearts == 5 && Hearts(id) == 5 && SaveData.GetInt("mazraa.cozy.v." + id + ".best", 0) == 0)
        {
            SaveData.SetInt("mazraa.cozy.v." + id + ".best", 1);
            line += "  " + Loc.Raw(PerkText(id, 5));
        }
        if (id == "neighbour" && hearts >= 3) LearnMode.Trigger("villager:neighbour");
        if (id == "neighbour" && hearts >= 5 && EconomyManager.Instance != null && EconomyManager.Instance.Data.fieldCrops.Length > 0)
        {
            var c = EconomyManager.Instance.Data.fieldCrops[CozyFx.Today % EconomyManager.Instance.Data.fieldCrops.Length];
            EconomyManager.Instance.AddSeeds(c.id, 1);
            line += "  " + Loc.Raw("Seeds") + ": " + FieldCropText.Name(c) + " +1";
        }
        CozyFx.Alert(Loc.Display(line));
        GameEvents.RaiseCustom("talkVillager", 1f);
        return line;
    }

    private static string Line(string id, int hearts)
    {
        switch (id)
        {
            case "shopkeeper":
                return hearts <= 1 ? "Welcome, neighbour! The feed came in fresh this morning." :
                       hearts == 2 ? "Your chickens look healthy, ma sha Allah." :
                       hearts == 3 ? "You're one of my best customers now." :
                       hearts == 4 ? "My father ran this shop for forty years - farmers like you kept it going." :
                                     "You're family now. Anything you need, just ask.";
            case "buyer":
                return hearts <= 1 ? "Bring me good produce and I'll pay a fair price." :
                       hearts == 2 ? "The hotels in Buraidah want fresh vegetables every week." :
                       hearts == 3 ? "Your crops sell fast - I'll pay you a bit more." :
                       hearts == 4 ? "During the date festival, everyone in Qassim comes to the market." :
                                     "I tell every buyer in Qassim about your farm!";
            default:
                return hearts <= 1 ? "Salam! Still getting used to the heat? Water early in the morning." :
                       hearts == 2 ? "Rotate your wheat with onions - the soil will thank you." :
                       hearts == 3 ? "Here, my mother's recipe. Your dates will taste even better." :
                       hearts == 4 ? "Drip lines saved half my water last summer." :
                                     "Take some seeds whenever you pass by, ya jar.";
        }
    }

    private static string PerkText(string id, int level)
    {
        if (id == "shopkeeper") return level == 3 ? "Perk: 10% back on shop purchases." : "Perk: 15% back on shop purchases.";
        if (id == "buyer") return level == 3 ? "Perk: market prices +5%." : "Perk: market prices +10%.";
        return level == 3 ? "Recipe tip unlocked: kleija date cookies (see Learn, K)." : "Perk: a seed gift each day you visit.";
    }

    // ---------------- perks ----------------

    /// <summary>Cash back from the shopkeeper on purchases with a fixed price (not truck/growth upgrades).</summary>
    private void OnShopPurchase(string id)
    {
        int h = Hearts("shopkeeper");
        if (h < 3 || id == "capacity" || id == "growth") return;
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        int price = eco.PriceOf(id);
        if (price <= 0) return;
        int back = Mathf.RoundToInt(price * (h >= 5 ? 0.15f : 0.10f));
        if (back > 0) eco.AddMoney(back);
    }

    private static float BuyerPrice(string good)
    {
        int h = Hearts("buyer");
        return h >= 5 ? 1.10f : h >= 3 ? 1.05f : 1f;
    }
}
