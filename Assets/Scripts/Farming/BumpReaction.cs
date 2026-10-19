using UnityEngine;

/// <summary>
/// The light-hearted reaction of a bumped person or animal (added on demand by NpcBump; no ragdoll, nothing graphic):
/// a quick knock-back along the push, then either a stagger, or a cartoon tumble onto their back, a moment on the ground
/// and getting back up. Animals run off afterwards (Flee), farm animals stay inside their pen. Their own walking
/// (Actor / FarmAnimal) is paused meanwhile and resumes afterwards; people who stand at a spot (shopkeepers) walk back to it.
/// </summary>
public class BumpReaction : MonoBehaviour
{
    public enum Phase { None, Shove, Down, GetUp, Flee, Return }

    public const float ShoveTime = 0.35f, DownTime = 0.8f, GetUpTime = 0.6f, FleeTime = 2.2f, FallTilt = 80f;

    public Phase State { get; private set; }
    public bool Busy => State != Phase.None;
    public bool Fleeing => State == Phase.Flee;
    /// <summary>How far they lean or lie (degrees, 0 = upright).</summary>
    public float Tilt { get; private set; }
    /// <summary>Can't be bumped again until this runs out (s).</summary>
    public float Cooldown { get; private set; }
    public Vector3 Origin { get; private set; }

    private Behaviour mover;
    private bool paused, fall, fleeAfter, returnAfter;
    private Quaternion yawRot = Quaternion.identity;
    private Vector3 from, to, axis = Vector3.right, fleeDir = Vector3.forward;
    private float t, fleeLeft, fleeSpeed = 3f;

    /// <summary>Knock back by distance along dir; fall = tumble over and get up, flee = run off afterwards (animals).</summary>
    public void Knock(Vector3 dir, float distance, bool fall, bool flee)
    {
        Pause();
        Vector3 push = dir; push.y = 0f;
        push = push.sqrMagnitude > 1e-4f ? push.normalized : -transform.forward;
        from = transform.position;
        to = Build.OnGround(from + push * distance);
        axis = Vector3.Cross(Vector3.up, push);          // tipping about this axis leans the top along the push
        fleeDir = push;
        this.fall = fall;
        fleeAfter = flee;
        State = Phase.Shove; t = 0f; Cooldown = 1.2f;
    }

    /// <summary>Run away from something for a couple of seconds (startled animals).</summary>
    public void Flee(Vector3 away)
    {
        if (Busy) return;
        Pause();
        away.y = 0f;
        fleeDir = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
        fleeLeft = FleeTime; State = Phase.Flee; Tilt = 0f;
    }

    private void Pause()
    {
        if (!paused)
        {
            var a = GetComponent<Actor>();
            mover = a != null ? (Behaviour)a : GetComponent<FarmAnimal>();
            if (mover != null) mover.enabled = false;
            paused = true;
            Origin = transform.position;
            returnAfter = mover == null || (a != null && a.mode == ActorMode.Stand);
            var fa = GetComponent<FarmAnimal>();
            fleeSpeed = a != null ? Mathf.Clamp(a.speed * 3f, 2.5f, 6f) : fa != null ? (fa.animalId == "cow" ? 2.6f : 3.2f) : 2.5f;
        }
        yawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (State == Phase.None) { if (Cooldown > 0f) Cooldown -= dt; return; }
        t += dt;
        switch (State)
        {
            case Phase.Shove:
            {
                float k = Mathf.Clamp01(t / ShoveTime), e = 1f - (1f - k) * (1f - k);
                transform.position = Build.OnGround(Vector3.Lerp(from, to, e));
                Tilt = fall ? FallTilt * e : 18f * Mathf.Sin(k * Mathf.PI);   // tumble over, or just stagger
                Apply();
                if (k >= 1f) { if (fall) Next(Phase.Down); else Recovered(); }
                break;
            }
            case Phase.Down:
                Tilt = FallTilt - 5f * Mathf.Sin(t * 14f) * Mathf.Exp(-t * 5f);   // a little bounce on landing
                Apply();
                if (t >= DownTime) Next(Phase.GetUp);
                break;
            case Phase.GetUp:
            {
                float k = Mathf.Clamp01(t / GetUpTime);
                Tilt = FallTilt * (1f - k * k * (3f - 2f * k));
                Apply();
                if (k >= 1f) Recovered();
                break;
            }
            case Phase.Flee:
            {
                fleeLeft -= dt;
                Vector3 next = transform.position + fleeDir * fleeSpeed * dt;
                var fa = GetComponent<FarmAnimal>();
                if (fa != null && fa.pen != null && !fa.pen.Contains(next))
                {
                    Vector3 c = fa.pen.transform.position - transform.position; c.y = 0f;   // run along inside the pen instead
                    fleeDir = (fleeDir + c.normalized).normalized;
                    next = transform.position + fleeDir * fleeSpeed * dt;
                }
                transform.position = Build.OnGround(next);
                transform.rotation = Quaternion.LookRotation(fleeDir);
                if (fleeLeft <= 0f) Resume();
                break;
            }
            case Phase.Return:
            {
                Vector3 d = Origin - transform.position; d.y = 0f;
                if (d.magnitude < 0.15f || t > 8f) { Resume(); break; }
                transform.rotation = Quaternion.LookRotation(d.normalized);
                transform.position = Build.OnGround(transform.position + d.normalized * Mathf.Min(d.magnitude, 1.1f * dt));
                break;
            }
        }
    }

    private void Next(Phase p) { State = p; t = 0f; }

    private void Recovered()
    {
        Tilt = 0f;
        transform.rotation = yawRot;
        if (fleeAfter) { fleeAfter = false; fleeLeft = FleeTime; Next(Phase.Flee); return; }
        if (returnAfter) { Next(Phase.Return); return; }
        Resume();
    }

    private void Resume()
    {
        State = Phase.None; Tilt = 0f; Cooldown = 1f; t = 0f;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (mover != null) mover.enabled = true;
        paused = false;
    }

    private void Apply() => transform.rotation = Quaternion.AngleAxis(Tilt, axis) * yawRot;

    private void OnDisable()
    {
        // never leave someone lying down or frozen if the object is switched off mid-tumble
        if (State != Phase.None) Resume();
    }
}
