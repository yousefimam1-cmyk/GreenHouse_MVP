using System.Collections.Generic;
using UnityEngine;

/// <summary>Small procedural meshes for the desert dressing (triangle prisms, dunes, ground-hugging strips).</summary>
public static class ProcMesh
{
    private static Mesh triangle;

    /// <summary>A unit triangular prism: base on y = 0 from x = -0.5..0.5, apex at (0, 1), depth z = -0.5..0.5.</summary>
    public static Mesh TrianglePrism()
    {
        if (triangle != null) return triangle;
        var v = new List<Vector3>();
        var t = new List<int>();
        Vector3 a0 = new Vector3(-0.5f, 0, -0.5f), b0 = new Vector3(0.5f, 0, -0.5f), c0 = new Vector3(0, 1, -0.5f);
        Vector3 a1 = new Vector3(-0.5f, 0, 0.5f), b1 = new Vector3(0.5f, 0, 0.5f), c1 = new Vector3(0, 1, 0.5f);
        Tri(v, t, a0, c0, b0);              // front (-z)
        Tri(v, t, a1, b1, c1);              // back (+z)
        Quad(v, t, a0, a1, c1, c0);         // left slope
        Quad(v, t, b0, c0, c1, b1);         // right slope
        Quad(v, t, a0, b0, b1, a1);         // bottom
        triangle = Finish("TrianglePrism", v, t);
        return triangle;
    }

    private static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    private static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
    }

    public static Mesh Finish(string name, List<Vector3> v, List<int> t)
    {
        var m = new Mesh();
        m.name = name;
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v.ToArray();
        m.triangles = t.ToArray();
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// A low dune (barchan-like: gentle windward side, steeper lee side) as a world-space mesh whose rim sits
    /// slightly below the ground. along is the dune's long axis; the steep lee side faces Cross(along, up).
    /// ground(p) returns the ground height. Vertices are relative to centre (GameObject at centre, no rotation).
    /// </summary>
    public static Mesh Dune(Vector3 centre, Vector3 along, float halfLength, float halfWidth, float height, System.Func<Vector3, float> ground, int seed)
    {
        const int rings = 8, segs = 24;
        var v = new List<Vector3>();
        var t = new List<int>();
        along.y = 0f; along = along.normalized;
        Vector3 across = Vector3.Cross(along, Vector3.up).normalized;   // keeps the winding facing up
        var rnd = new System.Random(seed);
        float phase = (float)rnd.NextDouble() * 10f;
        v.Add(Point(centre, along, across, 0f, 0f, halfLength, halfWidth, height, ground, phase));
        for (int r = 1; r <= rings; r++)
        {
            float rr = r / (float)rings;
            for (int s = 0; s < segs; s++)
            {
                float a = s / (float)segs * Mathf.PI * 2f;
                v.Add(Point(centre, along, across, Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, halfLength, halfWidth, height, ground, phase));
            }
        }
        for (int s = 0; s < segs; s++) { t.Add(0); t.Add(1 + (s + 1) % segs); t.Add(1 + s); }
        for (int r = 1; r < rings; r++)
        {
            int a0 = 1 + (r - 1) * segs, b0 = 1 + r * segs;
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                t.Add(a0 + s); t.Add(a0 + s1); t.Add(b0 + s1);
                t.Add(a0 + s); t.Add(b0 + s1); t.Add(b0 + s);
            }
        }
        return Finish("Dune", v, t);
    }

    private static Vector3 Point(Vector3 c, Vector3 along, Vector3 across, float u, float w, float hl, float hw, float h, System.Func<Vector3, float> ground, float phase)
    {
        Vector3 world = c + along * (u * hl) + across * (w * hw);
        float ww = w > 0f ? w * 1.5f : w * 0.85f;                 // lee side (+across) is steeper
        float k = Mathf.Max(0f, 1f - (u * u + ww * ww));
        float ripple = 0.06f * Mathf.Sin((u * hl + w * hw * 0.3f) * 1.7f + phase) * k;
        float y = ground(world) - c.y + h * Mathf.Pow(k, 1.25f) + ripple - 0.35f * (1f - k);
        return new Vector3(world.x - c.x, y, world.z - c.z);
    }

    /// <summary>A flat strip that follows the ground along a polyline (roads). Vertices are world positions.</summary>
    public static Mesh Strip(IList<Vector3> path, float width, float lift, System.Func<Vector3, float> ground)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 dir = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
            Vector3 l = path[i] - side, r = path[i] + side;
            l.y = ground(l) + lift; r.y = ground(r) + lift;
            v.Add(l); v.Add(r);
            if (i > 0)
            {
                int a = v.Count - 4;
                t.Add(a); t.Add(a + 2); t.Add(a + 1);
                t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
            }
        }
        return Finish("Strip", v, t);
    }

    /// <summary>
    /// Merge every renderer under root (except mesh-collider objects and those keep() accepts) into a few
    /// meshes per material (chunks of up to 60k vertices), then destroy the merged parts. Hundreds of primitive parts
    /// become a handful of objects and draw calls. Returns the number of parts merged.
    /// </summary>
    public static int MergeChildren(Transform root, System.Func<MeshRenderer, bool> keep)
    {
        var groups = new Dictionary<Material, List<MeshFilter>>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
            // (primitive parts still carry their auto collider until the end of the frame, so test MeshCollider only)
            if (mf.GetComponent<MeshCollider>() != null || (keep != null && keep(mr))) continue;
            List<MeshFilter> list;
            if (!groups.TryGetValue(mr.sharedMaterial, out list)) groups[mr.sharedMaterial] = list = new List<MeshFilter>();
            list.Add(mf);
        }
        int merged = 0;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (var g in groups)
        {
            var batch = new List<CombineInstance>();
            int verts = 0, part = 0;
            for (int i = 0; i <= g.Value.Count; i++)
            {
                bool flush = i == g.Value.Count || verts + g.Value[i].sharedMesh.vertexCount > 60000;
                if (flush && batch.Count > 0)
                {
                    var mesh = new Mesh();
                    mesh.name = "Merged " + g.Key.name + " " + part;
                    mesh.CombineMeshes(batch.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject("Merged " + part++);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = g.Key;
                    batch.Clear(); verts = 0;
                }
                if (i == g.Value.Count) break;
                var mf = g.Value[i];
                batch.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * mf.transform.localToWorldMatrix });
                verts += mf.sharedMesh.vertexCount;
            }
            foreach (var mf in g.Value) { Object.Destroy(mf.gameObject); merged++; }
        }
        return merged;
    }
}
