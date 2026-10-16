using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small object the player can pick up and carry by hand ([F] / D-pad up): hay bales and empty produce crates.
/// Weight slows the player (PlayerLife). A hay bale put down inside the pen feeds every animal for the day.
/// </summary>
public class Carriable : MonoBehaviour
{
    public static readonly List<Carriable> All = new List<Carriable>();

    public string kind = "crate";     // "hay_bale" or "crate"
    public float weightKg = 12f;
    public bool Carried { get; set; }

    public string NameKey => kind == "hay_bale" ? "Hay bale" : "Produce crate";

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);
    private void OnDestroy() => All.Remove(this);

    public static Carriable Spawn(string kind, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(kind == "hay_bale" ? "Hay Bale" : "Produce Crate");
        go.transform.SetPositionAndRotation(Build.OnGround(pos), rot);
        var c = go.AddComponent<Carriable>();
        c.kind = kind;
        if (kind == "hay_bale")
        {
            c.weightKg = 25f;
            var straw = Build.Mat(new Color(0.83f, 0.7f, 0.38f), 0.05f);
            var twine = Build.Mat(new Color(0.55f, 0.36f, 0.2f), 0.1f);
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.5f, 0.5f), straw, "Bale");
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(-0.22f, 0.25f, 0), new Vector3(0.03f, 0.52f, 0.52f), twine, "Twine");
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0.22f, 0.25f, 0), new Vector3(0.03f, 0.52f, 0.52f), twine, "Twine");
        }
        else
        {
            c.weightKg = 8f;
            var wood = Build.Mat(new Color(0.66f, 0.5f, 0.3f), 0.1f);
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.05f, 0), new Vector3(0.6f, 0.06f, 0.42f), wood, "Base");
            foreach (var x in new[] { -0.28f, 0.28f }) Build.Part(PrimitiveType.Cube, go.transform, new Vector3(x, 0.2f, 0), new Vector3(0.04f, 0.3f, 0.42f), wood, "Side");
            foreach (var z in new[] { -0.19f, 0.19f }) Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.2f, z), new Vector3(0.6f, 0.3f, 0.04f), wood, "Slat");
        }
        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.25f, 0); col.size = kind == "hay_bale" ? new Vector3(0.9f, 0.5f, 0.5f) : new Vector3(0.6f, 0.4f, 0.42f);
        return c;
    }

    public void PickUp(Transform holder)
    {
        Carried = true;
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
        transform.SetParent(holder, false);
        transform.localPosition = new Vector3(0f, 0.85f, 0.75f);
        transform.localRotation = Quaternion.identity;
    }

    public void PutDown(Vector3 pos, Quaternion rot)
    {
        Carried = false;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(Build.OnGround(pos), rot);
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = true;
    }
}
