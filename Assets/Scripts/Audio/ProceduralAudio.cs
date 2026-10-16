using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sounds synthesised at startup (no audio files needed): engine loop, wind loop, night crickets, bird chirps,
/// footsteps on sand, and farm-animal calls (cow, sheep, chicken, camel, goat). If a clip exists at
/// Resources/Sounds/&lt;name&gt; (e.g. a CC0 recording) it is used instead. Clips are cached.
/// </summary>
public static class ProceduralAudio
{
    public const int Rate = 22050;
    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>Clip by name: Resources/Sounds/name if present, else synthesised. Unknown names return null.</summary>
    public static AudioClip Get(string name)
    {
        AudioClip c;
        if (cache.TryGetValue(name, out c) && c != null) return c;
        c = Resources.Load<AudioClip>("Sounds/" + name);
        if (c == null)
        {
            float[] data = Synth(name);
            if (data == null) return null;
            c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
        }
        cache[name] = c;
        return c;
    }

    public static readonly string[] Animals = { "cow", "sheep", "chicken", "camel", "goat" };

    public static float[] Synth(string name)
    {
        switch (name)
        {
            case "engine": return Engine();
            case "wind": return Wind();
            case "crickets": return Crickets();
            case "birds": return Birds();
            case "step0": return Step(1);
            case "step1": return Step(2);
            case "step2": return Step(3);
            case "step3": return Step(4);
            case "cow": return Voice(1.5f, 118f, 92f, 4f, new[] { 450f, 900f, 2400f }, 0.0f, 0f, 7);
            case "sheep": return Voice(0.85f, 340f, 300f, 18f, new[] { 800f, 1400f, 2600f }, 0.55f, 0.03f, 8);
            case "goat": return Voice(0.7f, 420f, 380f, 22f, new[] { 900f, 1600f, 2900f }, 0.6f, 0.05f, 9);
            case "camel": return Camel();
            case "chicken": return Chicken();
            case "theme": return MenuMusic.Theme();
            default: return null;
        }
    }

    // ------------------------------------------------------------------ helpers
    private static float Env(float t, float len, float attack, float release)
    {
        if (t < attack) return t / attack;
        if (t > len - release) return Mathf.Max(0f, (len - t) / release);
        return 1f;
    }

