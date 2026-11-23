using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PoOlympic.Editor
{
    /// <summary>
    /// Voice manifest generator. Scans <see cref="CharacterVoiceManifest.Root"/> for every roster athlete x 5 emotions x
    /// 5 levels x 5 takes, rewrites <c>voices.json</c> (exists flags; the <c>voice</c> profiles already in the file are
    /// kept), binds the recorded WAVs into <c>Data/Resources/CharacterVoiceBank.asset</c> (and nothing else), and logs
    /// "N of M clips recorded; missing: …". Runs from PoOlympic › Audio › Rebuild Voice Manifest, from the FX upgrade
    /// (BroadcastFx), and by itself whenever a WAV under the voice folder is imported, moved or deleted.
    ///
    /// Recorded vs placeholder: a WAV counts as recorded unless it carries
    /// <see cref="CharacterVoiceSynth.PlaceholderMarker"/> (RIFF LIST/INFO ISFT), which every WAV the synth bakes has.
    /// No placeholders are baked any more (the runtime synthesizes missing cells); the 400 old unmarked placeholder WAVs
    /// were removed on 2026-11-22 after checking they matched the synth's output.
    /// </summary>
    public static class CharacterVoiceAssets
    {
        public const string BankAssetPath = "Assets/_Project/Data/Resources/CharacterVoiceBank.asset";
        public const string ManifestPath = CharacterVoiceManifest.Root + "/voices.json";

        [MenuItem("PoOlympic/Audio/Rebuild Voice Manifest (voices.json)")]
        public static string RebuildMenu()
        {
            var (recorded, total, _) = Rebuild();
            return $"voices.json: {recorded} of {total} clips recorded";
        }

        /// <summary>A real recording (exists = true): the file is there and is not synth-baked.</summary>
        public static bool IsRecorded(string assetPath) =>
            File.Exists(assetPath) && !CharacterVoiceSynth.IsPlaceholderWav(File.ReadAllBytes(assetPath));

        public static string AssetPathOf(string characterId, CharacterEmotion emotion, int take, int level) =>
            $"{CharacterVoiceManifest.Root}/{CharacterVoiceManifest.FileOf(characterId, emotion, take, level)}";

        /// <summary>Rewrite voices.json and rebind the bank. Returns (recorded takes, all takes, missing summary).</summary>
        public static (int recorded, int total, string missing) Rebuild()
        {
            var old = File.Exists(ManifestPath) ? CharacterVoiceManifest.Parse(File.ReadAllText(ManifestPath)) : null;
            var manifest = CharacterVoiceManifest.Build(MeetLineup.Roster,
                (id, e, t, l) => IsRecorded(AssetPathOf(id, e, t, l)),
                id => old?.Find(id)?.voice,
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            // only touch the file when something but the timestamp changed (no churn on every import)
            string json = manifest.ToJson();
            string Strip(string s) => string.Join("\n", (s ?? "").Replace("\r\n", "\n").Split('\n').Where(x => !x.TrimStart().StartsWith("\"generated\"")));
            bool changed = !File.Exists(ManifestPath) || Strip(File.ReadAllText(ManifestPath)) != Strip(json);
            if (changed)
            {
                File.WriteAllText(ManifestPath, json);
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BankAssetPath));
            var bank = AssetDatabase.LoadAssetAtPath<CharacterVoiceBank>(BankAssetPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<CharacterVoiceBank>();
                AssetDatabase.CreateAsset(bank, BankAssetPath);
            }
            bank.manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            foreach (var c in manifest.characters)
            {
                var set = bank.GetOrAddSet(c.name);
                for (int i = 0; i < set.clips.Length; i++) set.clips[i] = null;   // a placeholder is never bound
                foreach (var emotion in CharacterVoiceManifest.EmotionOrder)
                    foreach (var cell in c.clips.Of(emotion))
                        foreach (var take in cell.takes)
                            if (take.exists)
                                set.clips[CharacterVoiceBank.SlotIndex(emotion, take.take - 1, cell.level)] =
                                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{CharacterVoiceManifest.Root}/{take.file}");
            }
            bank.ReloadManifest();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();

            int recorded = manifest.RecordedCount(), total = manifest.TakeCount();
            var missing = new List<string>();
            foreach (var c in manifest.characters)
                foreach (var e in CharacterVoiceManifest.EmotionOrder)
                {
                    int miss = c.clips.Of(e).Sum(cell => cell.takes.Count(t => !t.exists));
                    if (miss > 0) missing.Add($"{c.name} {e.ToString().ToLowerInvariant()} ({miss})");
                }
            string summary = missing.Count == 0 ? "none" : string.Join(", ", missing);
            Debug.Log($"[VoiceManifest] {recorded} of {total} clips recorded; missing: {summary}" +
                      (changed ? $" — wrote {ManifestPath}" : ""));
            return (recorded, total, summary);
        }

        /// <summary>Re-runs the generator when a WAV under the voice folder changes (cheap: a file scan).</summary>
        sealed class Watcher : AssetPostprocessor
        {
            static bool _queued;

            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                bool Voice(string p) => p.StartsWith(CharacterVoiceManifest.Root + "/", StringComparison.Ordinal) &&
                                        p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                if (_queued || !imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(Voice)) return;
                _queued = true;
                EditorApplication.delayCall += () => { _queued = false; Rebuild(); };
            }
        }
    }
}
