using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-animal husbandry on top of the pen: every animal has its own food, water, cleanliness, health, happiness,
/// heat stress, age, sex and an inherited productivity trait. Simulated once per in-game day (cheap):
/// - animals eat while the feeder has feed (a hay bale or [feed now] tops everyone up), drink from the trough; dirt, hunger, thirst and summer heat make
///   animals ill (respiratory infection, digestive upset, heat stroke). Sick animals produce far less and lose health;
///   medicine from the shop or a vet visit cures them.
/// - shade canopy and misting fans (shop) cut heat stress in the Arabian summer; camels hardly mind the heat, hens do.
/// - females conceive with a male around when healthy and happy; after the gestation days a young one is born whose
///   trait is the parents' average +- 0.1. Production scales with the herd's productivity.
/// - value by age, health and genetics; animals can be sold from the records board at the pen (gamepad friendly).
/// Saved as JSON in SaveData ("mazraa.life.herd"); New Game clears it.
/// </summary>
[MazraaSystem(66)]
public class HerdManager : MonoBehaviour
{
    public static HerdManager Instance;
    public const string MedicineId = "animal_medicine", ShadeId = "pen_shade", FansId = "pen_fans";
    public const int MedicinePrice = 250, ShadePrice = 3000, FansPrice = 4500, VetPrice = 800;

    [Serializable] private class SaveFile { public int nextUid = 1; public int medicine; public bool shade, fans; public List<AnimalRecord> animals = new List<AnimalRecord>(); }

    private readonly List<AnimalRecord> animals = new List<AnimalRecord>();
    private System.Random rnd = new System.Random();
    private int nextUid = 1;
    private float syncTimer;
    private GameObject shadeObj, fansObj, board;
    private HerdPanel panel;

    public IReadOnlyList<AnimalRecord> Animals => animals;
    public int Medicine { get; private set; }
    public bool Shade { get; private set; }
    public bool Fans { get; private set; }
    /// <summary>Fans only mist while the trough has water.</summary>
    public bool FansWorking => Fans && (AnimalPen.Instance == null || AnimalPen.Instance.WaterLevel > 0.05f);
    public HerdPanel Panel => panel;

    /// <summary>For tests: fixed random sequence.</summary>
    public void Seed(int seed) => rnd = new System.Random(seed);

    private void Awake()
    {
        Instance = this;
        Load();
        ShopCatalog.Register(new ShopEntry
        {
            id = MedicineId, category = "Animals",
            name = () => Loc.Raw("Animal medicine (1 dose)"),
            detail = () => "(" + (Instance != null ? Instance.Medicine : 0) + ")",
            price = () => MedicinePrice,
            onBought = () => { if (Instance != null) { Instance.Medicine++; Instance.Save(); } }
        });
        ShopCatalog.Register(new ShopEntry
        {
            id = ShadeId, category = "Animals",
            name = () => Loc.Raw("Shade canopy for the pen"),
            price = () => Instance != null && Instance.Shade ? -1 : ShadePrice,
            blockReason = () => Instance != null && Instance.Shade ? "Already built" : null,
            onBought = () => { if (Instance != null) { Instance.Shade = true; Instance.Save(); Instance.BuildExtras(); } }
        });
        ShopCatalog.Register(new ShopEntry
        {
            id = FansId, category = "Animals",
            name = () => Loc.Raw("Misting fans for the pen"),
            price = () => Instance != null && Instance.Fans ? -1 : FansPrice,
            blockReason = () => Instance != null && Instance.Fans ? "Already built" : null,
            onBought = () => { if (Instance != null) { Instance.Fans = true; Instance.Save(); Instance.BuildExtras(); } }
        });
    }

    private void Start()
    {
        WorldClock.DayStarted += OnDay;
        GameEvents.UpgradeBought += OnBought;
        GameEvents.NewGameStarted += ResetAll;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
        panel = new HerdPanel(transform);
        Reconcile();
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.UpgradeBought -= OnBought;
        GameEvents.NewGameStarted -= ResetAll;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void OnBought(string id) => Reconcile();

    private void Update()
    {
        if (panel != null && panel.IsOpen) panel.Tick(this);
        syncTimer -= Time.deltaTime;
        if (syncTimer > 0f) return;
        syncTimer = 1f;
        Reconcile();
        if (AnimalPen.Instance != null && board == null) { BuildBoard(); BuildExtras(); }
    }

    // ------------------------------------------------------------------ records <-> owned animals

    /// <summary>Keep one record per owned animal (shop buys add adults, other systems may add or remove) and link pen bodies.</summary>
    public void Reconcile()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        bool changed = false;
        foreach (var def in eco.Data.animals)
        {
            int owned = eco.Owned(def.id);
            int have = 0;
            foreach (var a in animals) if (a.species == def.id) have++;
            for (; have < owned; have++) { animals.Add(NewBought(def.id)); changed = true; }
            for (int i = animals.Count - 1; i >= 0 && have > owned; i--)
                if (animals[i].species == def.id) { animals.RemoveAt(i); have--; changed = true; }
        }
        for (int i = animals.Count - 1; i >= 0; i--)
            if (eco.Animal(animals[i].species) == null) { animals.RemoveAt(i); changed = true; }
        LinkBodies();
        if (changed) Save();
    }

