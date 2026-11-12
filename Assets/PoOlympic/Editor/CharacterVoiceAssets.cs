using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PoOlympic.Editor
{
    /// <summary>
    /// Generates placeholder WAV files (HAPPY, ANGRY, HURT, INSULT x 5 sounds x 5 expressiveness levels) for every
    /// character in <see cref="MeetLineup.Roster"/> when a WAV does not yet exist, and populates
    /// <c>Assets/PoOlympic/Resources/CharacterVoiceBank.asset</c>.
    /// </summary>
    public static class CharacterVoiceAssets
    {
        public const string BankAssetPath = "Assets/PoOlympic/Resources/CharacterVoiceBank.asset";

        [MenuItem("PoOlympic/Audio/Generate Character Voice Placeholders")]
        public static string GenerateMenu()
        {
            var (created, total) = EnsurePlaceholders(forceReimport: true);
            return $"Character voice bank ready: {created} new placeholder WAVs generated ({total} total slots).";
        }

        public static (int created, int total) EnsurePlaceholders(bool forceReimport = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BankAssetPath));
            var bank = AssetDatabase.LoadAssetAtPath<CharacterVoiceBank>(BankAssetPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<CharacterVoiceBank>();
                AssetDatabase.CreateAsset(bank, BankAssetPath);
            }

            int created = 0;
            int total = 0;
            bool needRefresh = false;

            foreach (var characterId in MeetLineup.Roster)
            {
                var set = bank.GetOrAddSet(characterId);
                for (int e = 0; e < CharacterVoiceSynth.EmotionCount; e++)
                {
                    var emotion = (CharacterEmotion)e;
                    for (int v = 0; v < CharacterVoiceSynth.VariantsPerEmotion; v++)
                    {
                        for (int lvl = 1; lvl <= CharacterVoiceSynth.ExpressivenessLevels; lvl++)
                        {
                            total++;
                            string rel = CharacterVoiceBank.RelativeWavPath(characterId, emotion, v, lvl);
                            string assetPath = $"{BroadcastFx.AudioFolder}/{rel}";
                            if (!File.Exists(assetPath))
                            {
                                float[] samples = CharacterVoiceSynth.GenerateSamples(characterId, emotion, v, lvl);
                                CharacterVoiceSynth.WriteWavFile(assetPath, samples, CharacterVoiceSynth.SampleRate);
                                created++;
                                needRefresh = true;
                            }
                        }
                    }
                }
            }

            if (needRefresh || forceReimport)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var characterId in MeetLineup.Roster)
            {
                var set = bank.GetOrAddSet(characterId);
                for (int e = 0; e < CharacterVoiceSynth.EmotionCount; e++)
                {
                    var emotion = (CharacterEmotion)e;
                    for (int v = 0; v < CharacterVoiceSynth.VariantsPerEmotion; v++)
                    {
                        for (int lvl = 1; lvl <= CharacterVoiceSynth.ExpressivenessLevels; lvl++)
                        {
                            string rel = CharacterVoiceBank.RelativeWavPath(characterId, emotion, v, lvl);
                            string assetPath = $"{BroadcastFx.AudioFolder}/{rel}";
                            int slot = CharacterVoiceBank.SlotIndex(emotion, v, lvl);
                            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                            if (clip != null) set.clips[slot] = clip;
                        }
                    }
                }
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            return (created, total);
        }
    }
}
