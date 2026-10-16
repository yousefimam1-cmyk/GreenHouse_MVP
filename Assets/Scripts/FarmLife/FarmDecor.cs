using UnityEngine;

/// <summary>
/// Farm life decor (farmlife.json "decor"): pieces unlocked by farm level are sold on the shop page "Farm decor"
/// (locked ones show the level needed); festival and daily-gift prizes are never sold. Models are built from
/// primitives here and placed / picked up through Decorations (ExtraModels, AddToStock); each piece has a beauty value.
/// </summary>
[MazraaSystem(72)]
public class FarmDecor : MonoBehaviour
{
    public const string Category = "Farm decor";

    private void Awake()
    {
        foreach (var d in FarmLife.Data.decor)
        {
            var def = d;
            Decorations.ExtraModels[def.id] = t => BuildModel(def.model, t);
            if (def.minLevel <= 0 || def.price <= 0) continue;
            ShopCatalog.Register(new ShopEntry
            {
                id = def.id,
                category = Category,
                name = () => FarmLife.Pick(def.en, def.ar),
                detail = () => !FarmLevel.IsUnlocked(def.id) ? "(" + Loc.Raw("Level") + " " + def.minLevel + ")"
                             : Decorations.Instance != null && Decorations.Instance.Stock(def.id) > 0 ? "(" + Loc.Raw("in storage") + ": " + Decorations.Instance.Stock(def.id) + ")" : "",
                price = () => def.price,
                blockReason = () => FarmLevel.IsUnlocked(def.id) ? null : "Reach a higher farm level first",
                onBought = () => { if (Decorations.Instance != null) { Decorations.Instance.AddToStock(def.id); Decorations.Instance.BeginPlacing(def.id); } },
            });
        }
    }

    // ---------------- models (primitives) ----------------

    private static Material M(float r, float g, float b, float smooth = 0.2f, float metal = 0f) => Build.Mat(new Color(r, g, b), smooth, metal);
    private static Material Glow(Color c) => Build.Mat(c, 0.8f, 0f, c * 2f);

