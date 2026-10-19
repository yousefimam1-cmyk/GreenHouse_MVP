using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Builds the bought utility equipment in the world (placeholder geometry, or Resources/Models/&lt;name&gt; when present):
/// SEC pole + transformer, diesel generator, biogas dome, solar rows (dusty colour), wind turbines (spinning),
/// deep well pump, water tanks and the treated-wastewater valve (field irrigation is FarmCore's IrrigationSystem). Yard position: an object named "UtilitiesSpot", else beside the player start.
/// </summary>
public class UtilityYard : MonoBehaviour
{
    private Transform yard;
    private readonly List<Transform> blades = new List<Transform>();
    private readonly List<MeshRenderer> panels = new List<MeshRenderer>();
    private Transform tankWater;
    private int shownDust = -1;
    private bool built;

    private static readonly Color PanelBlue = new Color(0.08f, 0.14f, 0.32f), Sand = new Color(0.78f, 0.66f, 0.45f);

    private void Start()
    {
        var root = new GameObject("Utilities Yard");
        yard = root.transform;
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform start = inputs != null ? inputs.transform : (Camera.main != null ? Camera.main.transform : transform);
        Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var spot = GameObject.Find("UtilitiesSpot");
        Vector3 pos = spot != null ? spot.transform.position : start.position - fwd * 14f + right * 14f;
        yard.SetPositionAndRotation(Build.OnGround(pos), Quaternion.LookRotation(fwd));
        built = true;
        Rebuild();
    }

    private void OnDestroy()
    {
        if (yard != null) Destroy(yard.gameObject);
    }

    private UtilityModel M => UtilityGrid.Instance != null ? UtilityGrid.Instance.Model : null;

    private void Update()
    {
        var m = M;
        if (m == null) return;
        float windSpin = m.Wind > 0 ? m.WindOut / Mathf.Max(0.1f, UtilityModel.WindKw * m.Wind) : 0f;
        foreach (var b in blades) if (b != null) b.localRotation = b.localRotation * Quaternion.Euler(0f, 0f, (60f + 360f * windSpin) * Time.deltaTime);
    }

    /// <summary>Update the parts that change with state (dust colour, tank level).</summary>
    public void Refresh()
    {
        var m = M;
        if (m == null || !built) return;
        int dust = Mathf.RoundToInt(m.Dust / 0.6f * 5f);
        if (dust != shownDust)
        {
            shownDust = dust;
            var mat = Build.Mat(Color.Lerp(PanelBlue, Sand, dust / 5f * 0.8f), Mathf.Lerp(0.9f, 0.2f, dust / 5f), 0.2f);
            foreach (var p in panels) if (p != null) p.sharedMaterial = mat;
        }
        if (tankWater != null)
        {
            float f = m.StorageCapacity > 0f ? Mathf.Clamp01(m.Stored / m.StorageCapacity) : 0f;
            tankWater.localScale = new Vector3(tankWater.localScale.x, Mathf.Max(0.02f, Mathf.Round(f * 10f) / 10f * 2.4f), tankWater.localScale.z);
            tankWater.localPosition = new Vector3(tankWater.localPosition.x, 0.15f + tankWater.localScale.y * 0.5f, tankWater.localPosition.z);
        }
    }

    /// <summary>Rebuild every object from the owned equipment.</summary>
    public void Rebuild()
    {
        var m = M;
        if (m == null || !built) return;
        Clear(yard);
        blades.Clear(); panels.Clear(); tankWater = null; shownDust = -1;

        var steel = Build.Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.6f);
        var white = Build.Mat(new Color(0.93f, 0.93f, 0.9f), 0.4f);
        var dark = Build.Mat(new Color(0.1f, 0.1f, 0.11f), 0.3f);
        var green = Build.Mat(new Color(0.18f, 0.42f, 0.25f), 0.3f);
        var concrete = Build.Mat(new Color(0.62f, 0.6f, 0.56f), 0.1f);
        var waterMat = Build.Mat(new Color(0.25f, 0.5f, 0.75f), 0.95f);

        // SEC connection: pole + transformer (always there; size grows with the upgrade level)
        var grid = Item("SEC Grid", new Vector3(0, 0, 0), UtilityPartKind.Info, "SEC grid connection", new Vector3(1.6f, 2f, 1.6f));
        if (Build.TryModel("Utilities/PowerPole", grid, 0f, 0f) == null)
        {
            Build.Part(PrimitiveType.Cylinder, grid, new Vector3(-1.2f, 4f, 0), new Vector3(0.25f, 4f, 0.25f), Build.Mat(new Color(0.45f, 0.4f, 0.35f), 0.1f), "Pole");
            Build.Part(PrimitiveType.Cube, grid, new Vector3(-1.2f, 7.6f, 0), new Vector3(2.2f, 0.15f, 0.15f), steel, "Crossarm");
            Build.Part(PrimitiveType.Cube, grid, new Vector3(0, 0.9f, 0), new Vector3(1.4f, 1.8f, 1.2f) * (1f + 0.2f * m.GridLevel), green, "Transformer");
            Build.Part(PrimitiveType.Cube, grid, new Vector3(0, 0.05f, 0), new Vector3(2.2f, 0.1f, 2f), concrete, "Base");
        }
        Build.Label(grid, new Vector3(0, 3.2f, 0), "SEC grid connection", 2f, Color.white);

