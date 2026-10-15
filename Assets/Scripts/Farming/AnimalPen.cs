using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fenced pen that holds the animals bought in the shop. While feed is in stock, each animal makes its product
/// every interval; collect the products at the crate ([E]) to move them into farm storage.
/// </summary>
public class AnimalPen : MonoBehaviour
{
    public static AnimalPen Instance;
    public Vector2 size = new Vector2(18f, 12f);

    private readonly List<FarmAnimal> animals = new List<FarmAnimal>();
    private readonly Dictionary<string, float> timers = new Dictionary<string, float>();
    private readonly Dictionary<string, float> waiting = new Dictionary<string, float>();
    public bool HasFeed => EconomyManager.Instance != null && EconomyManager.Instance.Feed > 0.01f;
    public IReadOnlyDictionary<string, float> Waiting => waiting;

    private void Awake() => Instance = this;
    private void Start()
    {
        SyncAnimals();
        GameEvents.UpgradeBought += OnBought;
    }
    private void OnDestroy() => GameEvents.UpgradeBought -= OnBought;
    private void OnBought(string id) => SyncAnimals();

    public Vector3 RandomPoint()
    {
        Vector3 local = new Vector3(Random.Range(-size.x * 0.42f, size.x * 0.42f), 0, Random.Range(-size.y * 0.42f, size.y * 0.42f));
        return Build.OnGround(transform.TransformPoint(local));
    }

    private void SyncAnimals()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var def in eco.Data.animals)
        {
            int have = animals.FindAll(a => a.animalId == def.id).Count;
            for (int i = have; i < eco.Owned(def.id); i++)
                animals.Add(FarmAnimal.Create(def.id, this, RandomPoint()));
        }
    }

    private void Update()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var def in eco.Data.animals)
        {
            int count = eco.Owned(def.id);
            if (count == 0) continue;
            float t = (timers.TryGetValue(def.id, out var v) ? v : 0f) + Time.deltaTime;
            if (t >= def.interval)
            {
                t = 0f;
                if (eco.ConsumeFeed(def.feedPerCycle * count))
                    waiting[def.id] = (waiting.TryGetValue(def.id, out var w) ? w : 0f) + def.amount * count;
            }
            timers[def.id] = t;
        }
    }

    public void ResetPen()
    {
        foreach (var a in animals) if (a != null) Destroy(a.gameObject);
        animals.Clear(); timers.Clear(); waiting.Clear();
    }

    /// <summary>Move all waiting products into farm storage. Returns a localized summary.</summary>
    public string Collect()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return "";
        var parts = new List<string>();
        foreach (var def in eco.Data.animals)
        {
            if (!waiting.TryGetValue(def.id, out var amount) || amount <= 0f) continue;
            eco.AddGood(def.product, amount);
            var g = eco.Good(def.product);
            parts.Add(amount.ToString("F0") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g));
            waiting[def.id] = 0f;
        }
        if (parts.Count == 0) return Loc.T(HasFeed ? "Nothing to collect yet" : "No feed - buy feed at the shop");
        GameEvents.RaiseProductsCollected();
        return Loc.Display(Loc.Raw("Collected") + ": " + string.Join(", ", parts));
    }
}