    /// <summary>Build a decor model under t; returns the pick-up box size.</summary>
    public static Vector3 BuildModel(string model, Transform t)
    {
        switch (model)
        {
            case "zir":
            {
                var clay = M(0.7f, 0.45f, 0.28f, 0.15f);
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.25f, 0), new Vector3(0.7f, 0.5f, 0.7f), M(0.4f, 0.3f, 0.2f), "Stand");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.85f, 0), new Vector3(0.6f, 0.8f, 0.6f), clay, "Jar");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.28f, 0), new Vector3(0.28f, 0.06f, 0.28f), clay, "Neck");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.55f, 0.32f), new Vector3(0.12f, 0.04f, 0.12f), M(0.8f, 0.7f, 0.4f, 0.6f, 0.6f), "Cup");
                return new Vector3(0.9f, 1.4f, 0.9f);
            }
            case "bench":
            {
                var wood = M(0.5f, 0.36f, 0.22f, 0.1f);
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.5f), wood, "Seat");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.85f, -0.22f), new Vector3(1.8f, 0.5f, 0.06f), wood, "Back");
                for (int i = 0; i < 2; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(i == 0 ? -0.8f : 0.8f, 0.22f, 0), new Vector3(0.1f, 0.44f, 0.45f), wood, "Leg");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.52f, 0.02f), new Vector3(1.6f, 0.06f, 0.4f), M(0.75f, 0.2f, 0.15f), "Cushion");
                return new Vector3(1.9f, 1.2f, 0.7f);
            }
            case "shade":
            {
                var post = M(0.42f, 0.3f, 0.18f, 0.1f);
                for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cylinder, t, new Vector3(i % 2 == 0 ? -1.4f : 1.4f, 1.2f, i < 2 ? -1.4f : 1.4f), new Vector3(0.1f, 1.2f, 0.1f), post, "Post");
                var frond = M(0.66f, 0.55f, 0.32f, 0.05f);
                for (int i = 0; i < 9; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(-1.5f + i * 0.375f, 2.45f, 0), new Vector3(0.3f, 0.05f, 3.2f), frond, "Frond", new Vector3(0, 0, i % 2 == 0 ? 4f : -4f));
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.02f, 0), new Vector3(2.6f, 0.03f, 2.6f), M(0.6f, 0.2f, 0.12f), "Mat");
                return new Vector3(3f, 2.6f, 3f);
            }
            case "roses":
            {
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.15f, 0), new Vector3(2f, 0.3f, 0.9f), M(0.75f, 0.68f, 0.55f), "Border");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.3f, 0), new Vector3(1.85f, 0.04f, 0.75f), M(0.3f, 0.2f, 0.12f), "Soil");
                for (int i = 0; i < 8; i++)
                {
                    var p = new Vector3(-0.8f + (i % 4) * 0.53f, 0.55f, i < 4 ? -0.18f : 0.18f);
                    Build.Part(PrimitiveType.Sphere, t, p, new Vector3(0.35f, 0.4f, 0.35f), M(0.2f, 0.45f, 0.18f), "Bush");
                    Build.Part(PrimitiveType.Sphere, t, p + new Vector3(0.05f, 0.2f, 0.05f), Vector3.one * 0.14f, M(0.92f, 0.35f, 0.55f, 0.4f), "Rose");
                }
                return new Vector3(2.1f, 1f, 1f);
            }
            case "fountain":
            {
                var stone = M(0.85f, 0.8f, 0.7f, 0.3f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.25f, 0), new Vector3(2.2f, 0.25f, 2.2f), stone, "Basin");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.46f, 0), new Vector3(1.9f, 0.04f, 1.9f), Build.Mat(new Color(0.3f, 0.6f, 0.8f), 0.95f), "Water");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.9f, 0), new Vector3(0.25f, 0.6f, 0.25f), stone, "Column");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.5f, 0), new Vector3(0.9f, 0.08f, 0.9f), stone, "Bowl");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 1.7f, 0), Vector3.one * 0.3f, Build.Mat(new Color(0.5f, 0.75f, 0.9f), 0.95f), "Spout");
                for (int i = 0; i < 8; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(i * 0.785f) * 1.1f, 0.52f, Mathf.Sin(i * 0.785f) * 1.1f), new Vector3(0.12f, 0.06f, 0.12f), M(0.2f, 0.5f, 0.6f, 0.6f), "Tile");
                return new Vector3(2.3f, 1.9f, 2.3f);
            }
            case "dovecote":
            {
                var mud = M(0.78f, 0.62f, 0.45f, 0.1f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.5f, 0), new Vector3(1.2f, 1.5f, 1.2f), mud, "Tower");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 3.1f, 0), new Vector3(1.25f, 0.9f, 1.25f), mud, "Dome");
                for (int i = 0; i < 12; i++) Build.Part(PrimitiveType.Cylinder, t, new Vector3(Mathf.Cos(i * 0.52f) * 0.6f, 1.2f + (i % 3) * 0.6f, Mathf.Sin(i * 0.52f) * 0.6f), new Vector3(0.12f, 0.03f, 0.12f), M(0.2f, 0.15f, 0.1f), "Hole", new Vector3(90f, -i * 30f, 0));
                for (int i = 0; i < 3; i++) Build.Part(PrimitiveType.Sphere, t, new Vector3(-0.3f + i * 0.3f, 3.6f, 0), new Vector3(0.15f, 0.12f, 0.22f), M(0.85f, 0.85f, 0.85f), "Pigeon");
                return new Vector3(1.4f, 3.8f, 1.4f);
            }
            case "door":
            {
                var mud = M(0.8f, 0.66f, 0.48f, 0.1f);
                Build.Part(PrimitiveType.Cube, t, new Vector3(-1f, 1.3f, 0), new Vector3(0.5f, 2.6f, 0.5f), mud, "Pillar");
                Build.Part(PrimitiveType.Cube, t, new Vector3(1f, 1.3f, 0), new Vector3(0.5f, 2.6f, 0.5f), mud, "Pillar");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.8f, 0), new Vector3(2.6f, 0.5f, 0.55f), mud, "Lintel");
                for (int i = 0; i < 5; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(-1f + i * 0.5f, 3.15f, 0), new Vector3(0.25f, 0.25f, 0.5f), mud, "Crenel", new Vector3(0, 0, 45f));
                var colors = new[] { new Color(0.15f, 0.45f, 0.6f), new Color(0.85f, 0.65f, 0.2f), new Color(0.7f, 0.2f, 0.15f) };
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.2f, 0.05f), new Vector3(1.5f, 2.4f, 0.1f), Build.Mat(colors[0], 0.3f), "Door");
                for (int i = 0; i < 6; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(i % 2 == 0 ? -0.35f : 0.35f, 0.5f + (i / 2) * 0.7f, 0.11f), new Vector3(0.5f, 0.12f, 0.02f), Build.Mat(colors[1 + i % 2], 0.3f), "Pattern", new Vector3(0, 0, 45f));
                return new Vector3(2.6f, 3.2f, 0.7f);
            }
            case "trophy":
            {
                var gold = Build.Mat(new Color(0.95f, 0.75f, 0.2f), 0.85f, 0.9f);
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), M(0.3f, 0.22f, 0.15f), "Plinth");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1f, 0), new Vector3(0.1f, 0.4f, 0.1f), gold, "Trunk");
                for (int f = 0; f < 6; f++) Build.Part(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(f * 1.05f) * 0.2f, 1.45f, Mathf.Sin(f * 1.05f) * 0.2f), new Vector3(0.08f, 0.03f, 0.5f), gold, "Frond", new Vector3(25f, f * 60f, 0));
                return new Vector3(0.8f, 1.7f, 0.8f);
            }
            case "rosette":
            {
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.7f, 0), new Vector3(0.08f, 0.7f, 0.08f), M(0.85f, 0.85f, 0.85f, 0.5f, 0.4f), "Pole");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.55f, 0), new Vector3(0.6f, 0.04f, 0.6f), M(0.15f, 0.35f, 0.75f), "Rosette", new Vector3(90f, 0, 0));
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.55f, 0.03f), new Vector3(0.3f, 0.04f, 0.3f), Build.Mat(new Color(0.95f, 0.8f, 0.25f), 0.8f, 0.8f), "Medal", new Vector3(90f, 0, 0));
                Build.Part(PrimitiveType.Cube, t, new Vector3(-0.1f, 1.1f, 0), new Vector3(0.12f, 0.5f, 0.02f), M(0.15f, 0.35f, 0.75f), "Ribbon", new Vector3(0, 0, 10f));
                Build.Part(PrimitiveType.Cube, t, new Vector3(0.1f, 1.1f, 0), new Vector3(0.12f, 0.5f, 0.02f), M(0.15f, 0.35f, 0.75f), "Ribbon", new Vector3(0, 0, -10f));
                return new Vector3(0.8f, 1.9f, 0.6f);
            }
            case "melon":
            {
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.15f, 0), new Vector3(1.6f, 0.3f, 1.2f), M(0.5f, 0.38f, 0.25f), "Cart");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.95f, 0), new Vector3(1.5f, 1.2f, 1.1f), M(0.16f, 0.45f, 0.16f, 0.5f), "Melon");
                for (int i = 0; i < 6; i++) Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.95f, 0), new Vector3(0.06f, 1.22f, 1.12f), M(0.3f, 0.6f, 0.25f, 0.5f), "Stripe", new Vector3(0, i * 30f, 0));
                return new Vector3(1.7f, 1.6f, 1.3f);
            }
            case "arch":
            {
                var green = M(0.2f, 0.45f, 0.18f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(-1.1f, 1.2f, 0), new Vector3(0.18f, 1.2f, 0.18f), green, "Side");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(1.1f, 1.2f, 0), new Vector3(0.18f, 1.2f, 0.18f), green, "Side");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.5f, 0), new Vector3(2.5f, 0.3f, 0.3f), green, "Top");
                var colors = new[] { new Color(0.95f, 0.35f, 0.55f), new Color(1f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 0.95f), new Color(0.85f, 0.3f, 0.75f) };
                for (int i = 0; i < 16; i++)
                {
                    Vector3 p = i < 6 ? new Vector3(-1.1f, 0.4f + i * 0.35f, 0.15f) : i < 12 ? new Vector3(1.1f, 0.4f + (i - 6) * 0.35f, 0.15f) : new Vector3(-0.9f + (i - 12) * 0.6f, 2.55f, 0.18f);
                    Build.Part(PrimitiveType.Sphere, t, p, Vector3.one * 0.18f, Build.Mat(colors[i % 4], 0.4f), "Flower");
                }
                return new Vector3(2.6f, 2.8f, 0.6f);
            }
            case "crescent":
            {
                var metal = M(0.35f, 0.28f, 0.15f, 0.6f, 0.6f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 1.1f, 0), new Vector3(0.07f, 1.1f, 0.07f), metal, "Pole");
                var glow = new Color(1f, 0.82f, 0.4f);
                for (int i = 0; i < 7; i++)
                {
                    float a = -1.2f + i * 0.4f;
                    Build.Part(PrimitiveType.Sphere, t, new Vector3(Mathf.Sin(a) * 0.45f, 2.6f + Mathf.Cos(a) * 0.45f, 0), Vector3.one * 0.16f, Glow(glow), "Crescent");
                }
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.05f, 0), new Vector3(0.3f, 0.4f, 0.3f), Glow(new Color(1f, 0.7f, 0.3f)), "Fanous");
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(t, false); l.transform.localPosition = new Vector3(0, 2.2f, 0);
                l.type = LightType.Point; l.range = 6f; l.intensity = 1.2f; l.color = glow;
                return new Vector3(1f, 3.2f, 0.6f);
            }
            default:   // "goldlantern"
            {
                var gold = Build.Mat(new Color(0.95f, 0.75f, 0.25f), 0.85f, 0.9f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.06f, 0), new Vector3(0.5f, 0.06f, 0.5f), gold, "Base");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.7f, 0), new Vector3(0.06f, 0.65f, 0.06f), gold, "Stand");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.5f, 0), new Vector3(0.4f, 0.5f, 0.4f), Glow(new Color(1f, 0.8f, 0.4f)), "Fanous");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 1.82f, 0), new Vector3(0.42f, 0.25f, 0.42f), gold, "Dome");
                return new Vector3(0.8f, 2f, 0.8f);
            }
        }
    }
}
