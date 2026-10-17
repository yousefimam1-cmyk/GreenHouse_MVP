using UnityEngine;

/// <summary>
/// Mazraa's menu / loading theme, synthesised at startup (original music, no licence needed, no audio file in the build):
/// a 24-second seamless loop at 80 BPM in D Hijaz. Layers: warm string pad (detuned saws, low-passed), an oud-like plucked
/// melody (Karplus-Strong), a frame-drum (daf) groove and a soft bass. Mono, 22 kHz (~0.5M samples, ~2 MB).
/// A recorded track at Resources/Sounds/theme replaces it automatically (ProceduralAudio.Get).
/// </summary>
public static class MenuMusic
{
    public const float Bpm = 80f;
    public const int Bars = 8;
    public static float Beat => 60f / Bpm;
    public static float Length => Bars * 4 * Beat;

    // D Hijaz: D Eb F# G A Bb C (MIDI 62 = D4)
    private static readonly int[] Scale = { 0, 1, 4, 5, 7, 8, 10 };

    private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
    private static int Deg(int degree, int octave = 0)
    {
        int o = Mathf.FloorToInt(degree / 7f);
        int d = degree - o * 7;
        return 62 + Scale[d] + 12 * (o + octave);
    }

    // Melody: (start in eighths, scale degree, length in eighths); 8 bars = 64 eighths. Rests between phrases.
    private static readonly int[,] Melody =
    {
        {0,4,2},{2,5,1},{3,4,1},{4,3,2},{6,2,2},{8,1,3},{11,2,1},{12,0,4},
        {18,0,1},{19,1,1},{20,2,2},{22,3,2},{24,4,3},{27,5,1},{28,4,4},
        {32,7,2},{34,6,1},{35,5,1},{36,4,2},{38,5,2},{40,4,2},{42,3,1},{43,2,1},{44,1,4},
        {50,4,1},{51,3,1},{52,2,2},{54,1,2},{56,0,6},
    };

    // Chord roots per bar (scale degrees): i  bII  i  iv | i  bVII  bII  i
    private static readonly int[] ChordRoots = { 0, 1, 0, 3, 0, 6, 1, 0 };

