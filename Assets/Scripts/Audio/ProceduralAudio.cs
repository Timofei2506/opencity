using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// All sound in the game is synthesised at runtime (no wav assets): engine, gunshots,
    /// sirens, footsteps, explosions, ambience. Deterministic, tiny and license free.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        static AudioClip engine, gunshot, footstep, siren, screech, explosion, reload, cash, impact, ambience, hitSound, horn;
        static AudioSource ambienceSource;

        // ------------------------------------------------------------------ dsp helpers

        static float[] NewBuffer(float seconds) => new float[Mathf.Max(16, (int)(seconds * SampleRate))];

        static void ApplyFade(float[] buf, float fadeSeconds)
        {
            int n = Mathf.Min(buf.Length / 2, (int)(fadeSeconds * SampleRate));
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                buf[i] *= k;
                buf[buf.Length - 1 - i] *= k;
            }
        }

        static void LowPass(float[] buf, float cutoffHz)
        {
            float rc = 1f / (2f * Mathf.PI * cutoffHz);
            float dt = 1f / SampleRate;
            float a = dt / (rc + dt);
            float prev = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                prev += a * (buf[i] - prev);
                buf[i] = prev;
            }
        }

        static void HighPass(float[] buf, float cutoffHz)
        {
            float rc = 1f / (2f * Mathf.PI * cutoffHz);
            float dt = 1f / SampleRate;
            float a = rc / (rc + dt);
            float prevIn = buf[0], prevOut = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float x = buf[i];
                prevOut = a * (prevOut + x - prevIn);
                prevIn = x;
                buf[i] = prevOut;
            }
        }

        static float Noise() => Random.Range(-1f, 1f);

        // ------------------------------------------------------------------ clips

        static AudioClip Build(string name, float[] data, bool loop)
        {
            // normalise
            float peak = 0.0001f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float norm = 0.85f / peak;
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] * norm, -1f, 1f);

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip Engine
        {
            get
            {
                if (engine != null) return engine;
                // idle-ish 1.0 s loop: 50 Hz fundamental with harmonic stack + combustion noise
                float seconds = 1f;
                var buf = NewBuffer(seconds);
                float baseF = 50f; // integer number of cycles in the loop
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float s = 0f;
                    s += Mathf.Sin(2f * Mathf.PI * baseF * t) * 0.55f;
                    s += Mathf.Sin(2f * Mathf.PI * baseF * 2f * t) * 0.30f;
                    s += Mathf.Sin(2f * Mathf.PI * baseF * 3f * t) * 0.16f;
                    s += Mathf.Sin(2f * Mathf.PI * baseF * 5f * t) * 0.09f;
                    s += Noise() * 0.07f;
                    buf[i] = s;
                }
                LowPass(buf, 1400f);
                // crossfade the seam so the loop is click free
                int fade = (int)(0.05f * SampleRate);
                for (int i = 0; i < fade; i++)
                {
                    float k = (float)i / fade;
                    buf[i] = Mathf.Lerp(buf[buf.Length - fade + i], buf[i], k);
                }
                engine = Build("engine", buf, true);
                return engine;
            }
        }

        public static AudioClip Gunshot
        {
            get
            {
                if (gunshot != null) return gunshot;
                var buf = NewBuffer(0.42f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Mathf.Exp(-t * 26f);
                    float body = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 16f) * 0.7f;
                    buf[i] = (Noise() * env + body) * 1.1f;
                }
                LowPass(buf, 5200f);
                ApplyFade(buf, 0.004f);
                gunshot = Build("gunshot", buf, false);
                return gunshot;
            }
        }

        public static AudioClip Footstep
        {
            get
            {
                if (footstep != null) return footstep;
                var buf = NewBuffer(0.11f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    buf[i] = Noise() * Mathf.Exp(-t * 62f);
                }
                LowPass(buf, 1600f);
                ApplyFade(buf, 0.005f);
                footstep = Build("footstep", buf, false);
                return footstep;
            }
        }

        public static AudioClip Siren
        {
            get
            {
                if (siren != null) return siren;
                var buf = NewBuffer(2.4f);
                float phase = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float f = 720f + 320f * Mathf.Sin(2f * Mathf.PI * t / 2.4f);
                    phase += 2f * Mathf.PI * f / SampleRate;
                    float saw = Mathf.Repeat(phase / (2f * Mathf.PI), 1f) * 2f - 1f;
                    buf[i] = saw * 0.6f + Mathf.Sin(phase) * 0.4f;
                }
                LowPass(buf, 3600f);
                siren = Build("siren", buf, true);
                return siren;
            }
        }

        public static AudioClip Screech
        {
            get
            {
                if (screech != null) return screech;
                var buf = NewBuffer(0.9f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float wobble = 1f + 0.25f * Mathf.Sin(2f * Mathf.PI * 9f * t);
                    buf[i] = Noise() * wobble;
                }
                HighPass(buf, 1500f);
                LowPass(buf, 6500f);
                ApplyFade(buf, 0.05f);
                screech = Build("screech", buf, true);
                return screech;
            }
        }

        public static AudioClip Explosion
        {
            get
            {
                if (explosion != null) return explosion;
                var buf = NewBuffer(1.6f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Mathf.Exp(-t * 3.1f);
                    float sub = Mathf.Sin(2f * Mathf.PI * (55f - 20f * t) * t) * Mathf.Exp(-t * 2.4f);
                    buf[i] = Noise() * env * 1.3f + sub * 0.8f;
                }
                LowPass(buf, 2200f);
                ApplyFade(buf, 0.01f);
                explosion = Build("explosion", buf, false);
                return explosion;
            }
        }

        public static AudioClip Reload
        {
            get
            {
                if (reload != null) return reload;
                var buf = NewBuffer(0.5f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float click = 0f;
                    if (t < 0.06f) click += Noise() * Mathf.Exp(-t * 90f);
                    if (t > 0.22f && t < 0.34f) click += Noise() * Mathf.Exp(-(t - 0.22f) * 70f) * 0.9f;
                    buf[i] = click;
                }
                LowPass(buf, 4200f);
                reload = Build("reload", buf, false);
                return reload;
            }
        }

        public static AudioClip Cash
        {
            get
            {
                if (cash != null) return cash;
                var buf = NewBuffer(0.5f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Mathf.Exp(-t * 9f);
                    buf[i] = (Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.5f +
                              Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.35f +
                              Mathf.Sin(2f * Mathf.PI * 2360f * t) * 0.2f) * env;
                }
                ApplyFade(buf, 0.02f);
                cash = Build("cash", buf, false);
                return cash;
            }
        }

        public static AudioClip Horn
        {
            get
            {
                if (horn != null) return horn;
                var buf = NewBuffer(0.55f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    // two tone car horn: 400 Hz + 500 Hz square-ish, gated
                    float gate = (t < 0.42f) ? 1f : Mathf.Clamp01(1f - (t - 0.42f) * 12f);
                    float a = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 400f * t));
                    float b = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 505f * t));
                    buf[i] = (a * 0.35f + b * 0.3f) * gate * 0.6f;
                }
                LowPass(buf, 2200f);
                ApplyFade(buf, 0.01f);
                horn = Build("horn", buf, false);
                return horn;
            }
        }

        public static AudioClip Impact
        {
            get
            {
                if (impact != null) return impact;
                var buf = NewBuffer(0.18f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    buf[i] = (Noise() * 0.7f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.4f) * Mathf.Exp(-t * 40f);
                }
                LowPass(buf, 3000f);
                impact = Build("impact", buf, false);
                return impact;
            }
        }

        public static AudioClip Hit
        {
            get
            {
                if (hitSound != null) return hitSound;
                var buf = NewBuffer(0.22f);
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    buf[i] = (Noise() * 0.5f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.6f) * Mathf.Exp(-t * 22f);
                }
                LowPass(buf, 900f);
                hitSound = Build("hit", buf, false);
                return hitSound;
            }
        }

        public static AudioClip Ambience
        {
            get
            {
                if (ambience != null) return ambience;
                var buf = NewBuffer(4f);
                float phaseLow = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    phaseLow += 2f * Mathf.PI * (42f + 4f * Mathf.Sin(2f * Mathf.PI * t * 0.25f)) / SampleRate;
                    buf[i] = Mathf.Sin(phaseLow) * 0.5f + Noise() * 0.35f;
                }
                LowPass(buf, 380f);
                int fade = (int)(0.4f * SampleRate);
                for (int i = 0; i < fade; i++)
                {
                    float k = (float)i / fade;
                    buf[i] = Mathf.Lerp(buf[buf.Length - fade + i], buf[i], k);
                }
                ambience = Build("ambience", buf, true);
                return ambience;
            }
        }

        // ------------------------------------------------------------------ playback

        public static void InitAmbience(Transform parent)
        {
            if (ambienceSource != null) return;
            var go = new GameObject("AmbienceSource");
            go.transform.SetParent(parent, false);
            ambienceSource = go.AddComponent<AudioSource>();
            ambienceSource.clip = Ambience;
            ambienceSource.loop = true;
            ambienceSource.volume = 0.16f;
            ambienceSource.spatialBlend = 0f;
            ambienceSource.Play();
        }

        public static void SetAmbienceVolume(float v)
        {
            if (ambienceSource != null) ambienceSource.volume = Mathf.Clamp01(v);
        }

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("sfx_" + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = Mathf.Clamp(pitch, 0.4f, 2.4f);
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2.5f;
            src.maxDistance = 85f;
            src.dopplerLevel = 0.4f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.35f, src.pitch) + 0.25f);
        }

        public static AudioSource PlayLooping(AudioClip clip, Transform parent, float volume, float spatialBlend = 1f)
        {
            var go = new GameObject("loop_" + (clip != null ? clip.name : "null"));
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = Mathf.Clamp01(volume);
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = 70f;
            src.Play();
            return src;
        }
    }
}