    /// <summary>Crossfade the end into the start so the clip loops without a click.</summary>
    private static float[] Loop(float[] d, int fade)
    {
        int n = d.Length - fade;
        var o = new float[n];
        for (int i = 0; i < n; i++) o[i] = d[i];
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; o[i] = d[i] * k + d[n + i] * (1f - k); }
        return o;
    }

    private static void Normalize(float[] d, float peak)
    {
        float m = 1e-6f;
        foreach (var s in d) m = Math.Max(m, Math.Abs(s));
        float k = peak / m;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }

    // ------------------------------------------------------------------ loops
    /// <summary>Diesel-ish idle: firing pulses at 40 Hz with harmonics and a little rumble. 1 s loop (pitch by speed).</summary>
    private static float[] Engine()
    {
        var rnd = new System.Random(11);
        int n = Rate + 2205;
        var d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float ph = t * 40f % 1f;                                  // firing cycle
            float pulse = Mathf.Exp(-ph * 9f) - 0.25f;
            float body = 0.5f * Mathf.Sin(2f * Mathf.PI * 40f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 80f * t + 0.6f) + 0.15f * Mathf.Sin(2f * Mathf.PI * 120f * t + 1.1f);
            lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.08f;   // low-passed noise
            d[i] = pulse * 0.6f + body * 0.5f + lp * 0.35f;
        }
        d = Loop(d, 2205);
        Normalize(d, 0.8f);
        return d;
    }

    /// <summary>Wind: brown noise with slow swells and a faint whistle. 6 s loop.</summary>
    private static float[] Wind()
    {
        var rnd = new System.Random(5);
        int fade = Rate / 2, n = Rate * 6 + fade;
        var d = new float[n];
        float brown = 0f, bp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)rnd.NextDouble() * 2f - 1f;
            brown = (brown + white * 0.02f) * 0.996f;
            bp += (white - bp) * 0.25f;                                // brighter hiss
            float swell = 0.6f + 0.4f * Mathf.Sin(t * 0.9f) * Mathf.Sin(t * 0.37f + 1f);
            d[i] = (brown * 6f + bp * 0.08f * swell) * swell;
        }
        d = Loop(d, fade);
        Normalize(d, 0.7f);
        return d;
    }

    /// <summary>Two crickets chirping at different rates (4.4 and 4.9 kHz). 4 s loop.</summary>
    private static float[] Crickets()
    {
        int n = Rate * 4;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            d[i] = Chirp(t, 0.5f, 0f, 4400f) * 0.6f + Chirp(t, 0.8f, 0.21f, 4900f) * 0.4f;
        }
        Normalize(d, 0.5f);
        return d;
    }

    private static float Chirp(float t, float period, float offset, float freq)
    {
        float local = (t + offset) % period;
        float a = 0f;
        for (int k = 0; k < 3; k++)
        {
            float s = local - k * 0.045f;
            if (s >= 0f && s < 0.028f) a = Mathf.Sin(s / 0.028f * Mathf.PI);
        }
        return a * Mathf.Sin(2f * Mathf.PI * freq * t);
    }

    /// <summary>A short song: 4 rising/falling whistles.</summary>
    private static float[] Birds()
    {
        int n = (int)(Rate * 1.1f);
        var d = new float[n];
        float[] starts = { 0f, 0.18f, 0.36f, 0.62f };
        float[] f0 = { 2600f, 3100f, 2800f, 3600f }, f1 = { 3800f, 2500f, 3900f, 2900f };
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, v = 0f, f = 0f;
            for (int k = 0; k < starts.Length; k++)
            {
                float s = t - starts[k];
                if (s < 0f || s > 0.12f) continue;
                f = Mathf.Lerp(f0[k], f1[k], s / 0.12f);
                v = Mathf.Sin(s / 0.12f * Mathf.PI);
            }
            phase += 2f * Mathf.PI * f / Rate;
            d[i] = v * Mathf.Sin(phase);
        }
        Normalize(d, 0.4f);
        return d;
    }

    /// <summary>Footstep on sand: a soft thud plus a crunchy hiss.</summary>
    private static float[] Step(int seed)
    {
        var rnd = new System.Random(seed * 31);
        int n = (int)(Rate * 0.16f);
        var d = new float[n];
        float lp = 0f, hp = 0f, prev = 0f;
        float tone = 70f + seed * 9f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)rnd.NextDouble() * 2f - 1f;
            lp += (white - lp) * 0.06f;
            hp = 0.7f * (hp + white - prev); prev = white;
            float env = Mathf.Exp(-t * 32f) * Mathf.Min(1f, t * 400f);
            d[i] = (lp * 2.2f + hp * 0.22f) * env + Mathf.Sin(2f * Mathf.PI * tone * t) * Mathf.Exp(-t * 45f) * 0.5f;
        }
        Normalize(d, 0.6f);
        return d;
    }

    // ------------------------------------------------------------------ animals
    /// <summary>
    /// Voiced animal call by additive synthesis: pitch glides f0Start -> f0End, harmonics shaped by formants,
    /// tremolo (sheep/goat bleat), a little breath noise.
    /// </summary>
    private static float[] Voice(float len, float f0Start, float f0End, float tremoloHz, float[] formants, float tremoloDepth, float noise, int seed)
    {
        var rnd = new System.Random(seed);
        int n = (int)(Rate * len);
        var d = new float[n];
        int harmonics = 14;
        var phase = new float[harmonics];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float k = t / len;
            float f0 = Mathf.Lerp(f0Start, f0End, k) * (1f + 0.012f * Mathf.Sin(t * 2f * Mathf.PI * 5f));
            float s = 0f;
            for (int h = 1; h <= harmonics; h++)
            {
                float fh = f0 * h;
                if (fh > Rate * 0.45f) break;
                float g = 0.15f;
                foreach (var fm in formants) { float x = (fh - fm) / (fm * 0.35f); g += Mathf.Exp(-x * x); }
                phase[h - 1] += 2f * Mathf.PI * fh / Rate;
                s += g / h * Mathf.Sin(phase[h - 1]);
            }
            float trem = 1f - tremoloDepth * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * tremoloHz * t));
            s += noise * ((float)rnd.NextDouble() * 2f - 1f);
            d[i] = s * trem * Env(t, len, 0.12f, len * 0.35f);
        }
        Normalize(d, 0.75f);
        return d;
    }

    /// <summary>Camel grumble: low rough voice (70 Hz) with irregular gurgling.</summary>
    private static float[] Camel()
    {
        var rnd = new System.Random(21);
        float len = 1.7f;
        int n = (int)(Rate * len);
        var d = new float[n];
        float phase = 0f, rough = 1f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            if (i % 600 == 0) rough = 0.4f + (float)rnd.NextDouble() * 0.9f;
            float f0 = 72f + 18f * Mathf.Sin(t * 2.2f) - 10f * t;
            phase += 2f * Mathf.PI * f0 / Rate;
            float saw = 0f;
            for (int h = 1; h <= 10; h++) saw += Mathf.Sin(phase * h) / h * (h == 4 || h == 5 ? 2f : 1f);
            lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.1f;
            d[i] = (saw * rough + lp * 1.5f) * Env(t, len, 0.2f, 0.6f);
        }
        Normalize(d, 0.75f);
        return d;
    }

    /// <summary>Hen: a few quick clucks with a falling pitch, then a longer "bawk".</summary>
    private static float[] Chicken()
    {
        int n = (int)(Rate * 0.95f);
        var d = new float[n];
        float[] starts = { 0f, 0.13f, 0.24f, 0.45f };
        float[] lens = { 0.06f, 0.06f, 0.07f, 0.28f };
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, v = 0f, f = 600f;
            for (int k = 0; k < starts.Length; k++)
            {
                float s = t - starts[k];
                if (s < 0f || s > lens[k]) continue;
                float u = s / lens[k];
                f = k == 3 ? Mathf.Lerp(950f, 700f, u) : Mathf.Lerp(820f, 560f, u);
                v = Mathf.Sin(u * Mathf.PI);
            }
            phase += 2f * Mathf.PI * f / Rate;
            float s1 = Mathf.Sin(phase) + 0.6f * Mathf.Sin(phase * 2f) + 0.35f * Mathf.Sin(phase * 3f) + 0.2f * Mathf.Sin(phase * 5f);
            d[i] = v * s1;
        }
        Normalize(d, 0.6f);
        return d;
    }
}
