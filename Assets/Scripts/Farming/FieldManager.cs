using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>Lays out the open-field plots (2 x 3) in front of the player's start, or around an object named "FieldsSpot".</summary>
public class FieldManager : MonoBehaviour
{
    public static FieldManager Instance;
    public int columns = 3, rows = 2;
    public Vector2 plotSize = new Vector2(6f, 6f);
    public float gap = 1.6f;
    public readonly List<FieldPlot> Plots = new List<FieldPlot>();

    private void Awake() => Instance = this;

    /// <summary>Centre and facing of the plot grid (extra plots bought at the land office extend it).</summary>
    public Vector3 GridCenter { get; private set; }
    public Quaternion GridRotation { get; private set; } = Quaternion.identity;

    private void Start()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform start = inputs != null ? inputs.transform : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var spot = GameObject.Find("FieldsSpot");
        GridCenter = spot != null ? spot.transform.position : start.position + fwd * 18f;
        GridRotation = Quaternion.LookRotation(fwd);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                AddPlot(r, c);
    }

    /// <summary>Add a plot at grid cell (row, col); rows beyond `rows` extend the field. Index = Plots.Count (its save key).</summary>
    public FieldPlot AddPlot(int r, int c)
    {
        int i = 0;   // save key = place in the farm grid (open-world plots have their own keys from WorldPlotBase)
        foreach (var q in Plots) if (q != null && q.index < WorldPlotBase) i++;
        Vector3 local = new Vector3((c - (columns - 1) * 0.5f) * (plotSize.x + gap), 0, (r - (rows - 1) * 0.5f) * (plotSize.y + gap));
        Vector3 p = Build.OnGround(GridCenter + GridRotation * local);
        var go = new GameObject("Field " + (i + 1));
        go.transform.SetPositionAndRotation(p, GridRotation);
        var plot = go.AddComponent<FieldPlot>();
        plot.Setup(i, plotSize);
        Plots.Add(plot);
        return plot;
    }

    // ---- open world (World/WorldLand): farmland bought elsewhere on the map
    /// <summary>Save keys of open-world plots start here (mazraa.plot.200+), clear of the farm grid and the land office.</summary>
    public const int WorldPlotBase = 200;

    /// <summary>Add a plot anywhere (an open-world parcel) with its own save index.</summary>
    public FieldPlot AddPlotAt(int saveIndex, Vector3 pos, Quaternion rot, Vector2 size)
    {
        var go = new GameObject("Field " + (saveIndex + 1));
        go.transform.SetPositionAndRotation(pos, rot);
        var plot = go.AddComponent<FieldPlot>();
        plot.Setup(saveIndex, size);
        Plots.Add(plot);
        return plot;
    }
    // ---- end open world

    public void ResetAll() { foreach (var p in Plots) if (p != null) p.ResetPlot(); }
}