    private AnimalRecord NewBought(string species)
    {
        var sp = SpeciesTraits.Get(species);
        bool hasMale = false, hasFemale = false;
        foreach (var a in animals) if (a.species == species) { if (a.male) hasMale = true; else hasFemale = true; }
        return new AnimalRecord
        {
            uid = nextUid++, species = species,
            male = hasFemale && !hasMale,   // the second animal of a kind is a male, then females
            ageDays = sp.maturity + rnd.Next(0, sp.maturity + 1),
            trait = 1f + (float)rnd.NextDouble() * 0.2f,   // shop animals are good stock (1.0 .. 1.2)
            happiness = 1f,
        };
    }

    private void LinkBodies()
    {
        var pen = AnimalPen.Instance;
        if (pen == null) return;
        var linked = new HashSet<int>();
        foreach (var body in pen.Animals) if (body != null && body.uid != 0 && Find(body.uid) != null) linked.Add(body.uid);
        foreach (var body in pen.Animals)
        {
            if (body == null || (body.uid != 0 && linked.Contains(body.uid))) continue;
            body.uid = 0;
            foreach (var a in animals)
                if (a.species == body.animalId && !linked.Contains(a.uid)) { body.uid = a.uid; linked.Add(a.uid); break; }
        }
    }

    public AnimalRecord Find(int uid) => animals.Find(a => a.uid == uid);

    public FarmAnimal Body(int uid)
    {
        var pen = AnimalPen.Instance;
        if (pen == null) return null;
        foreach (var b in pen.Animals) if (b != null && b.uid == uid) return b;
        return null;
    }

    // ------------------------------------------------------------------ daily simulation

    public float HeatLoadToday
    {
        get { var c = WorldClock.Instance; return c == null ? 0f : Husbandry.HeatLoad(c.Season, c.Weather); }
    }

    public float StressFor(string species) => Husbandry.HeatStress(species, HeatLoadToday, Shade, FansWorking);

    private void OnDay(int day)
    {
        if (NetSession.IsClient) return;   // the host simulates the shared farm
        SimulateDay(day);
    }

    /// <summary>One in-game day for every animal: eat, drink, health, illness, ageing, pregnancies and births.
    /// Neglected animals don't die (like the big farming sims) but get weak, ill and stop producing.</summary>
    public void SimulateDay(int day)
    {
        var eco = EconomyManager.Instance; var pen = AnimalPen.Instance;
        if (eco == null) return;
        Reconcile();
        foreach (var a in animals)
        {
            float stress = StressFor(a.species);
            bool fed = eco.Feed > 0.01f;   // the feeder is stocked (feed itself is used by production cycles)
            bool watered = pen == null || pen.WaterLevel > 0.05f;
            if (watered && pen != null) pen.AddWater(-0.02f * (1f + stress));
            Husbandry.Day(a, fed, watered, pen != null ? pen.Cleanliness : 1f, stress, day, rnd);
        }
        Breed(day);
        Save();
    }

    private void Breed(int day)
    {
        var eco = EconomyManager.Instance;
        foreach (var mother in animals.ToArray())
        {
            var def = eco.Animal(mother.species);
            if (def == null) continue;
            if (mother.Pregnant)
            {
                mother.gestationLeft--;
                if (mother.gestationLeft > 0) continue;
                if (eco.Owned(mother.species) >= def.max) { mother.gestationLeft = 1; continue; }   // wait for room in the pen
                Birth(mother, def);
                continue;
            }
            if (!Husbandry.CanBreed(mother)) continue;
            AnimalRecord father = null;
            foreach (var m in animals) if (m.species == mother.species && m.male && m.Adult && !m.Sick) { father = m; break; }
            if (father == null) continue;
            int pregnant = 0, count = 0;
            foreach (var a in animals) if (a.species == mother.species) { count++; if (a.Pregnant) pregnant++; }
            if (count + pregnant >= def.max) continue;
            if (rnd.NextDouble() < 0.35 + 0.3 * (mother.happiness - 0.55))
            {
                mother.gestationLeft = SpeciesTraits.Get(mother.species).gestation;
                mother.sireTrait = father.trait;
            }
        }
    }

