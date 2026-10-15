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

    private void Start()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform start = inputs != null ? inputs.transform : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var spot = GameObject.Find("FieldsSpot");
        Vector3 center = spot != null ? spot.transform.position : start.position + fwd * 18f;
        Quaternion rot = Quaternion.LookRotation(fwd);
        int i = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                Vector3 local = new Vector3((c - (columns - 1) * 0.5f) * (plotSize.x + gap), 0, (r - (rows - 1) * 0.5f) * (plotSize.y + gap));
                Vector3 p = Build.OnGround(center + rot * local);
                var go = new GameObject("Field " + (i + 1));
                go.transform.SetPositionAndRotation(p, rot);
                var plot = go.AddComponent<FieldPlot>();
                plot.Setup(i, plotSize);
                Plots.Add(plot);
                i++;
            }
    }

    public void ResetAll() { foreach (var p in Plots) if (p != null) p.ResetPlot(); }
}