        if (m.Generator)
        {
            var gen = Item("Diesel Generator", new Vector3(4.5f, 0, 0), UtilityPartKind.Generator, "Diesel generator", new Vector3(2.6f, 1.6f, 1.4f));
            if (Build.TryModel("Utilities/Generator", gen, 0f, 0f) == null)
            {
                Build.Part(PrimitiveType.Cube, gen, new Vector3(0, 0.8f, 0), new Vector3(2.6f, 1.5f, 1.3f), Build.Mat(new Color(0.95f, 0.75f, 0.1f), 0.4f), "Body");
                Build.Part(PrimitiveType.Cylinder, gen, new Vector3(0.9f, 1.9f, 0), new Vector3(0.15f, 0.4f, 0.15f), dark, "Exhaust");
                Build.Part(PrimitiveType.Cube, gen, new Vector3(-1.31f, 0.9f, 0), new Vector3(0.02f, 0.9f, 1f), dark, "Grille");
            }
            Build.Label(gen, new Vector3(0, 2.8f, 0), "Diesel generator", 2f, Color.white);
        }

        if (m.Biogas)
        {
            var bio = Item("Biogas Digester", new Vector3(9f, 0, 0), UtilityPartKind.Biogas, "Biogas digester", new Vector3(3.6f, 2.4f, 3.6f));
            if (Build.TryModel("Utilities/Biogas", bio, 3.6f, 0f) == null)
            {
                Build.Part(PrimitiveType.Cylinder, bio, new Vector3(0, 0.5f, 0), new Vector3(3.6f, 0.5f, 3.6f), concrete, "Tank");
                Build.Part(PrimitiveType.Sphere, bio, new Vector3(0, 1f, 0), new Vector3(3.4f, 2.4f, 3.4f), Build.Mat(new Color(0.25f, 0.45f, 0.3f), 0.5f), "Dome");
                Build.Part(PrimitiveType.Cylinder, bio, new Vector3(1.6f, 1.6f, 0), new Vector3(0.12f, 0.8f, 0.12f), steel, "Gas pipe");
            }
            Build.Label(bio, new Vector3(0, 3.4f, 0), "Biogas digester", 2f, Color.white);
        }

        for (int i = 0; i < m.Solar; i++)
        {
            var sol = Item("Solar Array " + (i + 1), new Vector3(-1f + i * 5f, 0, -7f), UtilityPartKind.Solar, "Solar panels", new Vector3(4.4f, 1.6f, 2.4f));
            if (Build.TryModel("Utilities/SolarPanel", sol, 4.4f, 0f) == null)
            {
                foreach (var x in new[] { -1.8f, 1.8f })
                {
                    Build.Part(PrimitiveType.Cube, sol, new Vector3(x, 0.55f, 0.6f), new Vector3(0.08f, 1.1f, 0.08f), steel, "Leg");
                    Build.Part(PrimitiveType.Cube, sol, new Vector3(x, 0.3f, -0.6f), new Vector3(0.08f, 0.6f, 0.08f), steel, "Leg");
                }
                for (int k = 0; k < 4; k++)
                {
                    var p = Build.Part(PrimitiveType.Cube, sol, new Vector3(-1.65f + k * 1.1f, 0.95f, 0), new Vector3(1.05f, 0.05f, 2.2f), Build.Mat(PanelBlue, 0.9f, 0.2f), "Panel", new Vector3(-25f, 0, 0));
                    panels.Add(p.GetComponent<MeshRenderer>());
                }
            }
            if (i == 0) Build.Label(sol, new Vector3(0, 2.4f, 0), "Solar panels", 2f, Color.white);
        }