    private void Birth(AnimalRecord mother, AnimalDef def)
    {
        var eco = EconomyManager.Instance;
        mother.gestationLeft = 0;
        var baby = new AnimalRecord
        {
            uid = nextUid++, species = mother.species, male = rnd.NextDouble() < 0.5, ageDays = 0,
            trait = Husbandry.Inherit(mother.trait, mother.sireTrait, rnd), happiness = 0.9f, health = Mathf.Max(0.7f, mother.health),
        };
        animals.Add(baby);
        eco.AddAnimal(mother.species);
        if (AnimalPen.Instance != null) AnimalPen.Instance.SyncAnimals(true);
        LinkBodies();
        Say(Loc.Display(Loc.Raw("A new animal was born") + ": " + SpeciesName(def.id) + " (" + Loc.Raw(baby.male ? "male" : "female") + ", " + Loc.Raw("genetics") + " " + baby.trait.ToString("F2") + ")"));
        GameEvents.RaiseAnimalBorn(def.id);
    }

    /// <summary>Average productivity of one species (scales the pen's production; 1 when no records).</summary>
    public float ProductionFactor(string species)
    {
        float sum = 0f; int n = 0;
        foreach (var a in animals) if (a.species == species) { sum += Husbandry.Productivity(a); n++; }
        return n == 0 ? 1f : sum / n;
    }

    // ------------------------------------------------------------------ care actions

    /// <summary>Feed every animal its ration now from the feed store. Returns how many were fed.</summary>
    public int FeedAll()
    {
        var eco = EconomyManager.Instance;
        int fed = 0;
        foreach (var a in animals)
            if (eco != null && eco.ConsumeFeed(SpeciesTraits.Get(a.species).ration)) { a.food = 1f; fed++; }
        Save();
        return fed;
    }

    /// <summary>A hay bale dropped in the pen feeds everyone for the day.</summary>
    public void FeedFromBale()
    {
        foreach (var a in animals) { a.food = 1f; a.happiness = Mathf.Clamp01(a.happiness + 0.05f); }
        Save();
    }

    /// <summary>Everyone drinks from the trough (called when it is filled).</summary>
    public void WaterAll()
    {
        var pen = AnimalPen.Instance;
        if (pen != null && pen.WaterLevel <= 0.05f) return;
        foreach (var a in animals) a.water = 1f;
        Save();
    }

    /// <summary>Brush one animal: clean and happier (once a day counts).</summary>
    public string Groom(int uid)
    {
        var a = Find(uid);
        if (a == null) return Loc.T("No animal close enough");
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : 0;
        a.clean = 1f;
        if (a.groomedDay != day) a.happiness = Mathf.Clamp01(a.happiness + 0.2f);
        a.groomedDay = day;
        Save();
        return Loc.Display(Loc.Raw("Groomed") + ": " + SpeciesName(a.species) + " #" + a.uid);
    }

    public void MakeSick(AnimalRecord a, string illness) { if (a == null) return; a.illness = illness; a.sickDays = 0; Save(); }

    /// <summary>Give one dose of medicine to a sick animal.</summary>
    public string Treat(AnimalRecord a)
    {
        if (a == null || !a.Sick) return Loc.T("This animal is healthy");
        if (Medicine <= 0) return Loc.T("No medicine - buy it at the Farm Shop or call the vet");
        Medicine--;
        Husbandry.Cure(a, 0.15f);
        Save();
        return Loc.Display(Loc.Raw("Medicine given - cured") + ": " + SpeciesName(a.species) + " #" + a.uid);
    }

    public int SickCount { get { int n = 0; foreach (var a in animals) if (a.Sick) n++; return n; } }

    /// <summary>The vet treats every sick animal for a fee.</summary>
    public string CallVet()
    {
        int sick = SickCount;
        if (sick == 0) return Loc.T("No sick animals - the vet is not needed");
        var eco = EconomyManager.Instance;
        if (eco == null || !eco.Spend(VetPrice)) return Loc.T("Not enough money for the vet");
        foreach (var a in animals) if (a.Sick) Husbandry.Cure(a, 0.25f);
        Save();
        return Loc.Display(Loc.Raw("The vet treated the sick animals") + " (" + sick + ") -" + VetPrice + " " + Loc.Raw("SAR"));
    }

