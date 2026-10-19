// Headless tests: on-foot movement feel (MoveFeel: speed / acceleration curves, frame-rate independence, jump, ground
// snap, gamepad look curve, head bob, third-person boom), pets (saluki + cat present and near the player after start, a
// teleport and a scene reload, on every preset and density) and the image (exposure budget, High-contrast menus never
// touching the 3D world).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class FeelTests
{
    static readonly float[] Rates = { 30f, 60f, 144f };

    public static void Run()
    {
        Console.WriteLine("Movement feel, pets, image");
        Movement();
        Look();
        Pets();
        Image();
    }

    // ------------------------------------------------------------------ movement

    /// <summary>Accelerate from v0 towards target at fps until the speed is within 5% (or maxT): seconds taken.</summary>
    static float TimeTo(Vector3 v0, Vector3 target, float fps, float accel = 1f, bool grounded = true, float maxT = 3f)
    {
        float dt = 1f / fps, t = 0f; Vector3 v = v0;
        while (t < maxT)
        {
            if ((v - target).magnitude <= Math.Max(0.05f * target.magnitude, 0.05f)) return t;
            v = MoveFeel.StepVelocity(v, target, dt, accel, grounded); t += dt;
        }
        return t;
    }

    /// <summary>Distance walked in seconds at fps with a fixed wanted velocity.</summary>
    static Vector3 Walk(Vector3 target, float seconds, float fps, Vector3 v0 = default)
    {
        float dt = 1f / fps; Vector3 v = v0, p = Vector3.zero;
        int n = Mathf.RoundToInt(seconds * fps);
        for (int i = 0; i < n; i++) { Vector3 v1 = MoveFeel.StepVelocity(v, target, dt, 1f, true); p += MoveFeel.StepMove(v, v1, dt); v = v1; }
        return p;
    }

    static void Movement()
    {
        Vector3 fwd = Vector3.forward;
        float walk = MoveFeel.TargetSpeed(false, true, 1f, 1f), run = MoveFeel.TargetSpeed(true, true, 1f, 1f);
        T.Check("Feel: walk 4.5 m/s, sprint 7.5 m/s", Math.Abs(walk - 4.5f) < 0.01f && Math.Abs(run - 7.5f) < 0.01f, walk + " / " + run);
        T.Check("Feel: no sprint allowed (load) = walk; stick tilt scales the speed",
            Math.Abs(MoveFeel.TargetSpeed(true, false, 1f, 1f) - walk) < 0.01f && Math.Abs(MoveFeel.TargetSpeed(false, true, 1f, 0.5f) - walk * 0.5f) < 0.01f && MoveFeel.TargetSpeed(true, true, 1f, 0f) == 0f);

        foreach (float fps in Rates)
        {
            float tw = TimeTo(Vector3.zero, fwd * walk, fps), tr = TimeTo(Vector3.zero, fwd * run, fps), ts = TimeTo(fwd * run, Vector3.zero, fps);
            T.Check("Feel @" + fps + " fps: walk speed in < 0.15 s, run in < 0.35 s, stop from a run in < 0.25 s (no sluggish ramp, no ice-skating)",
                tw < 0.15f && tr < 0.35f && ts < 0.25f, tw.ToString("F3") + " / " + tr.ToString("F3") + " / " + ts.ToString("F3"));
        }
        T.Check("Feel: not instant either (a few frames of acceleration at 144 fps)", TimeTo(Vector3.zero, fwd * run, 144f) > 0.1f);

        // the same distance whatever the frame rate
        var dists = Rates.Select(f => Walk(fwd * run, 1.5f, f)).ToArray();
        float spread = Math.Max(Vector3.Distance(dists[0], dists[1]), Vector3.Distance(dists[1], dists[2]));
        T.Check("Feel: 1.5 s of running covers the same ground at 30 / 60 / 144 fps", spread < 0.05f, string.Join(", ", dists.Select(d => d.z.ToString("F3"))));
        var stops = Rates.Select(f => Walk(Vector3.zero, 1f, f, fwd * run)).ToArray();
        T.Check("Feel: stopping distance from a run is short and frame-rate independent", stops.All(s => s.z < 0.6f) && Math.Abs(stops[0].z - stops[2].z) < 0.02f, string.Join(", ", stops.Select(d => d.z.ToString("F3"))));

        // turning keeps the speed, a reversal brakes first
        Vector3 v = fwd * run; float minSpeed = run;
        for (int i = 0; i < 30; i++) { v = MoveFeel.StepVelocity(v, Vector3.right * run, 1f / 60f, 1f, true); minSpeed = Math.Min(minSpeed, v.magnitude); }
        T.Check("Feel: a 90 degree turn at a run keeps the speed (no slow drift)", minSpeed > run * 0.9f && Vector3.Angle(v, Vector3.right) < 2f, minSpeed + " " + v);
        float tRev = TimeTo(fwd * run, -fwd * run, 60f);
        T.Check("Feel: reversing at a run takes a moment (brake, then accelerate) but < 0.6 s", tRev > 0.2f && tRev < 0.6f, tRev + "");
        float tAir = TimeTo(Vector3.zero, fwd * run, 60f, 1f, false);
        T.Check("Feel: little control in the air", tAir > TimeTo(Vector3.zero, fwd * run, 60f) * 3f, tAir + "");
        float heavy = TimeTo(Vector3.zero, fwd * walk * MoveRules.CarryFactor(25f), 60f, MoveRules.Acceleration(25f));
        T.Check("Feel: carrying 25 kg is mild (>= 85% speed, still quick to start)", MoveRules.CarryFactor(25f) >= 0.85f && heavy < 0.2f, MoveRules.CarryFactor(25f) + " / " + heavy);
        T.Check("Feel: a wall takes the push out of the velocity (slides along it)",
            MoveFeel.AfterCollision(fwd * run, new Vector3(1f, 0f, 0f)) == new Vector3(1f, 0f, 0f) && MoveFeel.AfterCollision(fwd * run, Vector3.zero) == Vector3.zero && MoveFeel.AfterCollision(fwd * 2f, fwd * 3f) == fwd * 2f);

        // jump: same height at any frame rate, about 0.85 m
        var apex = new List<float>();
        foreach (float fps in Rates)
        {
            // as the controller: take-off speed, then every frame (jump frame included) the exact move of MoveFeel.Fall
            float dt = 1f / fps, vy = MoveFeel.JumpVelocity(), y = 0f, top = 0f;
            for (int i = 0; i < 400 && (i == 0 || y > 0f); i++) { y += MoveFeel.Fall(ref vy, dt); top = Math.Max(top, y); }
            apex.Add(top);
        }
        T.Check("Feel: jump ~0.85 m at 30 / 60 / 144 fps", apex.All(a => a > 0.8f && a <= MoveFeel.JumpHeight + 0.001f) && apex.Max() - apex.Min() < 0.04f, string.Join(", ", apex.Select(a => a.ToString("F3"))));
        T.Check("Feel: coyote time and jump buffer are short but real", MoveFeel.CoyoteTime >= 0.08f && MoveFeel.CoyoteTime <= 0.2f && MoveFeel.JumpBuffer >= 0.08f && MoveFeel.JumpBuffer <= 0.2f);

        // ground snap: enough to follow a 50 degree slope or a step at run speed, even at 30 fps
        float step = run / 30f;
        T.Check("Feel: ground snap covers a step and the steepest walkable slope in one frame", MoveFeel.SnapDistance(step) >= step * 1.19f && MoveFeel.SnapDistance(0f) >= MoveFeel.StepOffset,
            MoveFeel.SnapDistance(step) + "");
        T.Check("Feel: capsule tuned (step 0.35 m, slope 50 deg, skin 0.05 m)", MoveFeel.StepOffset >= 0.3f && MoveFeel.SlopeLimit >= 45f && MoveFeel.SkinWidth >= 0.04f);

        // head bob: slight, off with Reduce motion or the setting, nothing while standing
        GameSettings.ReduceMotion = false; GameSettings.HeadBob = true;
        bool on = MoveFeel.BobEnabled;
        GameSettings.ReduceMotion = true; bool reduced = MoveFeel.BobEnabled;
        GameSettings.ReduceMotion = false; GameSettings.HeadBob = false; bool off = MoveFeel.BobEnabled;
        GameSettings.HeadBob = true;
        float maxBob = 0f, ph = 0f;
        for (int i = 0; i < 200; i++) { ph = MoveFeel.AdvanceBob(ph, run, 1f / 60f); var o = MoveFeel.BobOffset(ph, run, true); maxBob = Math.Max(maxBob, Math.Max(Math.Abs(o.x), Math.Abs(o.y))); }
        var still = MoveFeel.BobOffset(1.3f, 0f, true); var air = MoveFeel.BobOffset(1.3f, run, false);
        T.Check("Feel: head bob is slight (< 5 cm), none when still or in the air, off with Reduce motion / the setting",
            on && !reduced && !off && maxBob > 0.005f && maxBob < 0.05f && still.x == 0f && still.y == 0f && air.y == 0f, maxBob + "");
        // bob phase follows distance, not frames
        float p30 = 0f, p144 = 0f;
        for (int i = 0; i < 30; i++) p30 = MoveFeel.AdvanceBob(p30, walk, 1f / 30f);
        for (int i = 0; i < 144; i++) p144 = MoveFeel.AdvanceBob(p144, walk, 1f / 144f);
        T.Check("Feel: head bob rhythm is the same at 30 and 144 fps", Math.Abs(p30 - p144) < 0.01f, p30 + " vs " + p144);
        T.Check("Feel: frame-rate independent easing", Math.Abs((1f - MoveFeel.Ease(10f, 1f / 30f)) - Math.Pow(1f - MoveFeel.Ease(10f, 1f / 60f), 2)) < 1e-4f);
    }

    static void Look()
    {
        float hold = 0f;
        var small = MoveFeel.GamepadLookRate(0.2f, 0f, 1f / 60f, ref hold);
        hold = 0f; var half = MoveFeel.GamepadLookRate(0.5f, 0f, 1f / 60f, ref hold);
        hold = 0f; var full = MoveFeel.GamepadLookRate(1f, 0f, 1f / 60f, ref hold);
        T.Check("Look: gamepad response curve (fine aim near the centre, full speed at the rim)",
            small.x < MoveFeel.LookYawRate * 0.1f && half.x < MoveFeel.LookYawRate * 0.4f && Math.Abs(full.x - MoveFeel.LookYawRate) < 1f, small.x + " / " + half.x + " / " + full.x);
        hold = 0f; var none = MoveFeel.GamepadLookRate(0f, 0f, 1f / 60f, ref hold);
        T.Check("Look: no stick, no turn", none.x == 0f && none.y == 0f);
        // turn boost when held at the rim, and the same total turn at any frame rate
        var totals = new List<float>();
        foreach (float fps in Rates)
        {
            hold = 0f; float yaw = 0f, dt = 1f / fps;
            for (int i = 0; i < Mathf.RoundToInt(1.5f * fps); i++) yaw += MoveFeel.GamepadLookRate(1f, 0f, dt, ref hold).x * dt;
            totals.Add(yaw);
        }
        T.Check("Look: holding the stick at the rim speeds the turn up (acceleration)", totals[1] > MoveFeel.LookYawRate * 1.5f * 1.2f, totals[1] + "");
        T.Check("Look: gamepad turn is frame-rate independent (30 / 60 / 144 fps within 3%)", (totals.Max() - totals.Min()) / totals.Max() < 0.03f, string.Join(", ", totals.Select(x => x.ToString("F1"))));
        hold = 0f; var up = MoveFeel.GamepadLookRate(0f, 1f, 1f / 60f, ref hold);
        T.Check("Look: vertical look slower than horizontal", up.y > 0f && up.y < full.x);
        // the action scale used by the bridge and the controller matches the input actions (ScaleVector2 300)
        string actions = System.IO.File.ReadAllText(System.IO.Path.Combine(FakeEngine.ResourcesRoot, "..", "StarterAssets", "InputSystem", "StarterAssets.inputactions"));
        T.Check("Look: stick scale matches StarterAssets.inputactions", actions.Contains("ScaleVector2(x=300,y=300)") && MoveFeel.StickLookScale == 300f);

        // third person boom: walls pull it in at once, it eases back out
        T.Check("3rd person: a wall pulls the camera in at once", ThirdPersonView.SmoothBoom(4.2f, 1.2f, 1f / 60f) == 1.2f);
        float b = 1.2f; for (int i = 0; i < 6; i++) b = ThirdPersonView.SmoothBoom(b, 4.2f, 1f / 60f);
        float b2 = 1.2f; for (int i = 0; i < 120; i++) b2 = ThirdPersonView.SmoothBoom(b2, 4.2f, 1f / 60f);
        T.Check("3rd person: ... and eases back out (no popping)", b > 1.2f && b < 3f && b2 > 4.1f, b + " / " + b2);
        float py = ThirdPersonView.SmoothPivotY(0f, 1f, 1f / 60f);
        T.Check("3rd person: height follows steps smoothly but never lags more than 0.35 m", py >= 0.65f && py < 1f, py + "");
    }

    // ------------------------------------------------------------------ pets

    static void Boot()
    {
        Program.BuildWorld();
        FakeEngine.Run(3f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Run(1f, 0.1f);
    }

    static bool Visible(GameObject go) => go != null && go.activeInHierarchy && go.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && r.GetComponent<TMPro.TextMeshPro>() == null)
        && go.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(s => s.updateWhenOffscreen);

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    static void CheckPets(string when, float near)
    {
        var pets = PetSystem.Instance;
        var player = GameObject.Find("PlayerCapsule").transform;
        bool ok = pets != null && pets.Ready && pets.Saluki != null && pets.Cat != null;
        T.Check("Pets " + when + ": saluki and cat exist", ok);
        if (!ok) return;
        T.Check("Pets " + when + ": both visible (active, renderers on, skinned meshes never culled)", Visible(pets.Saluki) && Visible(pets.Cat));
        float ds = Flat(pets.Saluki.transform.position, player.position), dc = Flat(pets.Cat.transform.position, player.position);
        T.Check("Pets " + when + ": near the player", ds < near && dc < near, ds.ToString("F1") + " / " + dc.ToString("F1"));
        T.Check("Pets " + when + ": on the ground (not on a roof, not below)", Math.Abs(pets.Saluki.transform.position.y - Build.GroundY(pets.Saluki.transform.position)) < 0.3f
            && Math.Abs(pets.Cat.transform.position.y - Build.GroundY(pets.Cat.transform.position)) < 0.3f, pets.Saluki.transform.position + " " + pets.Cat.transform.position);
        var sa = pets.Saluki.GetComponent<Actor>(); var ca = pets.Cat.GetComponent<Actor>();
        T.Check("Pets " + when + ": both follow you and are pets", sa != null && ca != null && sa.mode == ActorMode.FollowPlayer && ca.mode == ActorMode.FollowPlayer && sa.IsPet && ca.IsPet);
    }

    static void Pets()
    {
        Boot();
        int e0 = FakeEngine.Errors.Count;
        CheckPets("after start", 8f);
        var pets = PetSystem.Instance;
        T.Check("Pets: falcon perch near the farmhouse with a visible falcon", pets.Perch != null && Flat(pets.Perch.transform.position, new Vector3(0, 0, -57.4f)) < 12f && Visible(pets.Perch.transform.Find("Falcon").gameObject));

        // they walk after you
        var player = GameObject.Find("PlayerCapsule").transform;
        player.position += new Vector3(20f, 0f, 0f);
        FakeEngine.Run(8f, 0.05f);
        CheckPets("after walking 20 m", 9f);

        // fast travel / teleport far away: they catch up
        player.position += new Vector3(300f, 0f, 200f);
        FakeEngine.Run(3f, 0.1f);
        CheckPets("after a long teleport", 8f);

        // Settings: Ambient life density never hides pets; every graphics preset keeps them visible
        GameSettings.AmbientDensity = 0;
        FakeEngine.Run(0.5f, 0.1f);
        T.Check("Pets: Ambient life 'Off' hides other animals but never pets", SettingsApplier.Instance.HiddenAmbient > 0 && Visible(pets.Saluki) && Visible(pets.Cat), SettingsApplier.Instance.HiddenAmbient + "");
        GameSettings.AmbientDensity = 3;
        for (int p = 0; p < GraphicsEnhancer.PresetCount; p++)
        {
            GraphicsEnhancer.SetPreset(p);
            FakeEngine.Run(2.5f, 0.1f);
            T.Check("Pets: visible and near on the " + GameSettings.PresetName(p) + " preset", Visible(pets.Saluki) && Visible(pets.Cat) && Flat(pets.Saluki.transform.position, player.position) < 10f);
        }
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetHigh);

        // a skinned mesh that got switched off comes back on and is never culled by stale bounds
        var skin = new GameObject("fur").AddComponent<SkinnedMeshRenderer>();
        skin.transform.SetParent(pets.Cat.transform, false);
        skin.enabled = false;
        FakeEngine.Run(2.5f, 0.1f);
        T.Check("Pets: renderers switched back on, skinned meshes updated off-screen", skin.enabled && skin.updateWhenOffscreen);

        // something switched a pet off or destroyed it: the next check brings it back
        pets.Cat.SetActive(false);
        UnityEngine.Object.Destroy(pets.Saluki);
        FakeEngine.Run(2.5f, 0.1f);
        CheckPets("after being removed", 8f);
        T.Check("Pets: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        // scene reload (new scene, statics reset): pets are back next to the player
        Boot();
        CheckPets("after a scene reload", 8f);
    }

    // ------------------------------------------------------------------ image

    static void Image()
    {
        var ge = GraphicsEnhancer.Instance;
        var atmo = AtmosphereLook.Instance;
        T.Check("Image: systems present", ge != null && atmo != null);
        if (ge == null || atmo == null) return;

        // exposure budget on every preset and brightness, also in a heatwave
        var clock = WorldClock.Instance;
        T.SetProp(clock, "Weather", Weather.Heatwave);
        FakeEngine.Run(2f, 0.1f);
        bool inRange = true; string worst = "";
        foreach (int p in new[] { 0, 1, 2, 3 })
            foreach (float b in new[] { 0.5f, 1f, 1.5f })
            {
                GraphicsEnhancer.SetPreset(p); GameSettings.Brightness = b;
                FakeEngine.Tick(0.1f);
                float ev = ge.AppliedExposure;
                if (ev < GraphicsEnhancer.MinExposure - 1e-4f || ev > GraphicsEnhancer.MaxExposure + 1e-4f || Math.Abs(ev - GraphicsEnhancer.ExposureFor(p, b)) > 1e-4f) { inRange = false; worst += p + "/" + b + "=" + ev + " "; }
            }
        T.Check("Image: final exposure stays in " + GraphicsEnhancer.MinExposure + ".." + GraphicsEnhancer.MaxExposure + " EV on every preset and brightness", inRange, worst);
        GameSettings.Brightness = 1f;
        bool natural = true; string vals = "";
        foreach (int p in new[] { 0, 1, 2, 3 })
        {
            GraphicsEnhancer.SetPreset(p); FakeEngine.Tick(0.1f);
            vals += p + ":" + ge.AppliedExposure.ToString("F2") + "/" + ge.AppliedContrast + "/" + ge.AppliedBloom + " ";
            if (Math.Abs(ge.AppliedExposure) > 0.15f || ge.AppliedContrast > 10f || ge.AppliedBloom > 0.5f) natural = false;
        }
        T.Check("Image: default brightness is natural on every preset (|EV| <= 0.15, contrast <= 10, bloom <= 0.5)", natural, vals);
        T.Check("Image: Ultra is never brighter than High", GraphicsEnhancer.ExposureFor(3, 1f) <= GraphicsEnhancer.ExposureFor(2, 1f) && GraphicsEnhancer.ExposureFor(3, 1.5f) <= GraphicsEnhancer.MaxExposure);
        T.Check("Image: the sky that lights the farm is capped", RenderSettings.skybox == null || !RenderSettings.skybox.HasProperty("_Exposure") || RenderSettings.skybox.GetFloat("_Exposure") <= AtmosphereLook.MaxSkyExposure + 1e-4f);
        T.SetProp(clock, "Weather", Weather.Sunny);

        // High-contrast menus only touch the menus, never the 3D world
        foreach (int p in new[] { 2, 3 })
        {
            GraphicsEnhancer.SetPreset(p);
            GameSettings.HighContrast = false; FakeEngine.Tick(0.1f);
            var before = Snapshot(ge, atmo);
            GameSettings.HighContrast = true; FakeEngine.Tick(0.1f);
            var after = Snapshot(ge, atmo);
            GameSettings.HighContrast = false; FakeEngine.Tick(0.1f);
            T.Check("Image: High-contrast menus leave post-processing, brightness and lighting untouched (" + GameSettings.PresetName(p) + ")", before == after, before + " -> " + after);
        }
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetHigh);
    }

    static string Snapshot(GraphicsEnhancer ge, AtmosphereLook atmo)
    {
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        return string.Join(",", new object[] { ge.AppliedExposure, ge.LookWeight, ge.AppliedContrast, ge.AppliedBloom, ge.GradeSaturation, atmo.GradeVolume != null ? atmo.GradeVolume.weight : -1f,
            RenderSettings.fog, GraphicsEnhancer.Preset, urp != null ? urp.renderScale : -1f, Camera.main != null ? Camera.main.fieldOfView : -1f });
    }
}
