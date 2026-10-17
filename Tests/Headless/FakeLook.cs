// Fake UnityEngine members used by the graphics makeover (Assets/Scripts/Graphics/Look). Every member here mirrors a
// real Unity 6 / URP 17 API with the same name and signature (LODGroup, LOD, TerrainLayer, TerrainData alphamaps,
// Graphics.RenderMeshInstanced + RenderParams, GeometryUtility, ReflectionProbe, DynamicGI, Mesh.SetVertices...,
// ShadowsMidtonesHighlights, UniversalRenderPipelineAsset.shadowCascadeCount / colorGradingMode). Rendering is inert;
// Resources.Load also reads the real .mat files of Resources/Look (shader, textures with their imported size) so the
// tests check the shipped assets' wiring.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityEngine
{
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4(float x, float y, float z) : this(x, y, z, 0f) { }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public partial struct Color
    {
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color grey => new Color(0.5f, 0.5f, 0.5f);
    }
    public partial struct Bounds
    {
        public float SqrDistance(Vector3 p)
        {
            float dx = Math.Max(0f, Math.Max(min.x - p.x, p.x - max.x)), dy = Math.Max(0f, Math.Max(min.y - p.y, p.y - max.y)), dz = Math.Max(0f, Math.Max(min.z - p.z, p.z - max.z));
            return dx * dx + dy * dy + dz * dz;
        }
        public bool Intersects(Bounds b) => min.x <= b.max.x && max.x >= b.min.x && min.y <= b.max.y && max.y >= b.min.y && min.z <= b.max.z && max.z >= b.min.z;
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
    }
    public partial struct Quaternion
    {
        public static float Angle(Quaternion a, Quaternion b)
        {
            float d = Math.Min(Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w), 1f);
            return (float)(Math.Acos(d) * 2.0 * 180.0 / Math.PI);
        }
    }
    public static partial class Mathf
    {
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
    }
    public partial struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s)
        {
            Vector3 x = q * new Vector3(s.x, 0, 0), y = q * new Vector3(0, s.y, 0), z = q * new Vector3(0, 0, s.z);
            return new Matrix4x4 { m00 = x.x, m10 = x.y, m20 = x.z, m01 = y.x, m11 = y.y, m21 = y.z, m02 = z.x, m12 = z.y, m22 = z.z, m03 = pos.x, m13 = pos.y, m23 = pos.z, m33 = 1f };
        }
        public Vector4 GetColumn(int i) =>
            i == 0 ? new Vector4(m00, m10, m20, m30) : i == 1 ? new Vector4(m01, m11, m21, m31) : i == 2 ? new Vector4(m02, m12, m22, m32) : new Vector4(m03, m13, m23, m33);
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        public Vector3 MultiplyVector(Vector3 v) => new Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z);
    }

    // ------------------------------------------------------------------ meshes / materials / shaders
    public partial class Mesh
    {
        public Vector3[] normals = new Vector3[0];
        public Vector2[] uv = new Vector2[0];
        public Color[] colors = new Color[0];
        private readonly Dictionary<int, int> indexCounts = new Dictionary<int, int>();
        public void SetVertices(List<Vector3> v) => vertices = v.ToArray();
        public void SetNormals(List<Vector3> n) => normals = n.ToArray();
        public void SetUVs(int channel, List<Vector2> u) { if (channel == 0) uv = u.ToArray(); }
        public void SetColors(List<Color> c) => colors = c.ToArray();
        public void SetTriangles(List<int> t, int submesh) { indexCounts[submesh] = t.Count; triangles = triangles.Concat(t).ToArray(); }
        public uint GetIndexCount(int submesh) => indexCounts.TryGetValue(submesh, out var n) ? (uint)n : 0u;
        public void RecalculateTangents() { }
    }
    public partial class Material
    {
        public bool enableInstancing;
        internal readonly Dictionary<string, Texture> texs = new Dictionary<string, Texture>();
        public void SetTextureScale(string n, Vector2 s) { }
        public Texture GetTexture(string n) => texs.TryGetValue(n, out var t) ? t : (n == "_BaseMap" ? mainTexture : null);
        public Color GetColor(string n) => color;
        public IEnumerable<string> TextureProperties => texs.Keys;
    }
    public partial class Shader
    {
        public static readonly Dictionary<string, Vector4> Globals = new Dictionary<string, Vector4>();
        public static void SetGlobalVector(string n, Vector4 v) => Globals[n] = v;
    }
    public partial class Texture2D
    {
        public void SetPixels32(Color32[] c) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }
    public partial class Light { public float shadowStrength = 1f; }
    public partial class AnimationState { public float speed = 1f; }
    public partial class Animation
    {
        readonly Dictionary<string, AnimationState> states = new Dictionary<string, AnimationState>();
        public AnimationState this[string name] { get { if (!states.TryGetValue(name, out var s)) states[name] = s = new AnimationState { name = name }; return s; } }
    }

    // ------------------------------------------------------------------ LOD
    public enum LODFadeMode { None, CrossFade, SpeedTree }
    public struct LOD
    {
        public float screenRelativeTransitionHeight, fadeTransitionWidth;
        public Renderer[] renderers;
        public LOD(float screenRelativeTransitionHeight, Renderer[] renderers) { this.screenRelativeTransitionHeight = screenRelativeTransitionHeight; this.renderers = renderers; fadeTransitionWidth = 0f; }
    }
    public class LODGroup : Component
    {
        LOD[] lods = new LOD[0];
        public LODFadeMode fadeMode;
        public bool enabled = true;
        public int lodCount => lods.Length;
        public void SetLODs(LOD[] l) => lods = l;
        public LOD[] GetLODs() => lods;
        public void RecalculateBounds() { }
    }

    // ------------------------------------------------------------------ terrain
    public class TerrainLayer : Object
    {
        public Texture2D diffuseTexture, normalMapTexture, maskMapTexture;
        public Vector2 tileSize = new Vector2(15, 15), tileOffset;
        public float normalScale = 1f, metallic, smoothness;
        public Vector4 diffuseRemapMin = Vector4.zero, diffuseRemapMax = Vector4.one, maskMapRemapMin = Vector4.zero, maskMapRemapMax = Vector4.one;
    }
    public partial class Terrain
    {
        public static Terrain[] activeTerrains => FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        public Material materialTemplate;
        public float basemapDistance = 1000f;
    }
    public partial class TerrainData
    {
        public TerrainLayer[] terrainLayers = new TerrainLayer[0];
        public int alphamapResolution = 64;
        public int alphamapLayers => terrainLayers != null ? terrainLayers.Length : 0;
        public float[,,] alphamaps;
        public float GetSteepness(float x, float y) => x > 0.9f ? 45f : 0f;   // a steep strip along one edge (rock layer test)
        public void SetAlphamaps(int x, int y, float[,,] map)
        {
            int layers = alphamapLayers;
            if (alphamaps == null || alphamaps.GetLength(2) != layers) alphamaps = new float[alphamapResolution, alphamapResolution, layers];
            for (int j = 0; j < map.GetLength(0); j++) for (int i = 0; i < map.GetLength(1); i++) for (int l = 0; l < Math.Min(layers, map.GetLength(2)); l++) alphamaps[y + j, x + i, l] = map[j, i, l];
        }
        public float[,,] GetAlphamaps(int x, int y, int w, int h)
        {
            int layers = alphamapLayers;
            var a = new float[h, w, layers];
            if (alphamaps != null) for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) for (int l = 0; l < layers; l++) a[j, i, l] = alphamaps[y + j, x + i, l];
            return a;
        }
    }
    public partial class TerrainCollider { public TerrainData terrainData; }

    // ------------------------------------------------------------------ instanced drawing, culling, probes
    public struct RenderParams
    {
        public Material material;
        public int layer;
        public Bounds worldBounds;
        public Camera camera;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public RenderParams(Material mat) { material = mat; layer = 0; worldBounds = default; camera = null; shadowCastingMode = Rendering.ShadowCastingMode.On; receiveShadows = true; }
    }
    public static class Graphics
    {
        public static int InstancedCalls, InstancesDrawn;
        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] instanceData, int instanceCount = -1, int startInstance = 0) where T : struct   // real: unmanaged (mcs lacks it)
        {
            if (rparams.material == null) throw new ArgumentNullException("material");
            if (!rparams.material.enableInstancing) throw new InvalidOperationException("Material needs enableInstancing for RenderMeshInstanced");
            int n = instanceCount < 0 ? instanceData.Length - startInstance : instanceCount;
            if (n > 1023 || startInstance + n > instanceData.Length) throw new ArgumentOutOfRangeException("instanceCount");
            InstancedCalls++; InstancesDrawn += n;
        }
    }
    public struct Plane { public Vector3 normal; public float distance; }
    public static class GeometryUtility
    {
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes) { if (planes == null || planes.Length < 6) throw new ArgumentException("planes"); }
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds) => true;
    }
    public class ReflectionProbe : Behaviour
    {
        public Rendering.ReflectionProbeMode mode;
        public Rendering.ReflectionProbeRefreshMode refreshMode;
        public Rendering.ReflectionProbeTimeSlicingMode timeSlicingMode;
        public Vector3 size;
        public bool boxProjection, hdr;
        public float shadowDistance, intensity = 1f, farClipPlane = 1000f;
        public int importance = 1, resolution = 128, renders;
        public int RenderProbe() { renders++; return renders; }
    }
    public static class DynamicGI { public static int Updates; public static void UpdateEnvironment() { Updates++; } }

    // ------------------------------------------------------------------ Resources: look materials and textures from disk
    public static partial class Resources
    {
        static readonly Dictionary<string, Object> lookCache = new Dictionary<string, Object>();
        static Dictionary<string, string> guidToPath;

        static T LoadLook<T>(string path) where T : Object
        {
            string root = FakeEngine.ResourcesRoot;
            if (typeof(T) == typeof(Material) && File.Exists(Path.Combine(root, path + ".mat"))) return (T)(Object)LoadMaterial(Path.Combine(root, path + ".mat"));
            if (typeof(T) == typeof(Texture2D))
                foreach (var ext in new[] { ".png", ".jpg" })
                    if (File.Exists(Path.Combine(root, path + ext))) return (T)(Object)LoadTexture(Path.Combine(root, path + ext));
            return null;
        }

        static string AssetsRoot => Path.GetFullPath(Path.Combine(FakeEngine.ResourcesRoot, ".."));

        static string PathOfGuid(string guid)
        {
            if (guidToPath == null)
            {
                guidToPath = new Dictionary<string, string>();
                foreach (var meta in Directory.GetFiles(AssetsRoot, "*.meta", SearchOption.AllDirectories))
                {
                    if (!(meta.EndsWith(".shader.meta") || meta.EndsWith(".png.meta") || meta.EndsWith(".jpg.meta"))) continue;
                    var m = Regex.Match(File.ReadAllText(meta), @"guid: (\w+)");
                    if (m.Success) guidToPath[m.Groups[1].Value] = meta.Substring(0, meta.Length - 5);
                }
            }
            return guidToPath.TryGetValue(guid, out var p) ? p : null;
        }

        static readonly Dictionary<string, string> builtinShaders = new Dictionary<string, string>
        {
            { "933532a4fcc9baf4fa0491de14d08ed7", "Universal Render Pipeline/Lit" },
            { "69c1f799e772cb6438f56c23efccb782", "Universal Render Pipeline/Terrain/Lit" },
            { "0000000000000000f000000000000000:106", "Skybox/Procedural" },
        };

        /// <summary>Material from a .mat file: name, shader (by GUID), textures (by GUID, with their imported size).</summary>
        public static Material LoadMaterial(string file)
        {
            if (lookCache.TryGetValue(file, out var o)) return (Material)o;
            string t = File.ReadAllText(file);
            var sm = Regex.Match(t, @"m_Shader: \{fileID: (-?\d+), guid: (\w+)");
            string shaderName = "Hidden/InternalErrorShader";
            if (sm.Success)
            {
                string g = sm.Groups[2].Value;
                if (builtinShaders.TryGetValue(g, out var n) || builtinShaders.TryGetValue(g + ":" + sm.Groups[1].Value, out n)) shaderName = n;
                else
                {
                    var sp = PathOfGuid(g);
                    if (sp != null && sp.EndsWith(".shader")) { var nm = Regex.Match(File.ReadAllText(sp), "Shader \"([^\"]+)\""); if (nm.Success) shaderName = nm.Groups[1].Value; }
                }
            }
            var mat = new Material(new Shader { name = shaderName, isSupported = shaderName != "Hidden/InternalErrorShader" });
            mat.name = Regex.Match(t, @"m_Name: (\S+)").Groups[1].Value;
            mat.enableInstancing = t.Contains("m_EnableInstancingVariants: 1");
            foreach (Match m in Regex.Matches(t, @"- (\w+):\s*\n\s*m_Texture: \{fileID: 2800000, guid: (\w+)"))
            {
                var tp = PathOfGuid(m.Groups[2].Value);
                if (tp == null) continue;
                var tex = LoadTexture(tp);
                mat.texs[m.Groups[1].Value] = tex;
                if (m.Groups[1].Value == "_BaseMap") mat.mainTexture = tex;
            }
            lookCache[file] = mat;
            return mat;
        }

        /// <summary>Texture with the size Unity imports it at (file size capped by the .meta maxTextureSize).</summary>
        public static Texture2D LoadTexture(string file)
        {
            if (lookCache.TryGetValue(file, out var o)) return (Texture2D)o;
            var (w, h) = ImageSize(file);
            int max = 2048;
            if (File.Exists(file + ".meta")) { var m = Regex.Match(File.ReadAllText(file + ".meta"), @"maxTextureSize: (\d+)"); if (m.Success) max = int.Parse(m.Groups[1].Value); }
            while (w > max || h > max) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(file) };
            lookCache[file] = tex;
            return tex;
        }

        public static (int, int) ImageSize(string file)
        {
            var b = File.ReadAllBytes(file);
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P') return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
            for (int i = 2; i + 9 < b.Length;)
            {
                if (b[i] != 0xFF) { i++; continue; }
                int marker = b[i + 1];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }
            return (0, 0);
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum ReflectionProbeMode { Baked, Realtime, Custom }
    public enum ReflectionProbeRefreshMode { OnAwake, EveryFrame, ViaScripting }
    public enum ReflectionProbeTimeSlicingMode { AllFacesAtOnce, IndividualFaces, NoTimeSlicing }
}

namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;
    public enum ColorGradingMode { LowDynamicRange, HighDynamicRange }
    public class ShadowsMidtonesHighlights : VolumeComponent { public VolumeParameter<UnityEngine.Vector4> shadows, midtones, highlights; }
    public partial class UniversalRenderPipelineAsset { public int shadowCascadeCount = 4; public ColorGradingMode colorGradingMode; }
}
