using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds merged meshes for the open world directly (boxes, cylinders, cones, gable roofs, discs, road strips), one mesh
/// per material, so a whole town is a handful of renderers and draw calls and no temporary GameObjects are created.
/// Materials are the shared cached ones from Build.Mat (same colour = same material across the game).
/// </summary>
public class WorldMesh
{
    private class Part { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>(); }
    private readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
    public int Triangles { get; private set; }

    public static Material Mat(Color c, float smooth = 0.12f) => Build.Mat(c, smooth);

    private Part P(Material m)
    {
        if (!parts.TryGetValue(m, out var p)) parts[m] = p = new Part();
        return p;
    }

    private void Quad(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = p.v.Count;
        p.v.Add(a); p.v.Add(b); p.v.Add(c); p.v.Add(d);
        p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2); p.t.Add(i); p.t.Add(i + 2); p.t.Add(i + 3);
        Triangles += 2;
    }

    private void Tri(Part p, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = p.v.Count;
        p.v.Add(a); p.v.Add(b); p.v.Add(c);
        p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2);
        Triangles++;
    }

    private static Vector3 C(Vector3 basePos, Quaternion rot, float a, float b, float d) => basePos + rot * new Vector3(a, b, d);
    private static Vector3 S(Vector3 center, float r, float a, float b) => center + new Vector3(Mathf.Cos(a) * Mathf.Sin(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Cos(b)) * r;

    /// <summary>Box with its base centre at basePos (y = bottom), turned by rot.</summary>
    public void Box(Vector3 basePos, Vector3 size, Quaternion rot, Color c, float smooth = 0.12f) => Box(basePos, size, rot, Mat(c, smooth));

    public void Box(Vector3 basePos, Vector3 size, Quaternion rot, Material m)
    {
        var p = P(m);
        float x = size.x * 0.5f, z = size.z * 0.5f, y = size.y;
        Vector3 p000 = C(basePos, rot, -x, 0, -z), p100 = C(basePos, rot, x, 0, -z), p010 = C(basePos, rot, -x, y, -z), p110 = C(basePos, rot, x, y, -z);
        Vector3 p001 = C(basePos, rot, -x, 0, z), p101 = C(basePos, rot, x, 0, z), p011 = C(basePos, rot, -x, y, z), p111 = C(basePos, rot, x, y, z);
        Quad(p, p000, p010, p110, p100);   // -z
        Quad(p, p101, p111, p011, p001);   // +z
        Quad(p, p001, p011, p010, p000);   // -x
        Quad(p, p100, p110, p111, p101);   // +x
        Quad(p, p010, p011, p111, p110);   // top
    }

    /// <summary>Gable roof (triangular prism) over a w x d footprint, ridge along the local z axis.</summary>
    public void Gable(Vector3 basePos, float w, float d, float h, Quaternion rot, Color c)
    {
        var p = P(Mat(c));
        float x = w * 0.5f, z = d * 0.5f;
        Vector3 l0 = C(basePos, rot, -x, 0, -z), r0 = C(basePos, rot, x, 0, -z), t0 = C(basePos, rot, 0, h, -z), l1 = C(basePos, rot, -x, 0, z), r1 = C(basePos, rot, x, 0, z), t1 = C(basePos, rot, 0, h, z);
        Tri(p, l0, t0, r0);
        Tri(p, r1, t1, l1);
        Quad(p, l1, t1, t0, l0);
        Quad(p, r0, t0, t1, r1);
    }

    /// <summary>Upright cylinder (n sides) from basePos up h; rot tilts it (e.g. a horizontal tank).</summary>
    public void Cylinder(Vector3 basePos, float r, float h, int n, Quaternion rot, Color c, bool cap = true, float smooth = 0.12f)
    {
        var p = P(Mat(c, smooth));
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 d0 = new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r), d1 = new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
            Vector3 b0 = basePos + rot * d0, b1 = basePos + rot * d1, u0 = basePos + rot * (d0 + Vector3.up * h), u1 = basePos + rot * (d1 + Vector3.up * h);
            Quad(p, b1, u1, u0, b0);
            if (cap) Tri(p, u1, basePos + rot * (Vector3.up * h), u0);
        }
    }

    /// <summary>Cone (or a frustum when rTop > 0) from basePos up h.</summary>
    public void Cone(Vector3 basePos, float r, float h, int n, Color c, float rTop = 0f)
    {
        var p = P(Mat(c));
        Vector3 top = basePos + Vector3.up * h;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 b0 = basePos + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r), b1 = basePos + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
            if (rTop <= 0f) Tri(p, b1, top, b0);
            else
            {
                Vector3 t0 = top + new Vector3(Mathf.Sin(a0) * rTop, 0f, Mathf.Cos(a0) * rTop), t1 = top + new Vector3(Mathf.Sin(a1) * rTop, 0f, Mathf.Cos(a1) * rTop);
                Quad(p, b1, t1, t0, b0);
                Tri(p, t1, top, t0);
            }
        }
    }

    /// <summary>Low-poly dome / ball (half = upper hemisphere only).</summary>
    public void Dome(Vector3 center, float r, Color c, bool half = true)
    {
        var p = P(Mat(c));
        int rings = 4, segs = 10;
        float start = half ? 0f : -Mathf.PI * 0.5f;
        for (int i = 0; i < rings; i++)
        {
            float a0 = start + (Mathf.PI * 0.5f - start) * i / rings, a1 = start + (Mathf.PI * 0.5f - start) * (i + 1) / rings;
            for (int j = 0; j < segs; j++)
            {
                float b0 = j * Mathf.PI * 2f / segs, b1 = (j + 1) * Mathf.PI * 2f / segs;
                Quad(p, S(center, r, a0, b1), S(center, r, a1, b1), S(center, r, a1, b0), S(center, r, a0, b0));
            }
        }
    }

    /// <summary>Flat disc lying on y = center.y.</summary>
    public void Disc(Vector3 center, float r, int n, Color c, float smooth = 0.1f)
    {
        var p = P(Mat(c, smooth));
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Tri(p, center, center + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r), center + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r));
        }
    }

    /// <summary>Flat ring (annulus) lying on y = center.y.</summary>
    public void Ring(Vector3 center, float r0, float r1, int n, Color c)
    {
        var p = P(Mat(c, 0.3f));
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
            Quad(p, center + d0 * r0, center + d0 * r1, center + d1 * r1, center + d1 * r0);
        }
    }

    /// <summary>Road strip along a polyline at the given surface heights (+lift). from..to = point range.</summary>
    public void Strip(IList<Vector3> path, IList<float> heights, float width, float lift, Material m, int from = 0, int to = -1)
    {
        if (to < 0) to = path.Count - 1;
        if (to - from < 1) return;
        var p = P(m);
        int start = p.v.Count;
        for (int i = from; i <= to; i++)
        {
            Vector3 dir = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
            float y = heights[i] + lift;
            Vector3 c = new Vector3(path[i].x, y, path[i].z);
            p.v.Add(c - side); p.v.Add(c + side);
            if (i > from)
            {
                int a = p.v.Count - 4;
                p.t.Add(a); p.t.Add(a + 2); p.t.Add(a + 1);
                p.t.Add(a + 1); p.t.Add(a + 2); p.t.Add(a + 3);
                Triangles += 2;
            }
        }
    }

    /// <summary>Thin flat quad on the ground between a and b (centre-line dashes, field rows).</summary>
    public void FlatLine(Vector3 a, Vector3 b, float width, Material m)
    {
        Vector3 d = b - a; d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return;
        Vector3 side = Vector3.Cross(Vector3.up, d.normalized) * (width * 0.5f);
        Quad(P(m), a - side, b - side, b + side, a + side);
    }

    public bool Empty => parts.Count == 0;

    /// <summary>Create one renderer per material under parent; returns them. Shadows only for solid things (shadows = false for roads and water).</summary>
    public List<MeshRenderer> Emit(Transform parent, string name, bool shadows)
    {
        var list = new List<MeshRenderer>();
        foreach (var kv in parts)
        {
            if (kv.Value.t.Count == 0) continue;
            var mesh = ProcMesh.Finish(name, kv.Value.v, kv.Value.t);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = kv.Key;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            list.Add(mr);
        }
        parts.Clear();
        return list;
    }

    // ------------------------------------------------------------------ vegetation (cheap, merged)

    public void DatePalm(Vector3 pos, System.Random rnd)
    {
        float h = 5.5f + (float)rnd.NextDouble() * 3f;
        var trunk = new Color(0.45f, 0.34f, 0.22f);
        var leaf = new Color(0.27f + (float)rnd.NextDouble() * 0.06f, 0.42f, 0.16f);
        Cylinder(pos, 0.22f, h, 5, Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 8f, 0f, 0f), trunk, false);
        Vector3 top = pos + Vector3.up * h;
        int fronds = 7;
        float yaw0 = (float)rnd.NextDouble() * 360f;
        for (int i = 0; i < fronds; i++)
        {
            var fr = Quaternion.Euler(0f, yaw0 + i * 360f / fronds, 0f) * Quaternion.Euler(24f, 0f, 0f);
            Box(top + fr * new Vector3(0f, 0f, 1.5f), new Vector3(0.55f, 0.06f, 3.1f), fr, leaf);
        }
    }

    public void Acacia(Vector3 pos, System.Random rnd)
    {
        float h = 2.6f + (float)rnd.NextDouble() * 1.4f, r = 2.4f + (float)rnd.NextDouble() * 1.4f;
        Cylinder(pos, 0.16f, h, 5, Quaternion.identity, new Color(0.38f, 0.29f, 0.2f), false);
        Cylinder(pos + Vector3.up * h, r, 0.7f, 7, Quaternion.identity, new Color(0.38f, 0.45f, 0.22f));
    }

    public void Eucalyptus(Vector3 pos, System.Random rnd)
    {
        float h = 7f + (float)rnd.NextDouble() * 4f;
        Cylinder(pos, 0.2f, h * 0.55f, 5, Quaternion.identity, new Color(0.62f, 0.58f, 0.5f), false);
        Cone(pos + Vector3.up * h * 0.35f, 1.9f, h * 0.75f, 6, new Color(0.3f, 0.42f, 0.28f), 0.6f);
    }

    public void Shrub(Vector3 pos, System.Random rnd, Color c)
    {
        Cone(pos, 0.7f + (float)rnd.NextDouble() * 0.6f, 0.6f + (float)rnd.NextDouble() * 0.5f, 5, c, 0.3f);
    }
}