    public int ValueOf(AnimalRecord a)
    {
        var def = EconomyManager.Instance != null ? EconomyManager.Instance.Animal(a.species) : null;
        return def == null ? 0 : Husbandry.Value(a, def.price);
    }

    /// <summary>Sell one animal at its value. Returns money earned.</summary>
    public int Sell(AnimalRecord a)
    {
        var eco = EconomyManager.Instance;
        if (a == null || eco == null || Find(a.uid) == null) return 0;
        int v = ValueOf(a);
        Remove(a);
        eco.AddMoney(v);
        Save();
        return v;
    }

    private void Remove(AnimalRecord a)
    {
        var eco = EconomyManager.Instance;
        if (AnimalPen.Instance != null) AnimalPen.Instance.RemoveVisual(a.uid, a.species);
        animals.Remove(a);
        if (eco != null) eco.RemoveAnimal(a.species);
        if (AnimalPen.Instance != null) AnimalPen.Instance.SyncAnimals();
    }

    // ------------------------------------------------------------------ text

    public static string SpeciesName(string id)
    {
        var def = EconomyManager.Instance != null ? EconomyManager.Instance.Animal(id) : null;
        return def != null ? EconomyManager.AnimalName(def) : id;
    }

    private static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";

    /// <summary>One line per animal (translated, not shaped).</summary>
    public string ShortLine(AnimalRecord a)
    {
        string s = SpeciesName(a.species) + " #" + a.uid + " (" + Loc.Raw(a.male ? "male" : "female") + ") - " + Loc.Raw("Age") + " " + a.ageDays + " " + Loc.Raw("days")
                 + " - " + Loc.Raw("Health") + " " + Pct(a.health);
        if (a.Sick) s += " - " + Loc.Raw("Sick") + ": " + Loc.Raw(Husbandry.IllnessKey(a.illness));
        if (a.Pregnant) s += " - " + Loc.Raw("Pregnant");
        if (!a.Adult) s += " - " + Loc.Raw("Young");
        return s;
    }

    /// <summary>Detail lines for the selected animal (translated, not shaped).</summary>
    public List<string> DetailLines(AnimalRecord a)
    {
        var l = new List<string>
        {
            Loc.Raw("Food") + " " + Pct(a.food) + "  " + Loc.Raw("Water") + " " + Pct(a.water) + "  " + Loc.Raw("Clean") + " " + Pct(a.clean)
                + "  " + Loc.Raw("Health") + " " + Pct(a.health) + "  " + Loc.Raw("Happiness") + " " + Pct(a.happiness),
            Loc.Raw("Productivity") + " " + Mathf.RoundToInt(Husbandry.Productivity(a) * 100f) + "%" + "  " + Loc.Raw("Genetics") + " " + a.trait.ToString("F2")
                + "  " + Loc.Raw("Heat stress") + " " + Pct(a.heatStress) + "  " + Loc.Raw("Value") + " " + ValueOf(a).ToString("N0") + " " + Loc.Raw("SAR"),
        };
        if (a.Pregnant) l.Add(Loc.Raw("Pregnant") + ": " + a.gestationLeft + " " + Loc.Raw("days left"));
        if (a.Sick) l.Add(Loc.Raw("Sick") + ": " + Loc.Raw(Husbandry.IllnessKey(a.illness)) + " (" + a.sickDays + " " + Loc.Raw("days") + ")");
        return l;
    }

    private string HudLine()
    {
        if (animals.Count == 0) return null;
        int sick = SickCount, preg = 0;
        foreach (var a in animals) if (a.Pregnant) preg++;
        float heat = HeatLoadToday;
        if (sick == 0 && preg == 0 && heat < 0.3f) return null;
        string s = Loc.Raw("Herd") + " " + animals.Count;
        if (sick > 0) s += " - " + Loc.Raw("Sick") + " " + sick;
        if (preg > 0) s += " - " + Loc.Raw("Pregnant") + " " + preg;
        if (heat >= 0.3f) s += " - " + Loc.Raw("Heat") + " " + Pct(heat) + (Shade || Fans ? "" : " - " + Loc.Raw("needs shade"));
        return Loc.Display(s);
    }

    private static void Say(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }

    public void OpenPanel() { if (panel != null) panel.Open(this); }

