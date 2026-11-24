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
        /// <summary>Victory whoop (event win, medal, series win). Appended last so the slots of the first four
        /// emotions keep their indices in saved <see cref="CharacterVoiceBank"/> assets.</summary>
        Celebrate = 4,
    }

    /// <summary>
    /// Advanced procedural vocal imitation: what plays for every (athlete, emotion, take, level) cell that has no
    /// recorded WAV (voices.json says exists = false). Not a beep: a short human vocalisation by THAT athlete.
    ///   source     Rosenberg glottal flow pulse (differentiated, i.e. radiated) at the profile's pitch, with
    ///              per-period jitter (~0.5-1.5 %) and shimmer (~3-6 %), pitch-synchronous aspiration mixed by breath,
    ///              rasp = sub-harmonic period doubling + rough amplitude modulation
    ///   filter     cascade of 4 Klatt formant resonators; vowel targets a e i o u schwa scaled by formantScale,
    ///              gliding across each syllable; insult raises F2 and adds a nasal resonance (sneer)
    ///   syllables  1-4 per phrase with consonant onsets / codas: noise for h / s / sh / f, plosive bursts for
    ///              k / t / p, a nasal murmur for n, a 25 Hz trill for rr, an inhaled gasp
    ///   prosody    per emotion: angry tense, falling, rasped, hard onsets ("rraah", "hey!"); happy bright e/i,
    ///              rise-fall, light vibrato, laughs at higher levels ("heh", "ha-ha"); hurt gasp then groan with a
    ///              downward break, breathy, level 5 a long cry ("oof", "ugh"); insult sing-song nasal taunt
    ///              ("nyah-nyah", "pfft-heh"); celebrate rising whoop with a held, wide-vibrato last vowel, laughs at
    ///              higher levels ("woo-hoo!", "yeah!")
    ///   levels     1..5 scale loudness (peak), length, pitch range, rasp, breath, syllable count and vibrato
    /// Deterministic (seed = hash(athlete, emotion, take, level)), cached per cell. The athlete's profile comes from
    /// voices.json (<see cref="RegisterProfile"/>), else the built-in table / an id-hash profile for new athletes.
    /// 5 emotions x 5 takes ("variants") x 5 levels = 125 cells per athlete.
    /// </summary>
    public static class CharacterVoiceSynth
    {
        public const int EmotionCount = 5;
        public const int VariantsPerEmotion = 5;
        public const int ExpressivenessLevels = 5;
        public const int SoundsPerCharacter = EmotionCount * VariantsPerEmotion * ExpressivenessLevels;
        public const int SampleRate = 22050;
        /// <summary>Written into every WAV this synth bakes (RIFF LIST/INFO ISFT): such a file is a placeholder, never
        /// a recording, so the voice manifest keeps exists = false for it.</summary>
        public const string PlaceholderMarker = "PoOlympic CharacterVoiceSynth placeholder";

        public readonly struct VoiceProfile
        {
            public readonly float BaseF0;        // pitchHz
            public readonly float FormantScale;  // vocal tract length (1 = adult male)
            public readonly float VibratoHz;
            public readonly float VibratoDepth;
            public readonly float Grit;          // rasp
            public readonly float Breathiness;   // breath
            public readonly float NasalBoost;
            public readonly float TempoScale;    // > 1 = slower
            public readonly string Timbre;

            public VoiceProfile(float baseF0, float formantScale, float vibratoHz, float vibratoDepth,
                                float grit, float breathiness, float nasalBoost, float tempoScale, string timbre = "")
            {
                BaseF0 = baseF0;
                FormantScale = formantScale;
                VibratoHz = vibratoHz;
                VibratoDepth = vibratoDepth;
                Grit = grit;
                Breathiness = breathiness;
                NasalBoost = nasalBoost;
                TempoScale = tempoScale;
                Timbre = timbre ?? "";
            }
        }

        // ------------------------------------------------------------------------------------------ profiles
        static readonly Dictionary<string, VoiceProfile> Overrides = new();
        static readonly Dictionary<(string id, CharacterEmotion emotion, int variant, int level), AudioClip> Cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); Overrides.Clear(); }

        public static string NormalizeId(string characterId) =>
            string.IsNullOrWhiteSpace(characterId) ? "MATT" : characterId.Trim().ToUpperInvariant();

        /// <summary>Use this profile for the athlete (voices.json "voice"); drops the athlete's cached clips.</summary>
        public static void RegisterProfile(string characterId, VoiceProfile profile)
        {
            string id = NormalizeId(characterId);
            Overrides[id] = profile;
            var stale = new List<(string, CharacterEmotion, int, int)>();
            foreach (var k in Cache.Keys) if (k.id == id) stale.Add(k);
            foreach (var k in stale) Cache.Remove(k);
        }

        /// <summary>The athlete's voice: voices.json profile if registered, else the built-in table (the source of the
        /// manifest's first profiles), else a deterministic hash-derived profile for a new athlete.</summary>
        public static VoiceProfile ProfileOf(string characterId)
        {
            string id = NormalizeId(characterId);
            if (Overrides.TryGetValue(id, out var o)) return o;
            return BuiltInProfile(id);
        }

        public static VoiceProfile BuiltInProfile(string characterId)
        {
            string id = NormalizeId(characterId);
            switch (id)
            {
                case "MATT":    return new VoiceProfile(138f, 1.00f, 5.2f, 0.018f, 0.14f, 0.08f, 0.10f, 1.00f, "adult-male");
                case "ZOMBIE":  return new VoiceProfile(84f,  0.84f, 3.8f, 0.036f, 0.58f, 0.34f, 0.22f, 1.18f, "undead-growl");
                case "GRANDMA": return new VoiceProfile(214f, 1.14f, 7.1f, 0.052f, 0.24f, 0.22f, 0.30f, 1.08f, "elderly-female");
                case "KIM":     return new VoiceProfile(258f, 1.22f, 5.8f, 0.022f, 0.08f, 0.10f, 0.14f, 0.90f, "young-female");
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
                        0.90f + u0 * 0.22f,
                        u0 > 0.5f ? "adult-female" : "adult-male");
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

        // ------------------------------------------------------------------------------------------ phrases
        enum Onset { None, H, S, Sh, F, K, T, P, N, Rr, Gasp }

        [Flags]
        enum SylFlags { None = 0, Trill = 1, Hold = 2, Laugh = 4, Nasal = 8, Break = 16 }

        // vowels: a e i o u schwa
        const int A = 0, E = 1, I = 2, O = 3, U = 4, X = 5;
        static readonly float[] VF1 = { 730f, 530f, 270f, 570f, 300f, 500f };
        static readonly float[] VF2 = { 1090f, 1840f, 2290f, 840f, 870f, 1500f };
        static readonly float[] VF3 = { 2440f, 2480f, 3010f, 2410f, 2240f, 2500f };

        readonly struct Syl
        {
            public readonly Onset On, Coda;
            public readonly int V1, V2;
            public readonly float Dur, F0s, F0e;
            public readonly SylFlags Flags;
            public Syl(Onset on, int v1, int v2, float dur, float f0s, float f0e, SylFlags flags = SylFlags.None, Onset coda = Onset.None)
            { On = on; V1 = v1; V2 = v2; Dur = dur; F0s = f0s; F0e = f0e; Flags = flags; Coda = coda; }
        }

        static Syl S(Onset on, int v1, int v2, float dur, float f0s, float f0e, SylFlags f = SylFlags.None, Onset coda = Onset.None)
            => new(on, v1, v2, dur, f0s, f0e, f, coda);

        /// <summary>The phrase for (emotion, take, level): the take's template, plus the level's extra syllables.</summary>
        static List<Syl> Phrase(CharacterEmotion emotion, int take, int level)
        {
            var p = new List<Syl>();
            switch (emotion)
            {
                case CharacterEmotion.Angry:      // tense, falling, hard onsets
                    p.AddRange(take switch
                    {
                        0 => new[] { S(Onset.Rr, A, A, 0.38f, 1.15f, 0.78f, SylFlags.Trill) },                              // rraah
                        1 => new[] { S(Onset.K, X, X, 0.14f, 0.95f, 0.90f, SylFlags.Trill), S(Onset.None, A, A, 0.26f, 1.25f, 0.80f) }, // grr-AH
                        2 => new[] { S(Onset.None, A, O, 0.34f, 1.10f, 0.74f, SylFlags.Trill, Onset.H) },                 // argh
                        3 => new[] { S(Onset.H, E, I, 0.22f, 1.30f, 0.95f) },                                             // hey!
                        _ => new[] { S(Onset.None, X, X, 0.12f, 1.00f, 0.90f), S(Onset.H, A, A, 0.30f, 1.20f, 0.75f, SylFlags.None, Onset.P) }, // ugh-hap!
                    });
                    if (level >= 4) p.Add(S(Onset.Rr, A, X, 0.22f, 1.10f, 0.70f, SylFlags.Trill));
                    break;

                case CharacterEmotion.Happy:      // bright vowels, rise-fall, laughs
                    p.AddRange(take switch
                    {
                        0 => new[] { S(Onset.H, E, E, 0.18f, 1.05f, 1.20f) },                                             // heh
                        1 => new[] { S(Onset.H, A, A, 0.14f, 1.15f, 1.20f, SylFlags.Laugh), S(Onset.H, A, A, 0.16f, 1.20f, 1.05f, SylFlags.Laugh) }, // ha-ha
                        2 => new[] { S(Onset.None, I, E, 0.16f, 1.00f, 1.35f), S(Onset.None, E, I, 0.18f, 1.35f, 1.10f) }, // yay
                        3 => new[] { S(Onset.H, I, I, 0.13f, 1.30f, 1.35f, SylFlags.Laugh), S(Onset.H, I, I, 0.15f, 1.35f, 1.20f, SylFlags.Laugh) }, // hee-hee
                        _ => new[] { S(Onset.None, O, O, 0.16f, 1.00f, 1.20f), S(Onset.H, O, O, 0.20f, 1.30f, 1.05f) },  // oh-ho!
                    });
                    for (int k = 3; k < level; k++) p.Add(S(Onset.H, A, A, 0.12f, 1.25f, 1.15f, SylFlags.Laugh));
                    break;

                case CharacterEmotion.Hurt:       // gasp, groan, downward break, breathy
                    p.AddRange(take switch
                    {
                        0 => new[] { S(Onset.None, U, U, 0.20f, 1.20f, 0.70f, SylFlags.Break, Onset.F) },                // oof
                        1 => new[] { S(Onset.None, X, X, 0.26f, 1.10f, 0.70f, SylFlags.Break, Onset.H) },                // ugh
                        2 => new[] { S(Onset.Gasp, A, X, 0.32f, 1.30f, 0.65f, SylFlags.Break) },                          // (gasp) ahh
                        3 => new[] { S(Onset.None, A, U, 0.28f, 1.35f, 0.80f, SylFlags.Break) },                          // ow
                        _ => new[] { S(Onset.N, X, U, 0.30f, 0.95f, 0.70f, SylFlags.Nasal | SylFlags.Break) },            // nngh
                    });
                    if (level >= 5) p.Add(S(Onset.Gasp, A, X, 0.60f, 1.45f, 0.62f, SylFlags.Break | SylFlags.Hold));   // long cry
                    else if (level >= 4) p.Add(S(Onset.None, X, U, 0.22f, 0.90f, 0.70f));                               // groan tail
                    break;

                case CharacterEmotion.Insult:     // sing-song, sneering nasal
                    p.AddRange(take switch
                    {
                        0 => new[] { S(Onset.N, A, A, 0.18f, 1.25f, 1.25f, SylFlags.Nasal), S(Onset.N, A, A, 0.24f, 1.00f, 1.00f, SylFlags.Nasal) }, // nyah-nyah
                        1 => new[] { S(Onset.P, U, U, 0.05f, 1.00f, 1.00f, SylFlags.None, Onset.F), S(Onset.H, E, E, 0.18f, 1.20f, 1.00f, SylFlags.Nasal) }, // pfft-heh
                        2 => new[] { S(Onset.None, U, U, 0.20f, 1.20f, 1.25f, SylFlags.Nasal), S(Onset.S, X, X, 0.22f, 1.00f, 0.85f, SylFlags.Nasal) }, // loo-ser
                        3 => new[] { S(Onset.N, A, A, 0.14f, 1.25f, 1.25f, SylFlags.Nasal), S(Onset.N, A, A, 0.14f, 1.00f, 1.00f, SylFlags.Nasal), S(Onset.N, A, A, 0.20f, 1.25f, 1.10f, SylFlags.Nasal) }, // na-na-na
                        _ => new[] { S(Onset.H, A, A, 0.16f, 1.30f, 1.10f, SylFlags.Nasal), S(Onset.N, O, U, 0.22f, 1.00f, 0.80f, SylFlags.Nasal, Onset.P) }, // hah, nope
                    });
                    if (level >= 4) p.Add(S(Onset.N, A, A, 0.18f, 1.25f, 1.05f, SylFlags.Nasal));
                    break;

                default:                          // celebrate: rising whoop, held wide-vibrato last vowel, laughs
                    p.AddRange(take switch
                    {
                        0 => new[] { S(Onset.None, I, E, 0.12f, 1.00f, 1.20f), S(Onset.None, E, A, 0.50f, 1.25f, 1.60f, SylFlags.Hold) }, // yeah!
                        1 => new[] { S(Onset.None, U, U, 0.20f, 1.05f, 1.25f), S(Onset.H, U, U, 0.50f, 1.35f, 1.75f, SylFlags.Hold) },   // woo-hoo!
                        2 => new[] { S(Onset.None, I, I, 0.12f, 1.10f, 1.15f), S(Onset.T, I, I, 0.12f, 1.20f, 1.25f), S(Onset.T, I, I, 0.45f, 1.40f, 1.70f, SylFlags.Hold) }, // we did it!
                        3 => new[] { S(Onset.H, A, A, 0.14f, 1.20f, 1.30f), S(Onset.H, A, A, 0.55f, 1.40f, 1.80f, SylFlags.Hold) },      // ha-HAA
                        _ => new[] { S(Onset.None, E, E, 0.12f, 1.10f, 1.15f, SylFlags.None, Onset.S), S(Onset.None, E, E, 0.12f, 1.20f, 1.25f, SylFlags.None, Onset.S), S(Onset.None, E, E, 0.45f, 1.45f, 1.80f, SylFlags.Hold, Onset.S) }, // yes-yes-YES!
                    });
                    for (int k = 3; k < level; k++) p.Add(S(Onset.H, A, A, 0.12f, 1.30f, 1.20f, SylFlags.Laugh));
                    break;
            }
            return p;
        }

        // ------------------------------------------------------------------------------------------ synthesis
        /// <summary>Synthesize mono float samples [-1..1] for (athlete, emotion, take 0..4, level 1..5).</summary>
        public static float[] GenerateSamples(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = NormalizeId(characterId);
            return GenerateSamples(ProfileOf(id), id, emotion, variant, expressiveness);
        }

        public static float[] GenerateSamples(VoiceProfile prof, string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = NormalizeId(characterId);
            int take = Mathf.Clamp(variant, 0, VariantsPerEmotion - 1);
            int lvl = Mathf.Clamp(expressiveness, 1, ExpressivenessLevels);
            float x = (lvl - 1) / (float)(ExpressivenessLevels - 1);         // 0 weakest .. 1 strongest
            var rng = new System.Random(StableSeed(id, emotion, take, lvl));
            float Rnd() => (float)rng.NextDouble();
            float Gauss() { double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble(); return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)); }

            bool angry = emotion == CharacterEmotion.Angry, hurt = emotion == CharacterEmotion.Hurt,
                 insult = emotion == CharacterEmotion.Insult, celebrate = emotion == CharacterEmotion.Celebrate,
                 happy = emotion == CharacterEmotion.Happy;

            // level scaling + small, deterministic take variation
            float lengthScale = prof.TempoScale * Mathf.Lerp(0.80f, 1.30f, x) * (1f + (Rnd() - 0.5f) * 0.12f);
            float pitchRange = Mathf.Lerp(0.50f, 1.60f, x);
            float pitchBase = prof.BaseF0 * (1f + (Rnd() - 0.5f) * 0.08f) * (celebrate ? Mathf.Lerp(1.04f, 1.18f, x) : angry ? 1.06f : 1f);
            float rasp = Mathf.Clamp01(prof.Grit + x * (angry ? 0.45f : hurt ? 0.25f : celebrate ? 0.06f : 0.12f));
            float breath = Mathf.Clamp01(prof.Breathiness + (hurt ? 0.25f : 0.06f) * (0.4f + 0.6f * x));
            float vibDepth = prof.VibratoDepth * Mathf.Lerp(0.5f, 1.8f, x) * (happy ? 1.2f : 1f);
            float jitterAmt = Mathf.Lerp(0.005f, 0.015f, Mathf.Clamp01(rasp + 0.3f * x));
            float shimmerAmt = Mathf.Lerp(0.03f, 0.06f, Mathf.Clamp01(rasp + 0.3f * x));
            float targetPeak = Mathf.Lerp(0.32f, 0.94f, x);
            float gap = (angry ? 0.035f : insult ? 0.06f : 0.05f) * prof.TempoScale;

            var phrase = Phrase(emotion, take, lvl);
            // layout in seconds: [onset][voiced][coda] gap ...
            var segs = new List<(Syl s, float t0, float onset, float voiced, float coda)>();
            float t = 0.01f;
            foreach (var s in phrase)
            {
                float on = OnsetSeconds(s.On) * prof.TempoScale;
                float v = Mathf.Max(0.04f, s.Dur * lengthScale * (s.Flags.HasFlag(SylFlags.Hold) ? Mathf.Lerp(0.9f, 1.3f, x) : 1f));
                float co = OnsetSeconds(s.Coda) * prof.TempoScale * 0.8f;
                segs.Add((s, t, on, v, co));
                t += on + v + co + gap * (s.Flags.HasFlag(SylFlags.Laugh) ? 0.6f : 1f);
            }
            int n = Mathf.Max(SampleRate / 6, Mathf.CeilToInt((t + 0.03f) * SampleRate));
            var outS = new float[n];
            float dt = 1f / SampleRate;

            var f1 = new Resonator(); var f2 = new Resonator(); var f3 = new Resonator(); var f4 = new Resonator();
            var nasal = new Resonator(); var noiseBp = new Resonator();
            float phase = 0f, periodAmp = 1f, periodJit = 0f, lastFlow = 0f, lp = 0f;
            int periodIndex = 0;

            foreach (var (s, t0, on, voiced, co) in segs)
            {
                int i0 = Mathf.RoundToInt(t0 * SampleRate);
                // ---- onset consonant
                RenderConsonant(outS, i0, Mathf.RoundToInt(on * SampleRate), s.On, prof.FormantScale, rng, ref noiseBp, 0.55f + 0.35f * x, angry);
                // ---- voiced nucleus
                int vStart = i0 + Mathf.RoundToInt(on * SampleRate);
                int vLen = Mathf.RoundToInt(voiced * SampleRate);
                bool trill = s.Flags.HasFlag(SylFlags.Trill), hold = s.Flags.HasFlag(SylFlags.Hold), laugh = s.Flags.HasFlag(SylFlags.Laugh);
                bool nasalSyl = insult || s.Flags.HasFlag(SylFlags.Nasal);
                float attack = angry ? 0.012f : laugh ? 0.008f : Mathf.Lerp(0.05f, 0.02f, x);
                for (int j = 0; j < vLen && vStart + j < n; j++)
                {
                    float u = j / (float)Mathf.Max(1, vLen - 1);
                    float tt = (vStart + j) * dt;
                    // pitch contour: emotion shape scaled by level; hurt breaks downward at 60 %
                    float mul = Mathf.Lerp(s.F0s, s.F0e, happy || celebrate ? Mathf.SmoothStep(0f, 1f, u) : u);
                    if (s.Flags.HasFlag(SylFlags.Break) && u > 0.6f) mul *= 0.82f;
                    mul = 1f + (mul - 1f) * pitchRange;
                    float vib = Mathf.Sin(2f * Mathf.PI * prof.VibratoHz * tt) * vibDepth * (hold ? 2.2f * Mathf.Clamp01((u - 0.2f) * 2f) : 1f);
                    float f0 = Mathf.Clamp(pitchBase * mul * (1f + vib + periodJit), 45f, 1100f);

                    // glottal period bookkeeping: jitter / shimmer / sub-harmonic per period
                    phase += f0 * dt;
                    if (phase >= 1f)
                    {
                        phase -= 1f;
                        periodIndex++;
                        periodJit = Gauss() * jitterAmt;
                        periodAmp = 1f + Gauss() * shimmerAmt;
                        if (rasp > 0.05f && (periodIndex & 1) == 1) periodAmp *= 1f - 0.55f * rasp;     // period doubling
                    }
                    // Rosenberg flow pulse, differentiated (lip radiation)
                    const float tp = 0.40f, tn = 0.16f;
                    float flow = phase < tp ? 0.5f * (1f - Mathf.Cos(Mathf.PI * phase / tp))
                               : phase < tp + tn ? Mathf.Cos(Mathf.PI * (phase - tp) / (2f * tn)) : 0f;
                    float src = (flow - lastFlow) * SampleRate / Mathf.Max(60f, f0) * 0.35f * periodAmp;
                    lastFlow = flow;
                    // aspiration: pitch-synchronous noise, louder in the open phase
                    float white = Rnd() * 2f - 1f;
                    lp += 0.4f * (white - lp);
                    src += breath * 0.5f * lp * (0.35f + flow);
                    // rough AM for rasp, 25 Hz trill for "rr"
                    if (rasp > 0.05f) src *= 1f + rasp * 0.35f * (Rnd() * 2f - 1f);
                    if (trill && u < 0.45f) src *= 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 25f * tt);

                    if ((j & 15) == 0)
                    {
                        float vp = Mathf.Lerp(s.V1, s.V2, Mathf.SmoothStep(0f, 1f, u));
                        int a = Mathf.Clamp((int)vp, 0, 5), b = Mathf.Min(5, a + 1);
                        float fr = vp - a;
                        if (s.V1 == s.V2) { a = b = s.V1; fr = 0f; }
                        float sc = prof.FormantScale * Mathf.Lerp(0.97f, 1.06f, x);
                        float F1 = Mathf.Lerp(VF1[a], VF1[b], fr) * sc * (angry ? 1.08f : 1f);
                        float F2 = Mathf.Lerp(VF2[a], VF2[b], fr) * sc * (nasalSyl ? 1.15f : happy || celebrate ? 1.05f : 1f);
                        float F3 = Mathf.Lerp(VF3[a], VF3[b], fr) * sc;
                        f1.Set(F1, Mathf.Lerp(90f, 60f, x));
                        f2.Set(F2, Mathf.Lerp(120f, 85f, x));
                        f3.Set(F3, Mathf.Lerp(170f, 120f, x));
                        f4.Set(3500f * sc, 250f);
                        nasal.Set(1000f * prof.FormantScale, 160f);
                    }
                    float y = f4.Run(f3.Run(f2.Run(f1.Run(src))));
                    if (nasalSyl) y += nasal.Run(src) * (prof.NasalBoost + 0.35f);
                    // syllable envelope (+ laugh pulse)
                    float env = u * vLen * dt < attack ? (u * vLen * dt) / attack : Mathf.Pow(Mathf.Clamp01((1f - u) / 0.85f), angry ? 0.45f : 0.7f);
                    if (laugh) env *= 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Mathf.PI * 2f * u));
                    float drive = 1f + rasp * 1.8f + x * 1.2f;
                    outS[vStart + j] += (float)Math.Tanh(y * drive) * env;
                }
                // ---- coda consonant
                RenderConsonant(outS, vStart + vLen, Mathf.RoundToInt(co * SampleRate), s.Coda, prof.FormantScale, rng, ref noiseBp, 0.4f + 0.3f * x, angry);
            }

            // peak-normalise to the level's loudness (procedural and recorded clips sit at similar loudness), 4 ms fades
            float maxAbs = 1e-5f;
            for (int i = 0; i < n; i++) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(outS[i]));
            float gain = targetPeak / maxAbs;
            int fade = Mathf.Min(n / 8, (int)(SampleRate * 0.004f));
            for (int i = 0; i < n; i++)
            {
                float w = i < fade ? (float)i / fade : i >= n - fade ? (float)(n - 1 - i) / fade : 1f;
                outS[i] = Mathf.Clamp(outS[i] * gain * w, -0.98f, 0.98f);
            }
            return outS;
        }

        static float OnsetSeconds(Onset o) => o switch
        {
            Onset.H => 0.05f, Onset.S => 0.08f, Onset.Sh => 0.08f, Onset.F => 0.07f,
            Onset.K => 0.035f, Onset.T => 0.03f, Onset.P => 0.03f, Onset.N => 0.045f,
            Onset.Rr => 0.05f, Onset.Gasp => 0.12f, _ => 0f,
        };

        /// <summary>Unvoiced / murmured consonant into out[i0 .. i0+len): noise for h s sh f, a burst for k t p, a low
        /// nasal murmur for n, a trilled buzz for rr, an inhaled gasp (rising noise).</summary>
        static void RenderConsonant(float[] outS, int i0, int len, Onset o, float formantScale, System.Random rng, ref Resonator bp, float level, bool hard)
        {
            if (o == Onset.None || len <= 0) return;
            (float fc, float bw, float amp) = o switch
            {
                Onset.H => (1500f * formantScale, 1800f, 0.25f),
                Onset.S => (5200f, 2200f, 0.30f),
                Onset.Sh => (2600f, 1200f, 0.35f),
                Onset.F => (3800f, 3000f, 0.15f),
                Onset.K => (1800f, 900f, 0.70f),
                Onset.T => (4200f, 1800f, 0.65f),
                Onset.P => (800f, 900f, 0.65f),
                Onset.N => (280f * formantScale, 120f, 0.30f),
                Onset.Rr => (600f * formantScale, 400f, 0.35f),
                _ => (1200f * formantScale, 1500f, 0.22f),            // gasp
            };
            bp.Set(Mathf.Min(fc, SampleRate * 0.45f), bw);
            bool plosive = o == Onset.K || o == Onset.T || o == Onset.P;
            for (int j = 0; j < len && i0 + j < outS.Length; j++)
            {
                float u = j / (float)Mathf.Max(1, len - 1);
                float src;
                if (o == Onset.N) src = Mathf.Sin(2f * Mathf.PI * 140f * j / SampleRate);               // nasal murmur
                else if (o == Onset.Rr) src = ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 25f * j / SampleRate));
                else src = (float)rng.NextDouble() * 2f - 1f;
                float env = plosive ? (u < 0.35f ? 0f : Mathf.Exp(-(u - 0.35f) * (hard ? 14f : 9f)))   // closure, then burst
                          : o == Onset.Gasp ? Mathf.Sin(Mathf.PI * u) * (0.4f + 0.6f * u)
                          : Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.2f));
                outS[i0 + j] += bp.Run(src) * amp * level * env * 4f;
            }
        }

        /// <summary>Klatt two-pole resonator (unity gain at DC-normalised centre).</summary>
        struct Resonator
        {
            float _a, _b, _c, _y1, _y2;

            public void Set(float freqHz, float bandwidthHz)
            {
                float T = 1f / SampleRate;
                float f = Mathf.Clamp(freqHz, 60f, SampleRate * 0.45f);
                _c = -Mathf.Exp(-2f * Mathf.PI * bandwidthHz * T);
                _b = 2f * Mathf.Exp(-Mathf.PI * bandwidthHz * T) * Mathf.Cos(2f * Mathf.PI * f * T);
                _a = 1f - _b - _c;
            }

            public float Run(float x)
            {
                float y = _a * x + _b * _y1 + _c * _y2;
                _y2 = _y1; _y1 = y;
                return y;
            }
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

        // ------------------------------------------------------------------------------------------ WAV I/O
        /// <summary>Write mono 16-bit PCM WAV (creates the parent directory). Every file written here carries
        /// <see cref="PlaceholderMarker"/> in a LIST/INFO chunk: it is synth output, never a recording.</summary>
        public static void WriteWavFile(string path, float[] samples, int sampleRate = SampleRate)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            byte[] text = System.Text.Encoding.ASCII.GetBytes(PlaceholderMarker + "\0");
            int textLen = text.Length + (text.Length & 1);
            int listLen = 4 + 8 + textLen;                                        // "INFO" + ISFT chunk
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(4 + (8 + 16) + (8 + listLen) + (8 + samples.Length * 2));
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
            w.Write(listLen);
            w.Write(System.Text.Encoding.ASCII.GetBytes("INFOISFT"));
            w.Write(text.Length);
            w.Write(text);
            if ((text.Length & 1) == 1) w.Write((byte)0);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(samples.Length * 2);
            for (int i = 0; i < samples.Length; i++)
                w.Write((short)Mathf.Clamp(samples[i] * 32767f, -32767f, 32767f));
        }

        /// <summary>True when the WAV bytes carry <see cref="PlaceholderMarker"/> (baked by this synth).</summary>
        public static bool IsPlaceholderWav(byte[] wav)
        {
            if (wav == null || wav.Length < 12) return false;
            byte[] m = System.Text.Encoding.ASCII.GetBytes(PlaceholderMarker);
            int limit = Math.Min(wav.Length, 4096) - m.Length;                    // the marker sits before "data"
            for (int i = 12; i <= limit; i++)
            {
                int k = 0;
                while (k < m.Length && wav[i + k] == m[k]) k++;
                if (k == m.Length) return true;
            }
            return false;
        }
    }
}
