using UnityEngine;

/// <summary>
/// Places the animated ambient life once the farm is built: farm dog (follows you), barn cat, two horses,
/// eagles circling the farm, a shopkeeper at each stall, field workers, market buyers and vehicle drivers.
/// Every piece is optional: if its model is missing from Resources/Models it is simply skipped.
/// </summary>
public class AmbientLife : MonoBehaviour
{
    private static readonly string[] People = { "Characters/character-male-a", "Characters/character-female-b", "Characters/character-male-c", "Characters/character-female-d" };

    private void Start() => Invoke(nameof(Populate), 1.2f);

    private void Populate()
    {
        var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        Vector3 start = inputs != null ? inputs.transform.position : Vector3.zero;

        // Farm dog
        var dog = Actor.Spawn("dog", 0.9f, false, ActorMode.FollowPlayer, Build.OnGround(start + new Vector3(2f, 0, 1f)));
        if (dog != null) dog.speed = 2.2f;

        // Barn cat and horses around the animal pen
        var pen = AnimalPen.Instance;
        if (pen != null)
        {
            var cat = Actor.Spawn("cat", 0.55f, false, ActorMode.Wander, Build.OnGround(pen.transform.position + pen.transform.right * 4f));
            if (cat != null) { cat.radius = 5f; cat.speed = 0.9f; }
            for (int i = 0; i < 2; i++)
            {
                var h = Actor.Spawn("horse", 2.3f, false, ActorMode.Wander, Build.OnGround(pen.transform.TransformPoint(new Vector3(-14f - i * 3f, 0, 4f + i * 4f))));
                if (h != null) { h.radius = 9f; h.speed = 0.9f; }
            }
        }

        // Eagles over the farm
        Vector3 center = start; int n = 0;
        if (GameManager.Instance != null) foreach (var gh in GameManager.Instance.Greenhouses) { center += gh.transform.position; n++; }
        center /= n + 1;
        for (int i = 0; i < 2; i++)
        {
            var e = Actor.Spawn("eagle", 1.6f, false, ActorMode.Fly, Build.OnGround(center));
            if (e != null) { e.radius = 30f + i * 15f; e.flyHeight = 24f + i * 6f; e.speed = 2.5f - i * 0.6f; }
        }

        // Shopkeepers behind the stalls
        int p = 0;
        foreach (var stall in FindObjectsByType<ShopStall>(FindObjectsSortMode.None))
        {
            var keeper = Actor.Spawn(People[p++ % People.Length], 1.5f, true, ActorMode.Stand, Build.OnGround(stall.transform.position - stall.transform.forward * 1.3f));
            if (keeper != null) keeper.transform.rotation = stall.transform.rotation;
        }
        GameEvents.UpgradeBought += OnSale;

        // Field workers and market buyers
        if (FarmFields.Instance != null && FarmFields.Instance.Fields.Count > 0)
            for (int i = 0; i < 2; i++)
            {
                var w = Actor.Spawn(People[(p++) % People.Length], 1.5f, true, ActorMode.Worker, Build.OnGround(FarmFields.Instance.Fields[i % FarmFields.Instance.Fields.Count].center));
                if (w != null) { w.radius = 9f; w.speed = 1.1f; }
            }
        if (DeliveryZone.Market != null)
            for (int i = 0; i < 3; i++)
            {
                var b = Actor.Spawn(People[(p++) % People.Length], 1.5f, true, ActorMode.Wander, Build.OnGround(DeliveryZone.Market.transform.position + new Vector3(8f + i * 2f, 0, 4f - i * 3f)));
                if (b != null) { b.home = DeliveryZone.Market.transform.position + new Vector3(10f, 0, 0); b.radius = 7f; b.speed = 1f; }
            }

        // Drivers (visible only while you drive)
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
        {
            Vector3 seat = v is Tractor ? new Vector3(0f, 0.6f, -0.55f) : new Vector3(-0.42f, 0.35f, 0.55f);
            var d = Actor.Spawn(People[0], 1.05f, true, ActorMode.Driver, v.transform.TransformPoint(seat), v.transform);
            if (d != null) { d.vehicle = v; d.transform.localPosition = seat; d.transform.localRotation = Quaternion.identity; }
        }
    }

    private void OnDestroy() => GameEvents.UpgradeBought -= OnSale;

    private void OnSale(string id)
    {
        foreach (var a in FindObjectsByType<Actor>(FindObjectsSortMode.None))
            if (a.mode == ActorMode.Stand) a.React("emote-yes");
    }
}