    public static float[] Theme()
    {
        int rate = ProceduralAudio.Rate;
        int n = Mathf.RoundToInt(Length * rate);
        var mix = new float[n];
        float beat = Beat, eighth = beat * 0.5f, bar = beat * 4f;
        var rng = new System.Random(1407);

        // ---- pad: two detuned saws per chord tone, slow attack, simple one-pole low-pass, crossfaded bars
        for (int b = 0; b < Bars; b++)
        {
            int root = ChordRoots[b];
            int[] tones = { Deg(root, -1), Deg(root + 2, -1), Deg(root + 4, -1) };
            int s0 = Mathf.RoundToInt(b * bar * rate), len = Mathf.RoundToInt(bar * rate);
            foreach (int t in tones)
            {
                float f = Midi(t);
                float lp = 0f, ph1 = 0f, ph2 = 0f;
                for (int i = 0; i < len + rate / 4; i++)
                {
                    int idx = (s0 + i) % n;               // tail wraps to the start: seamless loop
                    float tt = i / (float)rate;
                    ph1 += f / rate; ph2 += f * 1.006f / rate;
                    float saw = (ph1 % 1f) * 2f - 1f + (ph2 % 1f) * 2f - 1f;
                    lp += (saw - lp) * 0.035f;
                    float env = Mathf.Clamp01(tt / 0.6f) * Mathf.Clamp01((bar + 0.25f - tt) / 0.5f);
                    mix[idx] += lp * env * 0.045f;
                }
            }
            // bass on beats 1 and 3
            for (int k = 0; k < 2; k++)
            {
                float f = Midi(Deg(root, -2));
                int st = s0 + Mathf.RoundToInt(k * 2 * beat * rate);
                int bl = Mathf.RoundToInt(beat * 1.8f * rate);
                for (int i = 0; i < bl; i++)
                {
                    float tt = i / (float)rate;
                    float env = Mathf.Min(1f, tt / 0.02f) * Mathf.Exp(-tt * 2.2f);
                    mix[(st + i) % n] += Mathf.Sin(2f * Mathf.PI * f * tt) * env * 0.16f;
                }
            }
        }

        // ---- oud-like pluck melody (Karplus-Strong), with a softer octave echo one beat later
        for (int m = 0; m < Melody.GetLength(0); m++)
        {
            float start = Melody[m, 0] * eighth;
            float f = Midi(Deg(Melody[m, 1]));
            float dur = Melody[m, 2] * eighth;
            Pluck(mix, rate, start, f, dur + 0.35f, 0.32f, rng);
            Pluck(mix, rate, start + beat, f * 2f, dur * 0.6f + 0.2f, 0.07f, rng);
        }

        // ---- daf groove: dum . . tek | dum dum tek . (per bar), ghost taks
        string[] pattern = { "D", "", "t", "T", "D", "D", "T", "t" };
        for (int b = 0; b < Bars; b++)
            for (int s = 0; s < 8; s++)
            {
                string hit = pattern[s];
                if (hit.Length == 0) continue;
                float at = b * bar + s * eighth;
                if (hit == "D") Dum(mix, rate, at, 0.34f);
                else Tek(mix, rate, at, hit == "T" ? 0.12f : 0.05f, rng);
            }

        // ---- master: soft clip, normalise to -3 dB, tiny fades so the loop point never clicks
        float peak = 0f;
        for (int i = 0; i < n; i++) { mix[i] = (float)System.Math.Tanh(mix[i] * 1.2f); peak = Mathf.Max(peak, Mathf.Abs(mix[i])); }
        float gain = peak > 0f ? 0.7f / peak : 1f;
        int fade = rate / 200;
        for (int i = 0; i < n; i++)
        {
            float e = 1f;
            if (i < fade) e = i / (float)fade; else if (i > n - fade) e = (n - i) / (float)fade;
            mix[i] *= gain * e;
        }
        return mix;
    }

    private static void Pluck(float[] mix, int rate, float start, float freq, float dur, float amp, System.Random rng)
    {
        int n = mix.Length;
        int period = Mathf.Max(2, Mathf.RoundToInt(rate / freq));
        var buf = new float[period];
        for (int i = 0; i < period; i++) buf[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        int s0 = Mathf.RoundToInt(start * rate), len = Mathf.RoundToInt(dur * rate);
        int p = 0; float prev = 0f;
        for (int i = 0; i < len; i++)
        {
            float v = buf[p];
            int q = (p + 1) % period;
            float next = 0.5f * (buf[p] + buf[q]) * 0.996f;
            buf[p] = next;
            p = q;
            // body resonance: a little low-pass for a warmer, wooden tone
            prev += (v - prev) * 0.55f;
            float env = Mathf.Clamp01((len - i) / (rate * 0.08f));
            mix[(s0 + i) % n] += prev * amp * env;
        }
    }

    private static void Dum(float[] mix, int rate, float at, float amp)
    {
        int n = mix.Length, s0 = Mathf.RoundToInt(at * rate), len = Mathf.RoundToInt(0.45f * rate);
        float ph = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)rate;
            float f = 55f + 70f * Mathf.Exp(-t * 30f);
            ph += f / rate;
            mix[(s0 + i) % n] += Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-t * 9f) * amp;
        }
    }

    private static void Tek(float[] mix, int rate, float at, float amp, System.Random rng)
    {
        int n = mix.Length, s0 = Mathf.RoundToInt(at * rate), len = Mathf.RoundToInt(0.09f * rate);
        float hp = 0f, last = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)rate;
            float w = (float)(rng.NextDouble() * 2.0 - 1.0);
            hp = 0.7f * (hp + w - last); last = w;
            float tone = Mathf.Sin(2f * Mathf.PI * 820f * t) * 0.5f;
            mix[(s0 + i) % n] += (hp * 0.8f + tone) * Mathf.Exp(-t * 45f) * amp;
        }
    }
}
