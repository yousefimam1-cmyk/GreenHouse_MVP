using UnityEngine;

/// <summary>
/// Camels (ناقة, camel milk) and Ardi goats (ماعز عارضي, goat milk) live in the existing animal pen: they are plain
/// AnimalDefs in Resources/Data/Economy/10_fish.json. This system only gives them a body (FarmAnimal.CustomBody):
/// the horse model tinted sandy with a hump for the camel, the sheep model tinted black for the goat, and primitive
/// fallbacks. It also adds the "ownCamel" state mission.
/// </summary>
[MazraaSystem(65)]
public class SaudiAnimals : MonoBehaviour
{
    private static readonly Color Sand = new Color(0.84f, 0.68f, 0.45f);
    private static readonly Color GoatBlack = new Color(0.16f, 0.14f, 0.13f);

    private void Awake()
    {
        FarmAnimal.CustomBody = BuildBody;
        MissionManager.StateMissions["ownCamel"] = () => EconomyManager.Instance != null ? EconomyManager.Instance.Owned("camel") : 0f;
        MissionManager.StateMissions["ownGoats"] = () => EconomyManager.Instance != null ? EconomyManager.Instance.Owned("goat") : 0f;
    }

    /// <summary>Returns true when it built the body (camel or goat).</summary>
    public static bool BuildBody(FarmAnimal a)
    {
        if (a == null) return false;
        if (a.animalId == "camel") { Camel(a.transform, a); return true; }
        if (a.animalId == "goat") { Goat(a.transform, a); return true; }
        return false;
    }

    private static void Camel(Transform t, FarmAnimal a)
    {
        var sand = Build.Mat(Sand, 0.1f);
        var model = Build.TryModel("camel", t, 2.8f, 0f) ?? Build.TryModel("horse", t, 2.6f, 0f);
        if (model != null)
        {
            if (model.name == "horse")
            {
                FishSite.Tint(model, Sand);
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 1.75f, -0.15f), new Vector3(0.6f, 0.55f, 0.85f), sand, "Hump");
            }
            a.SetupAnimation(model);
            return;
        }
        var dark = Build.Mat(new Color(0.45f, 0.33f, 0.2f), 0.1f);
        Build.Part(PrimitiveType.Capsule, t, new Vector3(0, 1.55f, 0), new Vector3(0.75f, 0.95f, 0.75f), sand, "Body", new Vector3(90, 0, 0));
        Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 2.05f, -0.1f), new Vector3(0.6f, 0.6f, 0.8f), sand, "Hump");
        Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 2.05f, 0.95f), new Vector3(0.22f, 0.5f, 0.22f), sand, "Neck", new Vector3(35, 0, 0));
        Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.5f, 1.35f), new Vector3(0.28f, 0.28f, 0.55f), sand, "Head");
        foreach (var p in new[] { new Vector2(0.28f, 0.65f), new Vector2(-0.28f, 0.65f), new Vector2(0.28f, -0.65f), new Vector2(-0.28f, -0.65f) })
            Build.Part(PrimitiveType.Cube, t, new Vector3(p.x, 0.6f, p.y), new Vector3(0.14f, 1.2f, 0.14f), dark, "Leg");
    }

    private static void Goat(Transform t, FarmAnimal a)
    {
        var black = Build.Mat(GoatBlack, 0.15f);
        var model = Build.TryModel("goat", t, 1.1f, 0f) ?? Build.TryModel("sheep", t, 1.05f, 0f);
        if (model != null)
        {
            if (model.name == "sheep") FishSite.Tint(model, GoatBlack);
            a.SetupAnimation(model);
            return;
        }
        var horn = Build.Mat(new Color(0.75f, 0.7f, 0.6f), 0.2f);
        Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.62f, 0), new Vector3(0.4f, 0.4f, 0.85f), black, "Body");
        var head = Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.88f, 0.5f), new Vector3(0.22f, 0.26f, 0.32f), black, "Head").transform;
        Build.Part(PrimitiveType.Cylinder, head, new Vector3(0.3f, 0.6f, -0.2f), new Vector3(0.18f, 0.5f, 0.18f), horn, "Horn", new Vector3(-30, 0, -15));
        Build.Part(PrimitiveType.Cylinder, head, new Vector3(-0.3f, 0.6f, -0.2f), new Vector3(0.18f, 0.5f, 0.18f), horn, "Horn", new Vector3(-30, 0, 15));
        Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.5f, -0.45f), new Vector3(0.1f, 0.18f, 0.08f), black, "Tail");
        foreach (var p in new[] { new Vector2(0.14f, 0.3f), new Vector2(-0.14f, 0.3f), new Vector2(0.14f, -0.3f), new Vector2(-0.14f, -0.3f) })
            Build.Part(PrimitiveType.Cube, t, new Vector3(p.x, 0.22f, p.y), new Vector3(0.08f, 0.44f, 0.08f), black, "Leg");
    }
}
