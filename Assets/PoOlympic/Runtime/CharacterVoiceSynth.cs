using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoOlympic
{
    public enum CharacterEmotion
    {
        Happy = 0,
        Angry = 1,
        Hurt = 2,
        Insult = 3,
    }

    /// <summary>
    /// Procedural formant + prosody vocal synthesizer for character voice placeholders and runtime fallbacks.
    /// Produces 4 emotions (HAPPY, ANGRY, HURT, INSULT) x 5 variants x 5 expressiveness levels (1=weakest..5=strongest),
    /// tailored per character profile (MATT, ZOMBIE, GRANDMA, KIM, or deterministic hash-derived profiles for new agents).
    /// </summary>
    public static class CharacterVoiceSynth
    {
        public const int EmotionCount = 4;
        public const int VariantsPerEmotion = 5;
        public const int ExpressivenessLevels = 5;
        public const int SoundsPerCharacter = EmotionCount * VariantsPerEmotion * ExpressivenessLevels;
        public const int SampleRate = 22050;

        public readonly struct VoiceProfile
        {
            public readonly float BaseF0;
            public readonly float FormantScale;
            public readonly float VibratoHz;
            public readonly float VibratoDepth;
            public readonly float Grit;
            public readonly float Breathiness;
            public readonly float NasalBoost;
            public readonly float TempoScale;

            public VoiceProfile(float baseF0, float formantScale, float vibratoHz, float vibratoDepth,
                                float grit, float breathiness, float nasalBoost, float tempoScale)
            {
                BaseF0 = baseF0;
                FormantScale = formantScale;
                VibratoHz = vibratoHz;
                VibratoDepth = vibratoDepth;
                Grit = grit;
                Breathiness = breathiness;
                NasalBoost = nasalBoost;
                TempoScale = tempoScale;
            }
        }

        readonly struct Syllable
        {
            public readonly float StartNorm;
            public readonly float EndNorm;
            public readonly float F0StartMul;
            public readonly float F0EndMul;
            public readonly float VowelA; // 0=/a/, 1=/i/, 2=/u/, 3=/o/, 4=/ae/
            public readonly float VowelB;

            public Syllable(float startNorm, float endNorm, float f0StartMul, float f0EndMul, float vowelA, float vowelB)
            {
                StartNorm = startNorm;
                EndNorm = endNorm;
                F0StartMul = f0StartMul;
                F0EndMul = f0EndMul;
                VowelA = vowelA;
                VowelB = vowelB;
            }
        }

        // Formant centre frequencies (F1, F2, F3 Hz) for 5 anchor vowels: /a/, /i/, /u/, /o/, /ae/
        static readonly float[] VowelsF1 = { 730f, 270f, 300f, 500f, 660f };
        static readonly float[] VowelsF2 = { 1090f, 2290f, 870f, 820f, 1720f };
        static readonly float[] VowelsF3 = { 2440f, 3010f, 2240f, 2400f, 2410f };

        static readonly Dictionary<(string id, CharacterEmotion emotion, int variant, int level), AudioClip> Cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Cache.Clear();

        public static string NormalizeId(string characterId) =>
            string.IsNullOrWhiteSpace(characterId) ? "MATT" : characterId.Trim().ToUpperInvariant();

        /// <summary>Distinct vocal tract and prosody profile for each character (deterministic fallback for new IDs).</summary>
        public static VoiceProfile ProfileOf(string characterId)
        {
            string id = NormalizeId(characterId);
            switch (id)
            {
                case "MATT":    return new VoiceProfile(138f, 1.00f, 5.2f, 0.018f, 0.14f, 0.08f, 0.10f, 1.00f);
                case "ZOMBIE":  return new VoiceProfile(84f,  0.84f, 3.8f, 0.036f, 0.58f, 0.34f, 0.22f, 1.18f);
                case "GRANDMA": return new VoiceProfile(214f, 1.14f, 7.1f, 0.052f, 0.24f, 0.22f, 0.30f, 1.08f);
                case "KIM":     return new VoiceProfile(258f, 1.22f, 5.8f, 0.022f, 0.08f, 0.10f, 0.14f, 0.90f);
                default:
                    uint h = 2166136261u;
                    for (int i = 0; i < id.Length; i++) h = (h ^ id[i]) * 16777619u;
                    float u0 = (h & 0xFF) / 255f;
                    float u1 = ((h >> 8) & 0xFF) / 255f;
                    float u2 = ((h >> 16) & 0xFF) / 255f;
                    return new VoiceProfile(
                        105f + u0 * 165f,
                        0.88f + u1 * 0.34f,
                        4.2f + u2 * 3.0f,
                        0.015f + u1 * 0.03f,
                        0.10f + u2 * 0.35f,
                        0.08f + u0 * 0.18f,
                        0.10f + u1 * 0.20f,
                        0.90f + u0 * 0.22f);
            }
        }

        public static AudioClip GetOrCreateClip(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = NormalizeId(characterId);
            int v = Mathf.Clamp(variant, 0, VariantsPerEmotion - 1);
            int lvl = Mathf.Clamp(expressiveness, 1, ExpressivenessLevels);
            var key = (id, emotion, v, lvl);
            if (Cache.TryGetValue(key, out var existing) && existing != null) return existing;

            float[] samples = GenerateSamples(id, emotion, v, lvl);
            var clip = AudioClip.Create($"ProcVoice_{id}_{emotion}_{v + 1}_L{lvl}", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Cache[key] = clip;
            return clip;
        }

        /// <summary>Synthesize mono float samples [-1..1] for (character, emotion, variant 0..4, expressiveness 1..5).</summary>
        public static float[] GenerateSamples(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            var prof = ProfileOf(characterId);
            int v = Mathf.Clamp(variant, 0, VariantsPerEmotion - 1);
            int lvl = Mathf.Clamp(expressiveness, 1, ExpressivenessLevels);
            float exp = (lvl - 1) / (float)(ExpressivenessLevels - 1); // 0.0 (weakest) .. 1.0 (strongest)

            (float baseDur, Syllable[] syls) = PhrasePattern(emotion, v);
            float dur = baseDur * prof.TempoScale * Mathf.Lerp(0.78f, 1.18f, exp);
            int n = Mathf.Max(SampleRate / 6, Mathf.RoundToInt(dur * SampleRate));
            var outSamples = new float[n];

            float pitchExcursion = Mathf.Lerp(0.38f, 1.72f, exp);
            float grit = Mathf.Clamp01(prof.Grit + exp * (emotion == CharacterEmotion.Angry ? 0.45f : emotion == CharacterEmotion.Hurt ? 0.30f : 0.15f));
            float breath = Mathf.Clamp01(prof.Breathiness + (emotion == CharacterEmotion.Hurt ? 0.22f : 0.08f) * (0.4f + 0.6f * exp));
            float targetPeak = Mathf.Lerp(0.32f, 0.94f, exp);

            int seed = StableSeed(NormalizeId(characterId), emotion, v, lvl);
            var rng = new System.Random(seed);

            // Three parallel biquad formant resonators
            var f1 = new Biquad();
            var f2 = new Biquad();
            var f3 = new Biquad();
            var nasal = new Biquad();

            float phase = 0f;
            float subPhase = 0f;
            float lpNoise = 0f;
            float envSmooth = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / (n - 1);

                if (!SamplePhrase(syls, u, out float sylU, out float f0Mul, out float vowelPos))
                {
                    envSmooth *= 0.992f;
                    outSamples[i] = 0f;
                    continue;
                }

                // Expressiveness scales pitch excursion away from neutral 1.0
                float scaledMul = 1f + (f0Mul - 1f) * pitchExcursion;
                float vib = Mathf.Sin(2f * Mathf.PI * prof.VibratoHz * t) * prof.VibratoDepth * Mathf.Lerp(0.4f, 1.8f, exp);
                float jitter = ((float)rng.NextDouble() * 2f - 1f) * grit * 0.03f;
                float f0 = Mathf.Clamp(prof.BaseF0 * scaledMul * (1f + vib + jitter), 45f, 900f);

                phase += f0 / SampleRate;
                if (phase >= 1f) phase -= 1f;
                subPhase += (0.5f * f0) / SampleRate;
                if (subPhase >= 1f) subPhase -= 1f;

                // Rosenberg-inspired asymmetric glottal pulse + harmonic buzz
                float glottal = phase < 0.42f
                    ? Mathf.Sin(Mathf.PI * phase / 0.42f)
                    : -0.35f * Mathf.Sin(Mathf.PI * (phase - 0.42f) / 0.58f);
                float saw = 1f - 2f * phase;
                float source = Mathf.Lerp(glottal, saw, 0.25f + 0.55f * exp);

                // Sub-harmonic growl for angry/hurt/zombie rasp
                if (grit > 0.05f)
                {
                    float sub = Mathf.Sin(2f * Mathf.PI * subPhase);
                    source += grit * 0.45f * sub * (0.5f + 0.5f * Mathf.Abs(source));
                }

                // Shaped aspiration / breath turbulence
                float white = (float)rng.NextDouble() * 2f - 1f;
                lpNoise += 0.35f * (white - lpNoise);
                source += breath * 0.45f * lpNoise;

                // Syllable amplitude envelope: punchier attack with higher expressiveness
                float attack = Mathf.Lerp(0.14f, 0.04f, exp);
                float sylEnv = sylU < attack ? sylU / attack : Mathf.Pow(Mathf.Clamp01((1f - sylU) / (1f - attack)), 0.65f);
                if (emotion == CharacterEmotion.Happy && v == 2) // staccato laugh pulse
                    sylEnv *= 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Mathf.PI * 3f * sylU));
                envSmooth = Mathf.Lerp(envSmooth, sylEnv, 0.08f);

                // Retune formants every 32 samples to keep CPU tiny while staying smooth
                if ((i & 31) == 0)
                {
                    EvalFormants(vowelPos, prof.FormantScale, exp, out float cf1, out float cf2, out float cf3);
                    float bw1 = Mathf.Lerp(95f, 65f, exp);
                    float bw2 = Mathf.Lerp(130f, 90f, exp);
                    float bw3 = Mathf.Lerp(180f, 130f, exp);
                    f1.SetBandpass(cf1, bw1, SampleRate);
                    f2.SetBandpass(cf2, bw2, SampleRate);
                    f3.SetBandpass(cf3, bw3, SampleRate);
                    nasal.SetBandpass(980f * prof.FormantScale, 150f, SampleRate);
                }

                float body = f1.Process(source) * 1.00f
                           + f2.Process(source) * Mathf.Lerp(0.55f, 0.85f, exp)
                           + f3.Process(source) * Mathf.Lerp(0.20f, 0.50f, exp);

                float nasalWeight = prof.NasalBoost + (emotion == CharacterEmotion.Insult ? 0.38f : 0f);
                body += nasal.Process(source) * nasalWeight;

                // Expressive waveshaping saturation
                float drive = 1f + grit * 2.2f + exp * 1.4f;
                float shaped = (float)Math.Tanh(body * drive);
                outSamples[i] = shaped * envSmooth;
            }

            // Normalize to target peak for expressiveness level 1..5, with 3 ms edge fades
            float maxAbs = 1e-5f;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Abs(outSamples[i]);
                if (a > maxAbs) maxAbs = a;
            }
            float gain = targetPeak / maxAbs;
            int fade = Mathf.Min(n / 8, (int)(SampleRate * 0.004f));
            for (int i = 0; i < n; i++)
            {
                float w = 1f;
                if (i < fade) w = (float)i / fade;
                else if (i >= n - fade) w = (float)(n - 1 - i) / fade;
                outSamples[i] = Mathf.Clamp(outSamples[i] * gain * w, -0.98f, 0.98f);
            }
            return outSamples;
        }

        static (float duration, Syllable[] syls) PhrasePattern(CharacterEmotion emotion, int variant)
        {
            switch (emotion)
            {
                case CharacterEmotion.Happy:
                    return variant switch
                    {
                        0 => (0.48f, new[] { new Syllable(0.00f, 0.42f, 1.00f, 1.24f, 2f, 0f), new Syllable(0.46f, 0.98f, 1.28f, 1.62f, 0f, 1f) }),
                        1 => (0.54f, new[] { new Syllable(0.00f, 0.30f, 1.00f, 1.05f, 0f, 0f), new Syllable(0.32f, 0.62f, 1.25f, 1.28f, 4f, 1f), new Syllable(0.65f, 0.98f, 1.50f, 1.58f, 1f, 1f) }),
                        2 => (0.52f, new[] { new Syllable(0.00f, 0.28f, 1.18f, 1.32f, 0f, 4f), new Syllable(0.33f, 0.62f, 1.25f, 1.40f, 0f, 4f), new Syllable(0.67f, 0.98f, 1.35f, 1.55f, 0f, 1f) }),
                        3 => (0.46f, new[] { new Syllable(0.00f, 0.96f, 0.95f, 1.68f, 2f, 1f) }),
                        _ => (0.44f, new[] { new Syllable(0.00f, 0.46f, 1.10f, 1.40f, 3f, 0f), new Syllable(0.52f, 0.98f, 1.35f, 1.20f, 0f, 3f) }),
                    };

                case CharacterEmotion.Angry:
                    return variant switch
                    {
                        0 => (0.50f, new[] { new Syllable(0.00f, 0.96f, 1.05f, 0.72f, 0f, 2f) }),
                        1 => (0.38f, new[] { new Syllable(0.00f, 0.44f, 1.26f, 0.85f, 4f, 0f), new Syllable(0.50f, 0.96f, 1.34f, 0.78f, 0f, 3f) }),
                        2 => (0.56f, new[] { new Syllable(0.00f, 0.30f, 0.92f, 0.82f, 3f, 2f), new Syllable(0.34f, 0.64f, 0.88f, 0.76f, 2f, 3f), new Syllable(0.68f, 0.98f, 0.95f, 0.70f, 0f, 2f) }),
                        3 => (0.46f, new[] { new Syllable(0.00f, 0.96f, 1.10f, 1.48f, 4f, 0f) }),
                        _ => (0.34f, new[] { new Syllable(0.00f, 0.95f, 1.20f, 0.68f, 0f, 3f) }),
                    };

                case CharacterEmotion.Hurt:
                    return variant switch
                    {
                        0 => (0.38f, new[] { new Syllable(0.00f, 0.94f, 1.45f, 0.74f, 0f, 3f) }),
                        1 => (0.46f, new[] { new Syllable(0.00f, 0.96f, 0.96f, 0.64f, 2f, 3f) }),
                        2 => (0.52f, new[] { new Syllable(0.00f, 0.42f, 1.28f, 0.95f, 1f, 4f), new Syllable(0.48f, 0.98f, 1.08f, 0.70f, 4f, 2f) }),
                        3 => (0.44f, new[] { new Syllable(0.00f, 0.36f, 1.15f, 0.88f, 3f, 0f), new Syllable(0.44f, 0.96f, 1.32f, 0.68f, 0f, 2f) }),
                        _ => (0.40f, new[] { new Syllable(0.00f, 0.95f, 1.05f, 0.58f, 2f, 0f) }),
                    };

                case CharacterEmotion.Insult:
                default:
                    return variant switch
                    {
                        0 => (0.54f, new[] { new Syllable(0.00f, 0.30f, 1.24f, 1.16f, 4f, 1f), new Syllable(0.34f, 0.64f, 1.06f, 1.00f, 4f, 1f), new Syllable(0.68f, 0.98f, 1.24f, 0.94f, 4f, 0f) }),
                        1 => (0.44f, new[] { new Syllable(0.00f, 0.44f, 1.22f, 0.86f, 1f, 4f), new Syllable(0.52f, 0.96f, 0.96f, 0.78f, 3f, 2f) }),
                        2 => (0.36f, new[] { new Syllable(0.00f, 0.38f, 1.32f, 1.04f, 4f, 0f), new Syllable(0.48f, 0.94f, 1.38f, 0.96f, 4f, 1f) }),
                        3 => (0.50f, new[] { new Syllable(0.00f, 0.96f, 1.35f, 0.82f, 1f, 4f) }),
                        _ => (0.52f, new[] { new Syllable(0.00f, 0.24f, 1.15f, 1.25f, 4f, 4f), new Syllable(0.26f, 0.50f, 1.25f, 1.10f, 1f, 4f), new Syllable(0.54f, 0.98f, 1.10f, 1.34f, 4f, 0f) }),
                    };
            }
        }

        static bool SamplePhrase(Syllable[] syls, float u, out float sylU, out float f0Mul, out float vowelPos)
        {
            for (int i = 0; i < syls.Length; i++)
            {
                var s = syls[i];
                if (u >= s.StartNorm && u <= s.EndNorm)
                {
                    sylU = Mathf.InverseLerp(s.StartNorm, s.EndNorm, u);
                    f0Mul = Mathf.Lerp(s.F0StartMul, s.F0EndMul, sylU);
                    vowelPos = Mathf.Lerp(s.VowelA, s.VowelB, sylU);
                    return true;
                }
            }
            sylU = 0f;
            f0Mul = 1f;
            vowelPos = 0f;
            return false;
        }

        static void EvalFormants(float vowelPos, float scale, float exp, out float f1, out float f2, out float f3)
        {
            float clamped = Mathf.Clamp(vowelPos, 0f, VowelsF1.Length - 1.001f);
            int idx = (int)clamped;
            float frac = clamped - idx;
            float brightness = Mathf.Lerp(0.92f, 1.12f, exp);
            f1 = Mathf.Lerp(VowelsF1[idx], VowelsF1[idx + 1], frac) * scale;
            f2 = Mathf.Lerp(VowelsF2[idx], VowelsF2[idx + 1], frac) * scale * brightness;
            f3 = Mathf.Lerp(VowelsF3[idx], VowelsF3[idx + 1], frac) * scale * brightness;
        }

        static int StableSeed(string id, CharacterEmotion emotion, int variant, int level)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < id.Length; i++) h = h * 31 + id[i];
                h = h * 31 + (int)emotion;
                h = h * 31 + variant;
                h = h * 31 + level;
                return h == 0 ? 1 : h;
            }
        }

        /// <summary>Write mono 16-bit PCM WAV bytes to a file path (creates parent directory).</summary>
        public static void WriteWavFile(string path, float[] samples, int sampleRate = SampleRate)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + samples.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(samples.Length * 2);
            for (int i = 0; i < samples.Length; i++)
                w.Write((short)Mathf.Clamp(samples[i] * 32767f, -32767f, 32767f));
        }

        struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public void SetBandpass(float freqHz, float bandwidthHz, int sampleRate)
            {
                float f = Mathf.Clamp(freqHz, 60f, sampleRate * 0.45f);
                float q = Mathf.Max(0.7f, f / Mathf.Max(20f, bandwidthHz));
                float w0 = 2f * Mathf.PI * f / sampleRate;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                _b0 = alpha / a0;
                _b1 = 0f;
                _b2 = -alpha / a0;
                _a1 = (-2f * Mathf.Cos(w0)) / a0;
                _a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
