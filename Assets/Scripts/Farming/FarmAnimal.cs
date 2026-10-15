using System.Collections.Generic;
using UnityEngine;

/// <summary>Simple wandering animal with a procedural low-poly body (or Resources/Models/&lt;id&gt; if present).</summary>
public class FarmAnimal : MonoBehaviour
{
    public string animalId;
    public AnimalPen pen;
    private Vector3 target;
    private float wait;
    private float speed;
    private Transform head;
    private readonly List<Transform> legs = new List<Transform>();
    private float walkPhase;

    public static FarmAnimal Create(string id, AnimalPen pen, Vector3 pos)
    {
        var root = new GameObject("Animal " + id);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        var a = root.AddComponent<FarmAnimal>();
        a.animalId = id; a.pen = pen;
        float fit = id == "cow" ? 2.2f : id == "sheep" ? 1.3f : 0.55f;
        var model = Build.TryModel(id, root.transform, fit, 0f);
        if (model == null) a.BuildBody();
        else a.SetupAnimation(model);
        a.speed = id == "chicken" ? 1.1f : id == "sheep" ? 0.8f : 0.6f;
        a.PickTarget();
        return a;
    }

    private void BuildBody()
    {
        Transform t = transform;
        if (animalId == "cow")
        {
            var white = Build.Mat(new Color(0.93f, 0.92f, 0.88f)); var black = Build.Mat(new Color(0.12f, 0.11f, 0.1f));
            var pink = Build.Mat(new Color(0.9f, 0.62f, 0.6f)); var horn = Build.Mat(new Color(0.85f, 0.8f, 0.65f));
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.05f, 0), new Vector3(0.9f, 0.8f, 1.8f), white, "Body");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0.46f, 1.15f, 0.2f), new Vector3(0.02f, 0.45f, 0.6f), black);
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.46f, 1.0f, -0.4f), new Vector3(0.02f, 0.5f, 0.5f), black);
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.46f, -0.3f), new Vector3(0.6f, 0.02f, 0.5f), black);
            head = Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.35f, 1.05f), new Vector3(0.5f, 0.5f, 0.55f), white, "Head").transform;
            Build.Part(PrimitiveType.Cube, head, new Vector3(0, -0.25f, 0.45f), new Vector3(0.9f, 0.5f, 0.35f), pink);
            Build.Part(PrimitiveType.Cylinder, head, new Vector3(0.4f, 0.55f, 0), new Vector3(0.12f, 0.2f, 0.12f), horn, null, new Vector3(0, 0, -35));
            Build.Part(PrimitiveType.Cylinder, head, new Vector3(-0.4f, 0.55f, 0), new Vector3(0.12f, 0.2f, 0.12f), horn, null, new Vector3(0, 0, 35));
            Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.62f, -0.45f), new Vector3(0.4f, 0.25f, 0.35f), pink, "Udder");
            Legs(white, black, 0.65f, 0.32f, 0.7f, 0.16f);
        }
        else if (animalId == "sheep")
        {
            var wool = Build.Mat(new Color(0.95f, 0.94f, 0.9f), 0.05f); var dark = Build.Mat(new Color(0.16f, 0.14f, 0.13f));
            for (int i = 0; i < 7; i++)
                Build.Part(PrimitiveType.Sphere, t, new Vector3((i % 2 == 0 ? -0.18f : 0.18f), 0.75f + (i % 3) * 0.08f, -0.45f + i * 0.15f), Vector3.one * 0.62f, wool, "Wool");
            head = Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.95f, 0.7f), new Vector3(0.28f, 0.32f, 0.38f), dark, "Head").transform;
            Build.Part(PrimitiveType.Cube, head, new Vector3(0.55f, 0.2f, -0.1f), new Vector3(0.5f, 0.2f, 0.3f), dark);
            Build.Part(PrimitiveType.Cube, head, new Vector3(-0.55f, 0.2f, -0.1f), new Vector3(0.5f, 0.2f, 0.3f), dark);
            Legs(dark, dark, 0.45f, 0.2f, 0.42f, 0.1f);
        }
        else // chicken
        {
            var white = Build.Mat(new Color(0.97f, 0.96f, 0.93f)); var red = Build.Mat(new Color(0.85f, 0.15f, 0.12f)); var yellow = Build.Mat(new Color(0.95f, 0.7f, 0.15f));
            Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.32f, 0), new Vector3(0.36f, 0.34f, 0.44f), white, "Body");
            Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.42f, -0.22f), new Vector3(0.18f, 0.24f, 0.14f), white, "Tail");
            head = Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.56f, 0.18f), new Vector3(0.18f, 0.2f, 0.18f), white, "Head").transform;
            Build.Part(PrimitiveType.Cube, head, new Vector3(0, 0.55f, 0), new Vector3(0.25f, 0.45f, 0.6f), red);
            Build.Part(PrimitiveType.Cube, head, new Vector3(0, -0.05f, 0.55f), new Vector3(0.3f, 0.2f, 0.45f), yellow);
            Legs(yellow, yellow, 0.18f, 0.08f, 0.08f, 0.035f);
        }
    }

    private void Legs(Material upper, Material hoof, float height, float halfWidth, float halfLength, float thick)
    {
        foreach (var o in new[] { new Vector2(halfWidth, halfLength), new Vector2(-halfWidth, halfLength), new Vector2(halfWidth, -halfLength), new Vector2(-halfWidth, -halfLength) })
        {
            var pivot = new GameObject("Leg").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(o.x, height, o.y);
            Build.Part(PrimitiveType.Cube, pivot, new Vector3(0, -height * 0.5f, 0), new Vector3(thick, height, thick), upper);
            Build.Part(PrimitiveType.Cube, pivot, new Vector3(0, -height + 0.04f, 0), new Vector3(thick * 1.1f, 0.08f, thick * 1.1f), hoof);
            legs.Add(pivot);
        }
    }

    private float grow = 1f;

    // Legacy animation clips imported with the CC0 models (Quaternius cow/sheep, CDmir chicken).
    private Animation anim;
    private string walkClip, idleClip, eatClip, playing;

    public void SetupAnimation(GameObject model)
    {
        anim = model.GetComponentInChildren<Animation>();
        if (anim == null) return;
        foreach (AnimationState s in anim)
        {
            string n = s.name.ToLowerInvariant();
            s.wrapMode = WrapMode.Loop;
            if (n.Contains("walkslow") || (walkClip == null && n.Contains("walk"))) walkClip = s.name;
            else if (n.EndsWith("idle") && idleClip == null) idleClip = s.name;
            else if ((n.Contains("eat") || n.Contains("peck")) && eatClip == null) eatClip = s.name;
        }
        if (idleClip == null) idleClip = walkClip;
        Play(idleClip);
    }

    private void Play(string clip)
    {
        if (anim == null || string.IsNullOrEmpty(clip) || clip == playing) return;
        anim.CrossFade(clip, 0.25f);
        playing = clip;
    }

    /// <summary>Newborn: starts small and grows to full size over about two in-game days.</summary>
    public void MakeBaby() { grow = 0.55f; transform.localScale = Vector3.one * grow; }

    private void PickTarget()
    {
        target = pen != null ? pen.RandomPoint() : transform.position;
        wait = Random.Range(1.5f, 6f);
    }

    private void Update()
    {
        if (grow < 1f)
        {
            float day = WorldClock.Instance != null ? WorldClock.Instance.secondsPerDay : 360f;
            grow = Mathf.Min(1f, grow + Time.deltaTime / (day * 2f) * 0.45f);
            transform.localScale = Vector3.one * grow;
        }
        Vector3 to = target - transform.position; to.y = 0f;
        bool walking = wait <= 0f && to.magnitude > 0.3f;
        if (wait > 0f) wait -= Time.deltaTime;
        else if (to.magnitude <= 0.3f) PickTarget();

        if (walking)
        {
            Quaternion look = Quaternion.LookRotation(to.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 120f * Time.deltaTime);
            Vector3 p = transform.position + transform.forward * speed * Time.deltaTime;
            transform.position = Build.OnGround(p);
            walkPhase += Time.deltaTime * speed * 7f;
        }
        if (anim != null) Play(walking ? (walkClip ?? idleClip) : (pen != null && pen.HasFeed && eatClip != null && Time.time % 9f > 6f ? eatClip : idleClip));
        float swing = walking ? Mathf.Sin(walkPhase) * 25f : 0f;
        for (int i = 0; i < legs.Count; i++) legs[i].localRotation = Quaternion.Euler((i == 0 || i == 3) ? swing : -swing, 0, 0);
        if (head != null)
        {
            // Graze (head down) while standing, bob while walking.
            float graze = !walking && pen != null && pen.HasFeed ? 28f : 0f;
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(graze + (walking ? Mathf.Sin(walkPhase * 2f) * 4f : 0f), 0, 0), 4f * Time.deltaTime);
        }
    }
}
