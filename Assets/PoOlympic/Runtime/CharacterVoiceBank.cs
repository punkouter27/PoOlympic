using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Per-character voice library: 4 emotions (HAPPY, ANGRY, HURT, INSULT) x 5 sounds x 5 expressiveness levels (1..5).
    /// Any missing WAV slot transparently falls back to procedurally synthesized audio via <see cref="CharacterVoiceSynth"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "PoOlympic/Character Voice Bank", fileName = "CharacterVoiceBank")]
    public class CharacterVoiceBank : ScriptableObject
    {
        public const string ResourceName = "CharacterVoiceBank";

        [Serializable]
        public class CharacterSet
        {
            public string characterId = "";
            [Tooltip("100 slots = [emotion (0..3) * 25 + variant (0..4) * 5 + (expressiveness - 1) (0..4)].")]
            public AudioClip[] clips = new AudioClip[CharacterVoiceSynth.SoundsPerCharacter];
        }

        public List<CharacterSet> characters = new();

        static CharacterVoiceBank _default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _default = null;

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

        public bool HasWavClip(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
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

        /// <summary>Returns the character's WAV clip for (emotion, variant 0..4, expressiveness 1..5), or a procedural placeholder if absent.</summary>
        public AudioClip GetClip(string characterId, CharacterEmotion emotion, int variant, int expressiveness)
        {
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            int idx = SlotIndex(emotion, variant, expressiveness);
            for (int i = 0; i < characters.Count; i++)
            {
                if (string.Equals(characters[i].characterId, id, StringComparison.OrdinalIgnoreCase))
                {
                    var clips = characters[i].clips;
                    if (clips != null && idx < clips.Length && clips[idx] != null)
                        return clips[idx];
                    break;
                }
            }
            return CharacterVoiceSynth.GetOrCreateClip(id, emotion, variant, expressiveness);
        }
    }
}
