using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Per-character voice library: 5 emotions (HAPPY, ANGRY, HURT, INSULT, CELEBRATE) x 5 takes x 5 expressiveness
    /// levels (1..5). <see cref="manifest"/> (voices.json, <see cref="CharacterVoiceManifest"/>) is loaded once and says
    /// which takes are recorded: those play their WAV (bound in <see cref="CharacterSet.clips"/> by the generator,
    /// PoOlympic › Audio › Rebuild Voice Manifest); every other cell plays the advanced procedural imitation
    /// (<see cref="CharacterVoiceSynth"/>) with the athlete's manifest voice profile. No manifest (or an invalid one):
    /// procedural for everything, with one warning. WAV names: <see cref="RelativeWavPath"/>
    /// (e.g. <c>Characters/MATT/CELEBRATE/celebrate_01_L5.wav</c>).
    /// Slot layout: CELEBRATE is the last emotion, so older 100-slot sets keep their indices (<see cref="GetOrAddSet"/>
    /// grows them to 125).
    /// </summary>
    [CreateAssetMenu(menuName = "PoOlympic/Character Voice Bank", fileName = "CharacterVoiceBank")]
    public class CharacterVoiceBank : ScriptableObject
    {
        public const string ResourceName = "CharacterVoiceBank";

        [Serializable]
        public class CharacterSet
        {
            public string characterId = "";
            [Tooltip("125 slots = [emotion (0..4) * 25 + variant (0..4) * 5 + (expressiveness - 1) (0..4)].")]
            public AudioClip[] clips = new AudioClip[CharacterVoiceSynth.SoundsPerCharacter];
        }

        public List<CharacterSet> characters = new();
        [Tooltip("voices.json — which takes are recorded, and each athlete's procedural voice profile.")]
        public TextAsset manifest;

        static CharacterVoiceBank _default;
        [NonSerialized] CharacterVoiceManifest _manifest;
        [NonSerialized] bool _manifestLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _default = null;

        /// <summary>The parsed manifest (loaded once; registers every athlete's voice profile with the synth). Null when
        /// missing or invalid — then every cell is procedural.</summary>
        public CharacterVoiceManifest Manifest
        {
            get
            {
                if (_manifestLoaded) return _manifest;
                _manifestLoaded = true;
                _manifest = CharacterVoiceManifest.Parse(manifest != null ? manifest.text : null);
                if (_manifest == null)
                {
                    Debug.LogWarning("[CharacterVoiceBank] voices.json missing or invalid: every athlete voice is procedural.");
                    return null;
                }
                foreach (var c in _manifest.characters)
                    if (c?.voice != null && c.voice.Valid) CharacterVoiceSynth.RegisterProfile(c.name, c.voice.ToProfile());
                return _manifest;
            }
        }

        /// <summary>Forget the parsed manifest (the generator rewrote voices.json).</summary>
        public void ReloadManifest() { _manifestLoaded = false; _manifest = null; }

        public static CharacterVoiceBank Default
        {
            get
            {
                if (_default == null)
                    _default = Resources.Load<CharacterVoiceBank>(ResourceName) ?? CreateInstance<CharacterVoiceBank>();
                return _default;
            }
        }

        public static int SlotIndex(CharacterEmotion emotion, int variant, int expressiveness)
        {
            int e = Mathf.Clamp((int)emotion, 0, CharacterVoiceSynth.EmotionCount - 1);
            int v = Mathf.Clamp(variant, 0, CharacterVoiceSynth.VariantsPerEmotion - 1);
            int l = Mathf.Clamp(expressiveness, 1, CharacterVoiceSynth.ExpressivenessLevels) - 1;
            return e * (CharacterVoiceSynth.VariantsPerEmotion * CharacterVoiceSynth.ExpressivenessLevels)
                 + v * CharacterVoiceSynth.ExpressivenessLevels
                 + l;
        }

        /// <summary>WAV location under the Audio folder: <c>Characters/&lt;ID&gt;/&lt;EMOTION&gt;/&lt;emotion&gt;_&lt;variant 01..05&gt;_L&lt;level 1..5&gt;.wav</c>.</summary>
        public static string RelativeWavPath(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            string emUpper = emotion.ToString().ToUpperInvariant();
            string emLower = emotion.ToString().ToLowerInvariant();
            int v = Mathf.Clamp(variant, 0, CharacterVoiceSynth.VariantsPerEmotion - 1) + 1;
            int l = Mathf.Clamp(expressiveness, 1, CharacterVoiceSynth.ExpressivenessLevels);
            return $"Characters/{id}/{emUpper}/{emLower}_{v:00}_L{l}.wav";
        }

        public CharacterSet GetOrAddSet(string characterId)
        {
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            for (int i = 0; i < characters.Count; i++)
            {
                if (string.Equals(characters[i].characterId, id, StringComparison.OrdinalIgnoreCase))
                {
                    if (characters[i].clips == null || characters[i].clips.Length != CharacterVoiceSynth.SoundsPerCharacter)
                        Array.Resize(ref characters[i].clips, CharacterVoiceSynth.SoundsPerCharacter);
                    return characters[i];
                }
            }
            var created = new CharacterSet { characterId = id };
            characters.Add(created);
            return created;
        }

        /// <summary>A recorded WAV plays for this cell: the manifest says it exists and the clip is bound.</summary>
        public bool HasWavClip(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            var m = Manifest;
            if (m == null || !m.Exists(characterId, emotion, Mathf.Clamp(variant, 0, CharacterVoiceSynth.VariantsPerEmotion - 1),
                                       Mathf.Clamp(expressiveness, 1, CharacterVoiceSynth.ExpressivenessLevels))) return false;
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            for (int i = 0; i < characters.Count; i++)
            {
                if (string.Equals(characters[i].characterId, id, StringComparison.OrdinalIgnoreCase))
                {
                    int idx = SlotIndex(emotion, variant, expressiveness);
                    var clips = characters[i].clips;
                    return clips != null && idx < clips.Length && clips[idx] != null;
                }
            }
            return false;
        }

        /// <summary>The athlete's clip for (emotion, take 0..4, level 1..5): the recorded WAV when voices.json says it
        /// exists, else the procedural imitation (never a file the manifest calls missing).</summary>
        public AudioClip GetClip(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            if (HasWavClip(id, emotion, variant, expressiveness))
            {
                int idx = SlotIndex(emotion, variant, expressiveness);
                foreach (var set in characters)
                    if (string.Equals(set.characterId, id, StringComparison.OrdinalIgnoreCase))
                        return set.clips[idx];
            }
            else _ = Manifest;                                   // registers the manifest's voice profiles first
            return CharacterVoiceSynth.GetOrCreateClip(id, emotion, variant, expressiveness);
        }
    }
}