        for (int i = 0; i < m.Wind; i++)
        {
            var wt = Item("Wind Turbine " + (i + 1), new Vector3(-8f, 0, -5f - i * 8f), UtilityPartKind.Info, "Small wind turbine", new Vector3(1f, 3f, 1f));
            Build.Part(PrimitiveType.Cylinder, wt, new Vector3(0, 6f, 0), new Vector3(0.35f, 6f, 0.35f), white, "Tower");
            Build.Part(PrimitiveType.Cube, wt, new Vector3(0, 12.1f, 0.2f), new Vector3(0.6f, 0.6f, 1.4f), white, "Nacelle");
            var hub = new GameObject("Rotor").transform; hub.SetParent(wt, false); hub.localPosition = new Vector3(0, 12.1f, -0.6f);
            for (int k = 0; k < 3; k++)
            {
                var arm = new GameObject("Blade").transform; arm.SetParent(hub, false); arm.localEulerAngles = new Vector3(0, 0, k * 120f);
                Build.Part(PrimitiveType.Cube, arm, new Vector3(0, 1.7f, 0), new Vector3(0.28f, 3.4f, 0.06f), white, "Blade");
            }
            blades.Add(hub);
            if (i == 0) Build.Label(wt, new Vector3(0, 2.6f, 0), "Small wind turbine", 2f, Color.white);
        }

        if (m.DeepWell)
        {
            var dw = Item("Deep Well", new Vector3(-5f, 0, 5f), UtilityPartKind.Well, "Deep aquifer well", new Vector3(2f, 2.4f, 2f));
            if (Build.TryModel("Utilities/DeepWell", dw, 2f, 0f) == null)
            {
                Build.Part(PrimitiveType.Cube, dw, new Vector3(0, 0.1f, 0), new Vector3(2f, 0.2f, 2f), concrete, "Pad");
                Build.Part(PrimitiveType.Cylinder, dw, new Vector3(0, 0.8f, 0), new Vector3(0.5f, 0.6f, 0.5f), Build.Mat(new Color(0.15f, 0.35f, 0.65f), 0.5f, 0.3f), "Pump");
                Build.Part(PrimitiveType.Cylinder, dw, new Vector3(0, 1.6f, 0.6f), new Vector3(0.18f, 0.6f, 0.18f), steel, "Riser", new Vector3(90, 0, 0));
            }
            Build.Label(dw, new Vector3(0, 2.8f, 0), "Deep aquifer well", 2f, Color.white);
        }

        // Ground reservoir (always) + bought tanks
        var res = Item("Water Reservoir", new Vector3(0, 0, 6f), UtilityPartKind.Tank, "Water tank", new Vector3(3f + 3.5f * m.Tanks, 2.6f, 3f));
        Build.Part(PrimitiveType.Cube, res, new Vector3(0, 0.15f, 0), new Vector3(3f, 0.3f, 3f), concrete, "Reservoir");
        var w = Build.Part(PrimitiveType.Cube, res, new Vector3(0, 0.3f, 0), new Vector3(2.6f, 0.1f, 2.6f), waterMat, "Water");
        tankWater = w.transform;
        for (int i = 0; i < m.Tanks; i++)
        {
            var tk = new GameObject("Tank " + (i + 1)).transform; tk.SetParent(res, false); tk.localPosition = new Vector3(3.5f * (i + 1), 0, 0);
            if (Build.TryModel("Utilities/WaterTank", tk, 3f, 0f) == null)
            {
                Build.Part(PrimitiveType.Cylinder, tk, new Vector3(0, 1.3f, 0), new Vector3(3f, 1.3f, 3f), white, "Tank");
                Build.Part(PrimitiveType.Cylinder, tk, new Vector3(0, 2.65f, 0), new Vector3(3.1f, 0.05f, 3.1f), Build.Mat(new Color(0.2f, 0.45f, 0.75f), 0.4f), "Lid");
            }
        }
        Build.Label(res, new Vector3(0, 3.6f, 0), "Water tank", 2f, Color.white);

        if (m.Tse)
        {
            var tse = Item("TSE Valve", new Vector3(10f, 0, 6f), UtilityPartKind.Info, "Treated wastewater line", new Vector3(1.4f, 1.6f, 1.4f));
            var purple = Build.Mat(new Color(0.5f, 0.25f, 0.6f), 0.4f, 0.3f); // reclaimed water pipes are purple
            Build.Part(PrimitiveType.Cylinder, tse, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.6f, 0.3f), purple, "Pipe");
            Build.Part(PrimitiveType.Cylinder, tse, new Vector3(0, 1.25f, 0), new Vector3(0.7f, 0.05f, 0.7f), Build.Mat(new Color(0.8f, 0.1f, 0.1f), 0.4f), "Valve");
            Build.Label(tse, new Vector3(0, 2.2f, 0), "Treated wastewater line", 2f, Color.white);
        }

        foreach (var r in yard.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        Refresh();
    }

    private Transform Item(string name, Vector3 local, UtilityPartKind kind, string label, Vector3 colliderSize)
    {
        var go = new GameObject(name);
        go.layer = 7;
        go.transform.SetParent(yard, false);
        go.transform.localPosition = local;
        go.transform.position = Build.OnGround(go.transform.position);
        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0, colliderSize.y * 0.5f, 0); col.size = colliderSize;
        var part = go.AddComponent<UtilityPart>();
        part.kind = kind; part.label = label;
        return go.transform;
    }

    private static void Clear(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }
}