    // ------------------------------------------------------------------ pen extras

    private void BuildBoard()
    {
        var pen = AnimalPen.Instance;
        if (pen == null) return;
        board = new GameObject("Animal Records Board");
        board.layer = 7;
        board.transform.SetPositionAndRotation(Build.OnGround(pen.transform.TransformPoint(new Vector3(2.4f, 0, pen.size.y / 2f + 0.4f))), pen.transform.rotation);
        var c = board.AddComponent<BoxCollider>(); c.center = new Vector3(0, 1.1f, 0); c.size = new Vector3(1.2f, 1.2f, 0.3f);
        var wood = Build.Mat(new Color(0.45f, 0.3f, 0.18f), 0.15f);
        Build.Part(PrimitiveType.Cube, board.transform, new Vector3(0, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f), wood, "Post");
        Build.Part(PrimitiveType.Cube, board.transform, new Vector3(0, 1.3f, 0), new Vector3(1.1f, 0.75f, 0.06f), Build.Mat(new Color(0.92f, 0.88f, 0.76f)), "Board");
        foreach (var r in board.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        board.AddComponent<PenBoard>();
        board.transform.SetParent(pen.transform, true);
    }

    /// <summary>Shade canopy (sun sail on posts) and misting fans over the pen, when bought.</summary>
    public void BuildExtras()
    {
        var pen = AnimalPen.Instance;
        if (pen == null) return;
        Transform t = pen.transform;
        if (Shade && shadeObj == null)
        {
            shadeObj = new GameObject("Pen Shade");
            shadeObj.transform.SetParent(t, false);
            var cloth = Build.Mat(new Color(0.88f, 0.84f, 0.72f), 0.05f); var steel = Build.Mat(new Color(0.5f, 0.5f, 0.52f), 0.5f, 0.6f);
            Build.Part(PrimitiveType.Cube, shadeObj.transform, new Vector3(-2f, 3.1f, 0.5f), new Vector3(9f, 0.06f, 7f), cloth, "Canopy");
            foreach (var p in new[] { new Vector2(-6.4f, -3f), new Vector2(2.4f, -3f), new Vector2(-6.4f, 4f), new Vector2(2.4f, 4f) })
                Build.Part(PrimitiveType.Cylinder, shadeObj.transform, new Vector3(p.x, 1.55f, p.y), new Vector3(0.12f, 1.55f, 0.12f), steel, "Pole");
        }
        if (Fans && fansObj == null)
        {
            fansObj = new GameObject("Misting Fans");
            fansObj.transform.SetParent(t, false);
            var body = Build.Mat(new Color(0.85f, 0.87f, 0.9f), 0.5f, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                var fan = Build.Part(PrimitiveType.Cylinder, fansObj.transform, new Vector3(-5.5f + i * 3.5f, 2.6f, -1f), new Vector3(0.8f, 0.12f, 0.8f), body, "Fan", new Vector3(80f, 0f, 0f));
                fan.AddComponent<PropSpinner>();
            }
        }
        if (!Shade && shadeObj != null) { Destroy(shadeObj); shadeObj = null; }
        if (!Fans && fansObj != null) { Destroy(fansObj); fansObj = null; }
    }

    // ------------------------------------------------------------------ save

    private void Load()
    {
        animals.Clear();
        string json = SaveData.GetString("mazraa.life.herd", "");
        SaveFile f = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { f = JsonUtility.FromJson<SaveFile>(json); }
            catch (Exception e) { Debug.LogWarning("[Herd] Ignoring bad herd save: " + e.Message); }
        }
        if (f == null) f = new SaveFile();
        nextUid = Mathf.Max(1, f.nextUid);
        Medicine = f.medicine; Shade = f.shade; Fans = f.fans;
        if (f.animals != null) foreach (var a in f.animals) if (a != null && !string.IsNullOrEmpty(a.species)) { animals.Add(a); nextUid = Mathf.Max(nextUid, a.uid + 1); }
    }

    public void Save()
    {
        var f = new SaveFile { nextUid = nextUid, medicine = Medicine, shade = Shade, fans = Fans, animals = new List<AnimalRecord>(animals) };
        SaveData.SetString("mazraa.life.herd", JsonUtility.ToJson(f));
        SaveData.Save();
    }

    public void ResetAll()
    {
        animals.Clear();
        nextUid = 1; Medicine = 0; Shade = false; Fans = false;
        if (panel != null) panel.Close();
        BuildExtras();
        Save();
    }
}
