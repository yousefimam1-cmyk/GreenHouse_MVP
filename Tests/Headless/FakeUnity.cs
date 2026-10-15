// A tiny fake of the UnityEngine API: enough to compile every game script and run its logic headlessly
// (component lifecycle, Update loop, coroutines, Invoke, transforms, Resources from disk, JsonUtility, PlayerPrefs).
// Rendering, physics and input are inert. Used only for automated logic tests outside the editor.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    // ------------------------------------------------------------------ engine core
    public static class FakeEngine
    {
        public static string ResourcesRoot;           // Assets/Resources
        public static string PersistentPath = Path.Combine(Path.GetTempPath(), "mazraa_test_" + System.Diagnostics.Process.GetCurrentProcess().Id);   // per process: parallel test runs do not share saves
        public static readonly List<GameObject> Objects = new List<GameObject>();
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Logs = new List<string>();
        internal static readonly List<(MonoBehaviour mb, IEnumerator it, float wakeAt)> Coroutines = new List<(MonoBehaviour, IEnumerator, float)>();
        internal static readonly List<(MonoBehaviour mb, string method, float at, float repeat)> Invokes = new List<(MonoBehaviour, string, float, float)>();

        public static void Reset()
        {
            Objects.Clear(); Errors.Clear(); Logs.Clear(); Coroutines.Clear(); Invokes.Clear();
            Time.time = 0; Time.frameCount = 0; Time.timeScale = 1; PlayerPrefs.Clear();
            RenderSettings.sun = null;
        }

        static object Call(object o, string name)
        {
            MethodInfo m = null;
            for (var ty = o.GetType(); ty != null && m == null; ty = ty.BaseType)
                m = ty.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (m == null) return null;
            try { return m.Invoke(o, null); }
            catch (TargetInvocationException e) { Errors.Add(o.GetType().Name + "." + name + ": " + e.InnerException); return null; }
        }

        /// <summary>While loading a scene, Awake is deferred until every object and component exists (like Unity).</summary>
        public static bool Loading;
        static readonly List<MonoBehaviour> pendingAwake = new List<MonoBehaviour>();
        public static void BeginScene() { Loading = true; pendingAwake.Clear(); }
        public static void EndScene() { Loading = false; foreach (var mb in pendingAwake.ToList()) { Call(mb, "Awake"); Call(mb, "OnEnable"); } pendingAwake.Clear(); }
        internal static void Awake(MonoBehaviour mb) { if (Loading) { pendingAwake.Add(mb); return; } Call(mb, "Awake"); Call(mb, "OnEnable"); }

        /// <summary>Advance one frame of dt seconds: Start (first time), coroutines, invokes, Update, FixedUpdate, LateUpdate.</summary>
        public static void Tick(float dt)
        {
            Time.frameCount++;
            Time.unscaledDeltaTime = dt;
            Time.deltaTime = dt * Time.timeScale;
            Time.fixedDeltaTime = Time.deltaTime;
            Time.time += Time.deltaTime;
            Time.unscaledTime += dt;
            var all = AllBehaviours().ToList();
            foreach (var mb in all)
            {
                if (mb.started || mb.destroyed) continue;
                mb.started = true;
                var r = Call(mb, "Start");
                if (r is IEnumerator it) Coroutines.Add((mb, it, Time.time));
            }
            for (int i = 0; i < Coroutines.Count; i++)
            {
                var (mb, it, wake) = Coroutines[i];
                if (mb.destroyed) { Coroutines.RemoveAt(i--); continue; }
                if (Time.time < wake) continue;
                bool more;
                try { more = it.MoveNext(); } catch (Exception e) { Errors.Add("coroutine: " + e); more = false; }
                if (!more) { Coroutines.RemoveAt(i--); continue; }
                float next = it.Current is WaitForSeconds w ? Time.time + w.seconds : Time.time;
                Coroutines[i] = (mb, it, next);
            }
            for (int i = 0; i < Invokes.Count; i++)
            {
                var (mb, method, at, rep) = Invokes[i];
                if (mb.destroyed) { Invokes.RemoveAt(i--); continue; }
                if (Time.time < at) continue;
                if (rep > 0) Invokes[i] = (mb, method, at + rep, rep); else Invokes.RemoveAt(i--);
                Call(mb, method);
            }
            foreach (var mb in AllBehaviours().ToList()) if (mb.started && !mb.destroyed && mb.enabled && mb.gameObject.activeInHierarchy) Call(mb, "Update");
            foreach (var mb in AllBehaviours().ToList()) if (mb.started && !mb.destroyed && mb.enabled && mb.gameObject.activeInHierarchy) Call(mb, "FixedUpdate");
            foreach (var rb in Objects.Where(g => !g.destroyed).SelectMany(g => g.components).OfType<Rigidbody>().ToList()) rb.Integrate(Time.fixedDeltaTime);
            foreach (var mb in AllBehaviours().ToList()) if (mb.started && !mb.destroyed && mb.enabled && mb.gameObject.activeInHierarchy) Call(mb, "LateUpdate");
        }

        public static void Run(float seconds, float dt = 0.1f) { for (float t = 0; t < seconds; t += dt) Tick(dt); }

        public static IEnumerable<MonoBehaviour> AllBehaviours() =>
            Objects.Where(g => !g.destroyed).SelectMany(g => g.components.OfType<MonoBehaviour>()).Where(c => !c.destroyed);
    }

    // ------------------------------------------------------------------ objects
    public class Object
    {
        internal bool destroyed;
        public string name = "";
        public HideFlags hideFlags;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed, bn = ReferenceEquals(b, null) || b.destroyed;
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => o is Object ob ? this == ob : (o == null && destroyed);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static implicit operator bool(Object o) => !(ReferenceEquals(o, null)) && !o.destroyed;

        public static void Destroy(Object o)
        {
            if (ReferenceEquals(o, null)) return;
            if (o is GameObject g) { foreach (var c in g.GetComponentsInChildren<Component>(true)) c.destroyed = true; foreach (var t in g.transform.AllChildren()) t.gameObject.destroyed = true; g.destroyed = true; g.transform.SetParent(null); }
            else o.destroyed = true;
        }
        public static void Destroy(Object o, float t) => Destroy(o);
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T original) where T : Object
        {
            if (original is ScriptableObject) return (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(original, null);
            return original;
        }
        public static T Instantiate<T>(T original, Transform parent, bool worldStays) where T : Object
        {
            if (original is GameObject g)
            {
                var c = new GameObject(g.name); c.transform.SetParent(parent, worldStays);
                var mesh = new GameObject("mesh"); mesh.transform.SetParent(c.transform, false); mesh.AddComponent<MeshRenderer>(); mesh.AddComponent<Animation>();
                return c as T;
            }
            return original;
        }
        public static T FindAnyObjectByType<T>() where T : Object => FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault();
        public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : Object => FindObjectsByType<T>(FindObjectsInactive.Exclude, m);
        public static T[] FindObjectsByType<T>(FindObjectsInactive inc, FindObjectsSortMode m) where T : Object
        {
            if (typeof(GameObject).IsAssignableFrom(typeof(T))) return FakeEngine.Objects.Where(g => !g.destroyed).Cast<T>().ToArray();
            return FakeEngine.Objects.Where(g => !g.destroyed && (inc == FindObjectsInactive.Include || g.activeInHierarchy))
                .SelectMany(g => g.components).Where(c => !c.destroyed).OfType<T>().ToArray();
        }
    }
    public enum HideFlags { None, HideAndDontSave = 61 }
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum Space { World, Self }

    public class GameObject : Object
    {
        internal readonly List<Component> components = new List<Component>();
        public Transform transform { get; }
        public int layer;
        public string tag = "Untagged";
        bool active = true;
        public bool activeSelf => active;
        public bool activeInHierarchy => active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject gameObject => this;

        public GameObject() : this("GameObject") { }
        public GameObject(string n, params Type[] comps)
        {
            name = n;
            transform = Add(typeof(RectTransform).IsAssignableFrom(comps.FirstOrDefault() ?? typeof(int)) ? typeof(RectTransform) : typeof(Transform)) as Transform;
            FakeEngine.Objects.Add(this);
            foreach (var c in comps) if (!typeof(Transform).IsAssignableFrom(c)) AddComponent(c);
        }
        Component Add(Type t)
        {
            var c = (Component)Activator.CreateInstance(t, true);
            c.gameObject = this; c.name = name;
            components.Add(c);
            return c;
        }
        public Component AddComponent(Type t)
        {
            var c = Add(t);
            if (c is ParticleSystem) Add(typeof(ParticleSystemRenderer));
            if (c is MonoBehaviour mb) FakeEngine.Awake(mb);
            return c;
        }
        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
        /// <summary>Like a scene object: set serialized fields first, then Awake runs.</summary>
        public T AddConfigured<T>(Action<T> init) where T : Component
        {
            var c = (T)Add(typeof(T));
            init(c);
            if (c is MonoBehaviour mb) FakeEngine.Awake(mb);
            return c;
        }
        public T GetComponent<T>() => components.Where(c => !c.destroyed).OfType<T>().FirstOrDefault();
        public Component GetComponent(Type t) => components.Where(c => !c.destroyed).FirstOrDefault(c => t.IsInstanceOfType(c));
        public T[] GetComponentsInChildren<T>(bool inc = false) => transform.GetComponentsInChildren<T>(inc);
        public T GetComponentInChildren<T>() => transform.GetComponentsInChildren<T>(false).FirstOrDefault();
        public void SetActive(bool v) => active = v;
        public static GameObject CreatePrimitive(PrimitiveType p)
        {
            var g = new GameObject(p.ToString());
            g.AddComponent<MeshFilter>(); g.AddComponent<MeshRenderer>(); g.AddComponent<BoxCollider>();
            return g;
        }
        public static GameObject Find(string n) => FakeEngine.Objects.FirstOrDefault(g => !g.destroyed && g.name == n);
        public static GameObject FindGameObjectWithTag(string t) => FakeEngine.Objects.FirstOrDefault(g => !g.destroyed && g.tag == t);
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public string tag { get => gameObject.tag; set => gameObject.tag = value; }
        public T GetComponent<T>() => gameObject.GetComponent<T>();
        public void SendMessage(string m, object v, SendMessageOptions o) { foreach (var c in gameObject.components.OfType<MonoBehaviour>().ToList()) { var mi = c.GetType().GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (mi != null && mi.GetParameters().Length == 1) mi.Invoke(c, new[] { v }); } }
        public Component GetComponent(Type t) => gameObject.GetComponent(t);
        public bool TryGetComponent<T>(out T c) { c = GetComponent<T>(); return c != null; }
        public T GetComponentInParent<T>()
        {
            for (var t = transform; t != null; t = t.parent) { var c = t.gameObject.GetComponent<T>(); if (c != null) return c; }
            return default;
        }
        public T GetComponentInChildren<T>() => GetComponentsInChildren<T>(false).FirstOrDefault();
        public T[] GetComponentsInChildren<T>() => GetComponentsInChildren<T>(false);
        public T[] GetComponentsInChildren<T>(bool inc) => transform.GetComponentsInChildren<T>(inc);
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour
    {
        internal bool started;
        public void Invoke(string m, float t) => FakeEngine.Invokes.Add((this, m, Time.time + t, 0));
        public void InvokeRepeating(string m, float t, float r) => FakeEngine.Invokes.Add((this, m, Time.time + t, Math.Max(0.0001f, r)));
        public void CancelInvoke(string m) => FakeEngine.Invokes.RemoveAll(x => x.mb == this && x.method == m);
        public void CancelInvoke() => FakeEngine.Invokes.RemoveAll(x => x.mb == this);
        public Coroutine StartCoroutine(IEnumerator e) { FakeEngine.Coroutines.Add((this, e, Time.time)); return new Coroutine(); }
    }
    public class Coroutine { }
    public class WaitForSeconds { public float seconds; public WaitForSeconds(float s) { seconds = s; } }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => Activator.CreateInstance<T>();
        public static ScriptableObject CreateInstance(Type t) => (ScriptableObject)Activator.CreateInstance(t);
    }
    public static class ObjectCloner
    {
        public static T Clone<T>(T o) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);
    }

    // ------------------------------------------------------------------ transforms
    public class Transform : Component, IEnumerable
    {
        Transform _parent;
        readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Transform parent { get => _parent; set => SetParent(value); }
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        public IEnumerator GetEnumerator() => children.GetEnumerator();
        internal IEnumerable<Transform> AllChildren() { foreach (var c in children.ToList()) { yield return c; foreach (var d in c.AllChildren()) yield return d; } }
        public void SetParent(Transform p) => SetParent(p, true);
        public void SetAsLastSibling() { if (_parent != null) { _parent.children.Remove(this); _parent.children.Add(this); } }
        public void SetParent(Transform p, bool worldStays)
        {
            Vector3 wp = position; Quaternion wr = rotation;
            if (_parent != null) _parent.children.Remove(this);
            _parent = p;
            if (p != null) p.children.Add(this);
            if (worldStays) { position = wp; rotation = wr; }
        }
        public Quaternion rotation
        {
            get => _parent == null ? localRotation : _parent.rotation * localRotation;
            set => localRotation = _parent == null ? value : Quaternion.Inverse(_parent.rotation) * value;
        }
        public Vector3 position
        {
            get => _parent == null ? localPosition : _parent.TransformPoint(localPosition);
            set => localPosition = _parent == null ? value : _parent.InverseTransformPoint(value);
        }
        public Vector3 eulerAngles { get => rotation.eulerAngles; set => rotation = Quaternion.Euler(value); }
        public Vector3 localEulerAngles { get => localRotation.eulerAngles; set => localRotation = Quaternion.Euler(value); }
        public Vector3 forward { get => rotation * Vector3.forward; set => rotation = Quaternion.LookRotation(value); }
        public Vector3 right => rotation * Vector3.right;
        public Vector3 up => rotation * Vector3.up;
        public Vector3 lossyScale => _parent == null ? localScale : Vector3.Scale(_parent.lossyScale, localScale);
        public Vector3 TransformPoint(Vector3 p) => position + rotation * Vector3.Scale(lossyScale, p);
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            var s = lossyScale; var l = Quaternion.Inverse(rotation) * (p - position);
            return new Vector3(s.x != 0 ? l.x / s.x : 0, s.y != 0 ? l.y / s.y : 0, s.z != 0 ? l.z / s.z : 0);
        }
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public void Rotate(Vector3 axis, float deg, Space s = Space.Self) { localRotation = localRotation * Quaternion.AngleAxis(deg, axis); }
        public void Rotate(Vector3 euler) { localRotation = localRotation * Quaternion.Euler(euler); }
        public void LookAt(Vector3 t) { var d = t - position; if (d.sqrMagnitude > 0) rotation = Quaternion.LookRotation(d); }
        public void LookAt(Transform t) => LookAt(t.position);
        public Matrix4x4 localToWorldMatrix => Matrix4x4.identity; public Matrix4x4 worldToLocalMatrix => Matrix4x4.identity;
        public bool IsChildOf(Transform p) { for (var t = this; t != null; t = t._parent) if (t == p) return true; return false; }
        public Transform Find(string n) => children.FirstOrDefault(c => c.name == n || c.gameObject.name == n);
        internal T[] GetComponentsInChildren<T>(bool inc)
        {
            var list = new List<T>();
            if (inc || gameObject.activeInHierarchy) list.AddRange(gameObject.components.Where(c => !c.destroyed).OfType<T>());
            foreach (var c in children) if (inc || c.gameObject.activeInHierarchy) list.AddRange(c.GetComponentsInChildren<T>(inc));
            return list.ToArray();
        }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax;
    }

    // ------------------------------------------------------------------ math
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-6f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => a * k;
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { var nn = n.normalized; return v - nn * Dot(v, nn); }
        public static float Angle(Vector3 a, Vector3 b) { float d = Mathf.Clamp(Dot(a.normalized, b.normalized), -1f, 1f); return (float)(Math.Acos(d) * 180 / Math.PI); }
        public static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) { float ang = Angle(a, b); return Dot(axis, Cross(a, b)) < 0 ? -ang : ang; }
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            var a = axis.normalized; double h = deg * Math.PI / 360; float s = (float)Math.Sin(h);
            return new Quaternion(a.x * s, a.y * s, a.z * s, (float)Math.Cos(h));
        }
        // Unity order: Z, then X, then Y
        public static Quaternion Euler(float ex, float ey, float ez) => AngleAxis(ey, Vector3.up) * AngleAxis(ex, Vector3.right) * AngleAxis(ez, Vector3.forward);
        public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z); float s = q.w;
            return u * (2f * Vector3.Dot(u, v)) + v * (s * s - Vector3.Dot(u, u)) + Vector3.Cross(u, v) * (2f * s);
        }
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static Quaternion LookRotation(Vector3 fwd) => LookRotation(fwd, Vector3.up);
        public static Quaternion LookRotation(Vector3 fwd, Vector3 up)
        {
            fwd = fwd.normalized; if (fwd.sqrMagnitude < 1e-8f) return identity;
            float yaw = (float)(Math.Atan2(fwd.x, fwd.z) * 180 / Math.PI);
            float pitch = (float)(-Math.Asin(Mathf.Clamp(fwd.y, -1, 1)) * 180 / Math.PI);
            return Euler(pitch, yaw, 0);
        }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t); float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; if (d < 0) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); }
            var r = new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
            float m = (float)Math.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w); return new Quaternion(r.x / m, r.y / m, r.z / m, r.w / m);
        }
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float maxDeg) => Slerp(a, b, Mathf.Clamp01(maxDeg / 180f * 4f));
        public Vector3 eulerAngles
        {
            get
            {
                var f = this * Vector3.forward;
                float yaw = (float)(Math.Atan2(f.x, f.z) * 180 / Math.PI); if (yaw < 0) yaw += 360;
                float pitch = (float)(-Math.Asin(Mathf.Clamp(f.y, -1, 1)) * 180 / Math.PI); if (pitch < 0) pitch += 360;
                return new Vector3(pitch, yaw, 0);
            }
        }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1); public static Color black => new Color(0, 0, 0);
        public static Color green => new Color(0, 1, 0); public static Color red => new Color(1, 0, 0); public static Color yellow => new Color(1, 0.92f, 0.016f);
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);
        public static Color Lerp(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
    }
    public static class ColorUtility
    {
        static string H(float v) => ((int)Math.Round(Mathf.Clamp01(v) * 255)).ToString("X2");
        public static string ToHtmlStringRGB(Color c) => H(c.r) + H(c.g) + H(c.b);
        public static string ToHtmlStringRGBA(Color c) => H(c.r) + H(c.g) + H(c.b) + H(c.a);
    }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
        public Vector3 min => center - size * 0.5f; public Vector3 max => center + size * 0.5f;
        public void Encapsulate(Vector3 p)
        {
            var mn = new Vector3(Math.Min(min.x, p.x), Math.Min(min.y, p.y), Math.Min(min.z, p.z));
            var mx = new Vector3(Math.Max(max.x, p.x), Math.Max(max.y, p.y), Math.Max(max.z, p.z));
            center = (mn + mx) * 0.5f; size = mx - mn;
        }
        public void Encapsulate(Bounds b) { Encapsulate(b.min); Encapsulate(b.max); }
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI, Rad2Deg = 57.29578f, Deg2Rad = 0.0174533f;
        public static float Abs(float v) => Math.Abs(v); public static int Abs(int v) => Math.Abs(v);
        public static float Repeat(float t, float length) => Math.Max(0f, Math.Min(t - (float)Math.Floor(t / length) * length, length));
        public static float Max(float a, float b) => Math.Max(a, b); public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(params float[] v) => v.Max();
        public static float Min(float a, float b) => Math.Min(a, b); public static int Min(int a, int b) => Math.Min(a, b);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpAngle(float a, float b, float t) { float d = ((b - a) % 360 + 540) % 360 - 180; return a + d * Clamp01(t); }
        public static float MoveTowards(float c, float t, float m) => Math.Abs(t - c) <= m ? t : c + Math.Sign(t - c) * m;
        public static float Sign(float v) => v >= 0 ? 1 : -1;
        public static float Sin(float v) => (float)Math.Sin(v); public static float Cos(float v) => (float)Math.Cos(v);
        public static float Exp(float v) => (float)Math.Exp(v); public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.ToEven);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static int FloorToInt(float v) => (int)Math.Floor(v); public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Clamp01((v - a) / (b - a));
        public static float PerlinNoise(float x, float y) => 0.5f + 0.25f * (float)(Math.Sin(x * 1.7 + y * 0.3) + Math.Sin(y * 1.3 - x * 0.2)) * 0.5f;
    }
    public static class Random
    {
        public static System.Random R = new System.Random(1234);
        public static float value => (float)R.NextDouble();
        public static Vector2 insideUnitCircle { get { double a = R.NextDouble() * Math.PI * 2, r = Math.Sqrt(R.NextDouble()); return new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)); } }
        public static float Range(float a, float b) => a + (float)R.NextDouble() * (b - a);
        public static int Range(int a, int b) => b <= a ? a : R.Next(a, b);
    }
    public static class Time
    {
        public static float deltaTime, unscaledDeltaTime, fixedDeltaTime = 0.02f, time, unscaledTime, timeScale = 1;
        public static int frameCount;
    }
    public static class Debug
    {
        public static void Log(object o) => FakeEngine.Logs.Add("" + o);
        public static void LogWarning(object o) => FakeEngine.Logs.Add("WARN " + o);
        public static void LogException(Exception e) => FakeEngine.Errors.Add("EXC " + e);
        public static void LogError(object o) => FakeEngine.Errors.Add("" + o);
    }
    public enum RuntimePlatform { WindowsPlayer, WebGLPlayer, LinuxEditor }
    public static class Application
    {
        public static string persistentDataPath => FakeEngine.PersistentPath;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static bool isEditor => true;
        public static void Quit() { }
        public static bool isMobilePlatform => false;
    }
    public static class PlayerPrefs
    {
        static readonly Dictionary<string, object> d = new Dictionary<string, object>();
        public static void Clear() => d.Clear();
        public static int GetInt(string k, int def = 0) => d.TryGetValue(k, out var v) && v is int i ? i : def;
        public static float GetFloat(string k, float def = 0) => d.TryGetValue(k, out var v) && v is float f ? f : def;
        public static string GetString(string k, string def = "") => d.TryGetValue(k, out var v) && v is string s ? s : def;
        public static void SetInt(string k, int v) => d[k] = v; public static void SetFloat(string k, float v) => d[k] = v; public static void SetString(string k, string v) => d[k] = v;
        public static bool HasKey(string k) => d.ContainsKey(k); public static void DeleteKey(string k) => d.Remove(k); public static void Save() { }
    }
    public static class JsonUtility
    {
        static readonly Newtonsoft.Json.JsonSerializerSettings S = new Newtonsoft.Json.JsonSerializerSettings { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace };
        public static T FromJson<T>(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, S);
        public static string ToJson(object o, bool pretty = false) => Newtonsoft.Json.JsonConvert.SerializeObject(o, pretty ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None);
    }
    public static class Resources
    {
        public static T[] LoadAll<T>(string path) where T : Object
        {
            var dir = Path.Combine(FakeEngine.ResourcesRoot, path);
            if (typeof(T) != typeof(TextAsset) || !Directory.Exists(dir)) return new T[0];
            return Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => (T)(Object)new TextAsset { text = File.ReadAllText(f), name = Path.GetFileNameWithoutExtension(f) }).ToArray();
        }
        public static T Load<T>(string path) where T : Object
        {
            if (typeof(T) == typeof(TextAsset))
            {
                var f = Path.Combine(FakeEngine.ResourcesRoot, path + ".json");
                if (!File.Exists(f)) f = Path.Combine(FakeEngine.ResourcesRoot, path + ".txt");
                return File.Exists(f) ? new TextAsset { text = File.ReadAllText(f), name = path } as T : null;
            }
            if (typeof(T) == typeof(GameObject) && path.StartsWith("Models/") && File.Exists(Path.Combine(FakeEngine.ResourcesRoot, path + ".fbx")))
            {   // stand-in for an imported model: an inactive template the game instantiates
                var g = new GameObject(path); g.SetActive(false); FakeEngine.Objects.Remove(g);
                return g as T;
            }
            return null; // fonts etc.: placeholders are used
        }
    }
    public class TextAsset : Object { public string text; }
    public class Font : Object { }
    public class Shader : Object { public static Shader Find(string n) => new Shader { name = n }; }
    public enum MaterialGlobalIlluminationFlags { None }
    public class Material : Object
    {
        public Material(Shader s) { } public Material(Material m) { }
        public Color color; public MaterialGlobalIlluminationFlags globalIlluminationFlags; public int renderQueue; public Texture mainTexture; public void SetTexture(string n, Texture t) { mainTexture = t; }
        public void SetColor(string n, Color c) { } public void SetFloat(string n, float v) { } public float GetFloat(string n) => 1; public bool HasProperty(string n) => true; public void EnableKeyword(string k) { }
    }
    public class Mesh : Object { public int subMeshCount = 1; public Bounds bounds; public int vertexCount => vertices.Length; public void CombineMeshes(CombineInstance[] c, bool merge, bool useMatrices) { } public Vector3[] vertices = new Vector3[0]; public int[] triangles = new int[0]; public Rendering.IndexFormat indexFormat; public void RecalculateNormals() { } public void RecalculateBounds() { } }
    public class MeshCollider : Collider { public Mesh sharedMesh; }
    public struct CombineInstance { public Mesh mesh; public Matrix4x4 transform; public int subMeshIndex; }
    public struct Matrix4x4 { public static Matrix4x4 identity => new Matrix4x4(); public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a; }
    public class TerrainCollider : Collider { }
    public static class StaticBatchingUtility { public static void Combine(GameObject root) { } }
    public class MeshFilter : Component { public Mesh mesh, sharedMesh = new Mesh(); }
    public class Renderer : Component
    {
        public bool enabled = true; public Material material, sharedMaterial; public Material[] materials, sharedMaterials;
        public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows;
        public Bounds bounds => new Bounds(transform.position, transform.lossyScale);
    }
    public class MeshRenderer : Renderer { }
    public class SkinnedMeshRenderer : Renderer { }
    public class Collider : Component { public bool enabled = true; public PhysicsMaterial material; public Bounds bounds => new Bounds(transform.position, Vector3.Scale(transform.lossyScale, this is BoxCollider b ? b.size : Vector3.one)); }
    public class BoxCollider : Collider { public Vector3 center, size; }
    public class CharacterController : Collider { }
    public class PhysicsMaterial : Object { public PhysicsMaterial(string n) { name = n; } public float dynamicFriction, staticFriction; public PhysicsMaterialCombine frictionCombine; }
    public enum PhysicsMaterialCombine { Average, Minimum }
    public enum RigidbodyInterpolation { None, Interpolate }
    public enum CollisionDetectionMode { Discrete, ContinuousDynamic }
    public class Rigidbody : Component
    {
        public float mass; public RigidbodyInterpolation interpolation; public CollisionDetectionMode collisionDetectionMode; public bool freezeRotation, useGravity = true, isKinematic;
        public Vector3 linearVelocity;
        public Vector3 position { get => transform.position; set => transform.position = value; }
        public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }
        public void MoveRotation(Quaternion q) => transform.rotation = q;
        internal void Integrate(float dt) { transform.position = transform.position + linearVelocity * dt; }
    }
    public struct ContactPoint { public Vector3 normal; }
    public class Collision { public int contactCount; public ContactPoint GetContact(int i) => default; }
    public struct RaycastHit { public Vector3 point, normal; public float distance; public Collider collider; }
    public struct Ray { public Vector3 origin, direction; }
    public struct LayerMask { public int value; public static implicit operator int(LayerMask m) => m.value; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static bool Raycast(Vector3 o, Vector3 d, out RaycastHit h, float dist, int mask, QueryTriggerInteraction q)
        {   // flat ground at y = 0
            h = default;
            if (d.y >= 0 || o.y < 0) return false;
            float t = o.y / -d.y; if (t > dist) return false;
            h.point = o + d * t; h.normal = Vector3.up; h.distance = t; return true;
        }
        public static bool Raycast(Ray r, out RaycastHit h, float dist, int mask) { h = default; return false; }
        public static void IgnoreCollision(Collider a, Collider b, bool i) { }
    }
    public class Terrain : Component { public static Terrain activeTerrain; public TerrainData terrainData; public float SampleHeight(Vector3 p) => 0; }
    public class TerrainData : Object { public Vector3 size; }
    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Soft }
    public class Light : Behaviour { public LightType type; public float intensity = 1, range, spotAngle; public Color color; public LightShadows shadows; }
    public static class RenderSettings { public static Light sun; public static float ambientIntensity = 1; public static Color ambientLight; public static Material skybox; public static bool fog; public static float fogDensity = 0.01f; public static Color fogColor; public static FogMode fogMode = FogMode.Exponential; }
    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }
    public class Camera : Behaviour
    {
        public static Camera main => FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.tag == "MainCamera");
        public static Camera[] allCameras => FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled).ToArray();
        public float fieldOfView, nearClipPlane, farClipPlane = 1000;
        public Ray ViewportPointToRay(Vector3 v) => new Ray { origin = transform.position, direction = transform.forward };
        public bool orthographic; public float orthographicSize = 5, depth; public RenderTexture targetTexture; public int cullingMask = -1; public CameraClearFlags clearFlags; public Color backgroundColor;
        public Vector3 WorldToScreenPoint(Vector3 p) => new Vector3(0, 0, 1);
    }
    public class AudioListener : Behaviour { public static float volume = 1f; }
    public class AudioClip : Object { public int samples, channels, frequency; public float[] data; public float length => frequency > 0 ? samples / (float)frequency : 0; public static AudioClip Create(string n, int len, int ch, int freq, bool stream) => new AudioClip { name = n, samples = len, channels = ch, frequency = freq }; public bool SetData(float[] d, int off) { data = d; return true; } }
    public class AudioSource : Behaviour { public bool isPlaying, loop, playOnAwake = true; public AudioClip clip; public float volume = 1, pitch = 1, spatialBlend, dopplerLevel = 1, minDistance = 1, maxDistance = 500; public AudioRolloffMode rolloffMode; public int oneShots; public void Play() => isPlaying = true; public void Stop() => isPlaying = false; public void PlayOneShot(AudioClip c) { oneShots++; } public void PlayOneShot(AudioClip c, float v) { oneShots++; } }
    public enum AudioRolloffMode { Logarithmic, Linear, Custom }
    public enum WrapMode { Once, Loop }
    public class AnimationState { public string name; public WrapMode wrapMode; }
    public class Animation : Behaviour, IEnumerable { public IEnumerator GetEnumerator() { yield break; } public void CrossFade(string n, float t) { } }
    public enum CursorLockMode { None, Locked }
    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
    public enum RenderMode { ScreenSpaceOverlay }
    public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; }
    public class CanvasGroup : Component { public float alpha; }
    public class RectOffset { public RectOffset(int a, int b, int c, int d) { } }
    public enum ParticleSystemStopBehavior { StopEmittingAndClear }
    public enum ParticleSystemSimulationSpace { Local, World }
    public enum ParticleSystemShapeType { Box }
    public enum ParticleSystemRenderMode { Billboard, Stretch }
    public class ParticleSystem : Component
    {
        public bool isPlaying; public void Play() => isPlaying = true; public void Stop() => isPlaying = false; public void Stop(bool c, ParticleSystemStopBehavior b) => isPlaying = false;
        public MainModule main => new MainModule(); public EmissionModule emission => new EmissionModule(); public ShapeModule shape => new ShapeModule();
        public struct MainModule { public float startLifetime { set { } } public float startSpeed { set { } } public float startSize { set { } } public int maxParticles { set { } } public Color startColor { set { } } public ParticleSystemSimulationSpace simulationSpace { set { } } public float gravityModifier { set { } } }
        public struct EmissionModule { public float rateOverTime { set { } } }
        public struct ShapeModule { public ParticleSystemShapeType shapeType { set { } } public Vector3 scale { set { } } }
    }
    public class ParticleSystemRenderer : Renderer { public ParticleSystemRenderMode renderMode; public float lengthScale; }

    public enum TextureWrapMode { Repeat, Clamp }
    public class Sprite : Object { public Texture2D texture; public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) => new Sprite { texture = t }; }

    // attributes
    public class SerializeField : Attribute { }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class SpaceAttribute : Attribute { }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class ContextMenu : Attribute { public ContextMenu(string s) { } }
    public class CreateAssetMenuAttribute : Attribute { public string menuName; }
    public class RequireComponent : Attribute { public RequireComponent(Type t) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
}
namespace UnityEngine.Serialization { public class FormerlySerializedAsAttribute : System.Attribute { public FormerlySerializedAsAttribute(string s) { } } }
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On }
    public enum IndexFormat { UInt16, UInt32 }
    public enum BlendMode { Zero, One, DstColor, SrcColor, OneMinusDstColor, SrcAlpha, OneMinusSrcColor, DstAlpha, OneMinusDstAlpha, SrcAlphaSaturate, OneMinusSrcAlpha }
    public class RenderPipeline { }
    public abstract class RenderPipelineAsset : UnityEngine.ScriptableObject { public abstract RenderPipeline CreatePipeline(); }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; }
}
namespace UnityEngine.Events { public class UnityEvent { readonly System.Collections.Generic.List<System.Action> l = new System.Collections.Generic.List<System.Action>(); public void AddListener(System.Action a) => l.Add(a); public void Invoke() { foreach (var a in l) a(); } } }
// IMGUI (used by the co-op menu; OnGUI is never called headless)
namespace UnityEngine
{
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    public static class Screen { public static int width = 1920, height = 1080; }
    public enum TextAnchor { UpperLeft, MiddleLeft, MiddleCenter, MiddleRight }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { if (other != null) { font = other.font; fontSize = other.fontSize; alignment = other.alignment; wordWrap = other.wordWrap; } }
        public Font font; public int fontSize; public TextAnchor alignment; public bool wordWrap;
        public GUIStyleState normal = new GUIStyleState();
    }
    public class GUISkin { public GUIStyle label = new GUIStyle(), button = new GUIStyle(), box = new GUIStyle(), textField = new GUIStyle(); }
    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
        public static void Box(Rect r, string t, GUIStyle s) { }
        public static void Label(Rect r, string t, GUIStyle s) { }
        public static bool Button(Rect r, string t, GUIStyle s) => false;
        public static string TextField(Rect r, string t, int max, GUIStyle s) => t;
        public static void SetNextControlName(string n) { }
        public static void FocusControl(string n) { }
    }
}
