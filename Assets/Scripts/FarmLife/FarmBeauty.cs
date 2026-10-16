using UnityEngine;

/// <summary>
/// Farm beauty rating (like Farm Together / Animal Crossing): placed decor, tidy fields (worked, sown or ripe, not
/// bare), happy animals (water, feed, clean pen, health), orchard trees and festival decorations add up to a score
/// and 0-5 stars (weights in farmlife.json "beauty"). Stars give small bonuses: +1% market prices per star, +5%
/// roadside stand pay per star, visitors come more often from 3 stars and a fourth visitor waits from 4 stars.
/// The flower fair judges it too. Computed from the farm, nothing extra is saved.
/// </summary>
[MazraaSystem(75)]
public class FarmBeauty : MonoBehaviour
{
    public static FarmBeauty Instance { get; private set; }

    public float Score { get; private set; }
    public int StarCount { get; private set; }
    public float DecorPart { get; private set; }
    public float FieldPart { get; private set; }
    public float AnimalPart { get; private set; }
    public float TreePart { get; private set; }
    public float FestivalPart { get; private set; }

    private float timer;

    private void Awake()
    {
        Instance = this;
        MarketManager.PriceMultipliers.Remove(PriceFor);
        MarketManager.PriceMultipliers.Add(PriceFor);
    }

    private void OnDestroy()
    {
        MarketManager.PriceMultipliers.Remove(PriceFor);
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 2f;
        Compute();
    }

    public static int DecorBeauty(string id)
    {
        var d = FarmLife.Decor(id);
        if (d != null) return d.beauty;
        foreach (var o in FarmLife.Data.beauty.oldDecor) if (o.id == id) return o.beauty;
        return Mathf.RoundToInt(FarmLife.Data.beauty.decorDefault);
    }

    /// <summary>Recompute the rating now. Returns the star count.</summary>
    public int Compute()
    {
        var b = FarmLife.Data.beauty;
        float decor = 0f;
        if (Decorations.Instance != null) foreach (var p in Decorations.Instance.Placed) decor += DecorBeauty(p.id);
        DecorPart = Mathf.Min(b.decorCap, decor);

        float fields = 0f;
        var fm = FieldManager.Instance;
        if (fm != null && fm.Plots.Count > 0)
        {
            int tidy = 0;
            foreach (var p in fm.Plots) if (p != null && p.State != PlotState.Untilled) tidy++;
            fields = b.fields * tidy / fm.Plots.Count;
        }
        FieldPart = fields;

        float animals = b.noAnimals;
        var pen = AnimalPen.Instance;
        if (pen != null && pen.AnimalCount > 0)
            animals = b.animals * Mathf.Clamp01(0.4f * pen.Health + 0.3f * pen.Cleanliness + (pen.WaterLevel > 0.05f ? 0.15f : 0f) + (pen.HasFeed ? 0.15f : 0f));
        AnimalPart = animals;

        int trees = 0;
        if (OrchardManager.Instance != null) foreach (var t in OrchardManager.Instance.Trees) if (t != null && !string.IsNullOrEmpty(t.Kind)) trees++;
        TreePart = Mathf.Min(b.treeCap, trees * b.treeEach);

        FestivalPart = Festivals.Instance != null && Festivals.Instance.Today != null ? b.festival : 0f;

        Score = DecorPart + FieldPart + AnimalPart + TreePart + FestivalPart;
        int stars = 0;
        foreach (var th in b.stars) if (Score >= th) stars++;
        StarCount = Mathf.Clamp(stars, 0, 5);
        return StarCount;
    }

    public static int Stars => Instance != null ? Instance.StarCount : 0;
    /// <summary>Roadside stand pay bonus (0.05 per star).</summary>
    public static float OrdersBonus => Stars * FarmLife.Data.beauty.ordersPerStar;

    /// <summary>MarketManager price hook: +1% per star (passive bonus).</summary>
    public static float PriceFor(string good) => FarmLife.PassiveBonuses ? 1f + Stars * FarmLife.Data.beauty.pricePerStar : 1f;

    public string Summary()
    {
        return Loc.Raw("Farm beauty") + ": " + FarmLife.Stars(StarCount, 5) + "  (" + Mathf.RoundToInt(Score) + ")\n"
            + "  " + Loc.Raw("Decor") + ": " + Mathf.RoundToInt(DecorPart) + "   " + Loc.Raw("Tidy fields") + ": " + Mathf.RoundToInt(FieldPart)
            + "   " + Loc.Raw("Happy animals") + ": " + Mathf.RoundToInt(AnimalPart) + "   " + Loc.Raw("Trees") + ": " + Mathf.RoundToInt(TreePart)
            + (FestivalPart > 0f ? "   " + Loc.Raw("Festival") + ": " + Mathf.RoundToInt(FestivalPart) : "") + "\n"
            + "  " + Loc.Raw("Bonus") + ": +" + StarCount + "% " + Loc.Raw("market prices") + ", +" + Mathf.RoundToInt(OrdersBonus * 100f) + "% " + Loc.Raw("roadside stand pay");
    }
}
