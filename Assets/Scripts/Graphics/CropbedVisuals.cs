using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Greenhouse beds show real growth stages: each living plant under a bed's foliage swaps its mesh to the FarmCrops
/// stage model for the bed's growth (seedling -> young -> grown -> ripe). The bed's own crop is used when FarmCrops has
/// it (tomato, cucumber, melon, lettuce); otherwise each plant keeps its own kind (Carrot, Wheat...). Dead plants and
/// "C_" collision meshes are left alone. Works next to Cropbed's own growth scaling without changing Cropbed.
/// </summary>
[MazraaSystem(60)]
public class CropbedVisuals : MonoBehaviour
{
    /// <summary>A swapped-in plant may be at most this much bigger than the plant the scene had there.</summary>
    public float maxGrowthOverOriginal = 1.3f;

    private class Swap { public MeshFilter filter; public MeshRenderer renderer; public string crop; }
    private class Bed { public Cropbed bed; public readonly List<Swap> swaps = new List<Swap>(); public int shown = -1; }

    private readonly List<Bed> beds = new List<Bed>();
    private readonly HashSet<MeshFilter> fitted = new HashSet<MeshFilter>();
    private float timer;
    public int SwappedPlants { get; private set; }

    private void Start() => Invoke(nameof(Collect), 0.5f);

    private void Collect()
    {
        beds.Clear(); SwappedPlants = 0;
        foreach (var cb in FindObjectsByType<Cropbed>(FindObjectsSortMode.None))
        {
            if (cb.foliage == null) continue;
            var entry = new Bed { bed = cb };
            string bedCrop = cb.Crop != null && CropVisuals.HasModels(cb.Crop.id) ? cb.Crop.id : null;
            foreach (var f in cb.foliage)
            {
                if (f == null || f.transform.childCount == 0) continue;
                Transform alive = f.transform.GetChild(0);
                string crop = bedCrop ?? CropVisuals.CropFromName(f.name) ?? CropVisuals.CropFromName(alive.name);
                if (crop == null) continue;
                foreach (var mf in alive.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.gameObject.name.StartsWith("C_")) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled) continue;
                    entry.swaps.Add(new Swap { filter = mf, renderer = mr, crop = crop });
                    FitToOriginal(mf, crop);
                }
            }
            if (entry.swaps.Count > 0) { beds.Add(entry); SwappedPlants += entry.swaps.Count; }
        }
        Refresh(true);
    }

    // Beds were laid out for small plants (often scaled up x2): keep a big ripe model (tomato 1.4 m) from towering
    // over its neighbours or the greenhouse roof by limiting it to maxGrowthOverOriginal x the original plant.
    private void FitToOriginal(MeshFilter mf, string crop)
    {
        Mesh ripe; Material[] mats;
        if (!fitted.Add(mf)) return;
        if (mf.sharedMesh == null || !CropVisuals.TryGetMesh(crop, 1f, out ripe, out mats) || ripe == null) return;
        Vector3 a = mf.sharedMesh.bounds.size, b = ripe.bounds.size;
        float orig = Mathf.Max(a.x, Mathf.Max(a.y, a.z)), next = Mathf.Max(b.x, Mathf.Max(b.y, b.z));
        if (orig <= 0.001f || next <= 0.001f) return;
        float k = Mathf.Min(1f, orig * maxGrowthOverOriginal / next);
        if (k < 0.999f) mf.transform.localScale *= k;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = 0.5f;
        Refresh(false);
    }

    private void Refresh(bool force)
    {
        foreach (var b in beds)
        {
            if (b.bed == null) continue;
            int stage = CropVisuals.StageIndex(b.bed.Growth);
            if (!force && stage == b.shown) continue;
            b.shown = stage;
            float g = stage / 3f;
            foreach (var s in b.swaps)
            {
                if (s.filter == null) continue;
                Mesh mesh; Material[] mats;
                if (!CropVisuals.TryGetMesh(s.crop, g, out mesh, out mats)) continue;
                s.filter.sharedMesh = mesh;
                s.renderer.sharedMaterials = mats;
            }
        }
    }

    /// <summary>Stage shown for a bed (0..3), or -1 if the bed has no swappable plants. For tests/debug.</summary>
    public int StageOf(Cropbed bed)
    {
        foreach (var b in beds) if (b.bed == bed) return b.shown;
        return -1;
    }
}
