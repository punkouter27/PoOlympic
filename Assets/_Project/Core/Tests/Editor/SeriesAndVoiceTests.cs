using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PoOlympic.Editor;
using UnityEditor;
using UnityEngine;

namespace PoOlympic.Tests
{
    /// <summary>
    /// Verifies character voice sound coverage (5 emotions x 5 variants x 5 expressiveness levels = 125/character),
    /// the voices.json manifest, the advanced procedural imitation and its fallback, persistent ELO ratings (head-to-head and from the
    /// placings of an event), the 8-athlete event field and the 8-lane scenes every playable event is built from.
    /// </summary>
    public class SeriesAndVoiceTests
    {
        string _savedElo;

        [SetUp]
        public void SetUp() => _savedElo = PlayerPrefs.GetString(AgentElo.Key, null);

        [TearDown]
        public void TearDown() => RestorePref(AgentElo.Key, _savedElo);

        static void RestorePref(string key, string value)
        {
            if (value == null) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }

        static float Rms(float[] samples)
        {
            double sum = 0.0;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
        }

        [Test]
        public void VoiceManifest_CoversEveryAthleteEmotionLevelAndTake()
        {
            Assert.AreEqual(5, CharacterVoiceSynth.EmotionCount);
            Assert.AreEqual(5, CharacterVoiceSynth.VariantsPerEmotion);
            Assert.AreEqual(5, CharacterVoiceSynth.ExpressivenessLevels);
            Assert.AreEqual(125, CharacterVoiceSynth.SoundsPerCharacter);

            Assert.IsTrue(File.Exists(CharacterVoiceAssets.ManifestPath), "voices.json missing");
            var m = CharacterVoiceManifest.Parse(File.ReadAllText(CharacterVoiceAssets.ManifestPath));
            Assert.NotNull(m, "voices.json does not parse as po-voice-manifest/1");
            CollectionAssert.AreEqual(new[] { "angry", "happy", "hurt", "insult", "celebrate" }, m.emotions);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, m.levels);
            Assert.AreEqual("procedural", m.fallback);
            Assert.AreEqual(CharacterVoiceManifest.Root, m.root);
            CollectionAssert.AreEquivalent(MeetLineup.Roster, m.characters.Select(c => c.name), "one entry per roster athlete, no BYE");

            var naming = new System.Text.RegularExpressions.Regex(@"^[A-Z0-9_]+/(ANGRY|HAPPY|HURT|INSULT|CELEBRATE)/(angry|happy|hurt|insult|celebrate)_0[1-5]_L[1-5]\.wav$");
            foreach (var c in m.characters)
            {
                Assert.AreEqual(c.name.ToLowerInvariant(), c.id);
                Assert.IsTrue(c.voice != null && c.voice.Valid && !string.IsNullOrEmpty(c.voice.timbre), $"{c.name}: voice profile");
                foreach (var emotion in CharacterVoiceManifest.EmotionOrder)
                {
                    var cells = c.clips.Of(emotion);
                    CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, cells.Select(x => x.level), $"{c.name} {emotion}: levels");
                    foreach (var cell in cells)
                    {
                        Assert.AreEqual(5, cell.takes.Length, $"{c.name} {emotion} L{cell.level}: takes");
                        Assert.AreEqual(cell.takes[0].file, cell.file);
                        Assert.AreEqual(cell.takes.Any(t => t.exists), cell.exists);
                        for (int t = 0; t < 5; t++)
                        {
                            var take = cell.takes[t];
                            Assert.AreEqual(t + 1, take.take);
                            Assert.AreEqual(CharacterVoiceManifest.FileOf(c.name, emotion, t, cell.level), take.file);
                            StringAssert.IsMatch(naming.ToString(), take.file);
                            // exists is the generator's: true only for a real recording (not a synth-baked placeholder)
                            Assert.AreEqual(CharacterVoiceAssets.IsRecorded($"{m.root}/{take.file}"), take.exists,
                                $"{take.file}: stale exists flag — run PoOlympic › Audio › Rebuild Voice Manifest");
                        }
                    }
                }
            }
            var bank = AssetDatabase.LoadAssetAtPath<CharacterVoiceBank>(CharacterVoiceAssets.BankAssetPath);
            Assert.NotNull(bank, "CharacterVoiceBank asset missing");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<TextAsset>(CharacterVoiceAssets.ManifestPath), bank.manifest, "the bank loads voices.json");
            foreach (var c in m.characters)
                foreach (var emotion in CharacterVoiceManifest.EmotionOrder)
                    for (int t = 0; t < 5; t++)
                        for (int lvl = 1; lvl <= 5; lvl++)
                        {
                            Assert.AreEqual(m.Exists(c.name, emotion, t, lvl), bank.HasWavClip(c.name, emotion, t, lvl), $"{c.name} {emotion} {t} L{lvl}");
                            Assert.NotNull(bank.GetClip(c.name, emotion, t, lvl));
                        }
        }

        [Test]
        public void VoiceManifest_SynthBakedWavIsAPlaceholderNotARecording()
        {
            string path = Path.Combine(Path.GetTempPath(), "poolympic_placeholder_test.wav");   // outside the project
            try
            {
                CharacterVoiceSynth.WriteWavFile(path, CharacterVoiceSynth.GenerateSamples("MATT", CharacterEmotion.Happy, 0, 3));
                var bytes = File.ReadAllBytes(path);
                Assert.IsTrue(CharacterVoiceSynth.IsPlaceholderWav(bytes));
                Assert.IsFalse(CharacterVoiceAssets.IsRecorded(path));
                // the same audio without the marker is what a recording looks like
                var marker = System.Text.Encoding.ASCII.GetBytes(CharacterVoiceSynth.PlaceholderMarker);
                int at = Enumerable.Range(0, bytes.Length - marker.Length).First(i => bytes.Skip(i).Take(marker.Length).SequenceEqual(marker));
                bytes[at] = (byte)'X';
                Assert.IsFalse(CharacterVoiceSynth.IsPlaceholderWav(bytes));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ProceduralVoice_EveryCellAudibleScaledDeterministicAndPerAthlete()
        {
            foreach (var characterId in MeetLineup.Roster)
                foreach (CharacterEmotion emotion in Enum.GetValues(typeof(CharacterEmotion)))
                    for (int take = 0; take < CharacterVoiceSynth.VariantsPerEmotion; take++)
                    {
                        var l1 = CharacterVoiceSynth.GenerateSamples(characterId, emotion, take, 1);
                        var l5 = CharacterVoiceSynth.GenerateSamples(characterId, emotion, take, 5);
                        string what = $"{characterId} {emotion} take {take + 1}";
                        Assert.Greater(Rms(l1), 0.01f, $"{what}: level 1 silent");
                        Assert.Greater(Rms(l5), Rms(l1), $"{what}: level 5 not louder than 1");
                        Assert.Greater(l5.Length, l1.Length, $"{what}: level 5 not longer than 1");
                        CollectionAssert.AreEqual(l5, CharacterVoiceSynth.GenerateSamples(characterId, emotion, take, 5), $"{what}: not deterministic");
                    }
            for (int a = 0; a < MeetLineup.Roster.Length; a++)
                for (int b = a + 1; b < MeetLineup.Roster.Length; b++)
                {
                    var x = CharacterVoiceSynth.GenerateSamples(MeetLineup.Roster[a], CharacterEmotion.Angry, 0, 3);
                    var y = CharacterVoiceSynth.GenerateSamples(MeetLineup.Roster[b], CharacterEmotion.Angry, 0, 3);
                    Assert.IsFalse(x.Length == y.Length && x.SequenceEqual(y), $"{MeetLineup.Roster[a]} and {MeetLineup.Roster[b]} sound the same");
                }
        }

        [Test]
        public void CharacterVoices_MissingWavFallsBackProcedurallyForFutureCharacters()
        {
            var bank = ScriptableObject.CreateInstance<CharacterVoiceBank>();
            try
            {
                foreach (CharacterEmotion emotion in Enum.GetValues(typeof(CharacterEmotion)))
                {
                    for (int v = 0; v < 5; v++)
                    {
                        for (int lvl = 1; lvl <= 5; lvl++)
                        {
                            Assert.IsFalse(bank.HasWavClip("CYBORG", emotion, v, lvl));
                            var clip = bank.GetClip("CYBORG", emotion, v, lvl);
                            Assert.NotNull(clip, $"Procedural fallback failed for CYBORG {emotion} v{v} L{lvl}");
                            Assert.Greater(clip.samples, 100);
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bank);
            }
        }

        [Test]
        public void CharacterVoices_CelebrateIsAppendedAndDistinctFromHappy()
        {
            // the first four emotions keep their slots in saved banks; CELEBRATE takes 100..124
            Assert.AreEqual(99, CharacterVoiceBank.SlotIndex(CharacterEmotion.Insult, 4, 5));
            Assert.AreEqual(100, CharacterVoiceBank.SlotIndex(CharacterEmotion.Celebrate, 0, 1));
            Assert.AreEqual(124, CharacterVoiceBank.SlotIndex(CharacterEmotion.Celebrate, 4, 5));
            Assert.AreEqual("Characters/KIM/CELEBRATE/celebrate_03_L5.wav",
                CharacterVoiceBank.RelativeWavPath("kim", CharacterEmotion.Celebrate, 2, 5));

            foreach (var characterId in MeetLineup.Roster)
                for (int v = 0; v < CharacterVoiceSynth.VariantsPerEmotion; v++)
                {
                    var happy = CharacterVoiceSynth.GenerateSamples(characterId, CharacterEmotion.Happy, v, 5);
                    var cheer = CharacterVoiceSynth.GenerateSamples(characterId, CharacterEmotion.Celebrate, v, 5);
                    Assert.Greater(cheer.Length, happy.Length, $"{characterId} v{v}: celebrate is the longer, held whoop");
                }
        }

        [Test]
        public void AgentElo_UpdatesAndIgnoresByeAndPersists()
        {
            AgentElo.Reset();
            Assert.AreEqual(1000, AgentElo.GetRoundedElo("MATT"));
            Assert.AreEqual(1000, AgentElo.GetRoundedElo("KIM"));
            Assert.IsTrue(AgentElo.IsBye("BYE"));
            Assert.IsTrue(AgentElo.IsBye(""));

            var (wElo, lElo, delta) = AgentElo.RecordMatch("MATT", "KIM");
            Assert.Greater(delta, 0f);
            Assert.AreEqual(AgentElo.DefaultElo + delta, wElo, 0.01f);
            Assert.AreEqual(AgentElo.DefaultElo - delta, lElo, 0.01f);
            Assert.AreEqual(wElo, AgentElo.GetElo("MATT"), 0.01f);
            Assert.AreEqual(lElo, AgentElo.GetElo("KIM"), 0.01f);

            // BYE match must not change ELO
            var (_, _, byeDelta) = AgentElo.RecordMatch("MATT", "BYE");
            Assert.AreEqual(0f, byeDelta);
            Assert.AreEqual(wElo, AgentElo.GetElo("MATT"), 0.01f);

            AgentElo.RecordTournamentWin("MATT");
            Assert.AreEqual(1, AgentElo.GetEntry("MATT").tournamentsWon);
        }

        [Test]
        public void AgentElo_EventPlacingsRateEveryCharacterAgainstEveryOther()
        {
            AgentElo.Reset();
            // RecordEvent takes any field size (16 here, from before events ran 8): a character's mark is its best entrant
            var entrants = new[]
            {
                "KIM", "MATT", "ZOMBIE", "GRANDMA", "KIM", "MATT", "ZOMBIE", "GRANDMA",
                "GRANDMA", "ZOMBIE", "MATT", "KIM", "GRANDMA", "ZOMBIE", "MATT", "KIM",
            };
            var points = new[] { 10, 8, 6, 5, 4, 3, 2, 1, 8, 6, 5, 4, 3, 2, 1, 0 };   // best: KIM 10, MATT 8, GRANDMA 8, ZOMBIE 6

            var deltas = AgentElo.RecordEvent(entrants, points);
            Assert.AreEqual(4, deltas.Count);
            // equal ratings before: three games each at K/3, expectation ½ per game
            float k = AgentElo.KFactor / 3f;
            Assert.AreEqual(k * 1.5f, deltas["KIM"], 0.01f, "KIM beat all three");
            Assert.AreEqual(0f, deltas["MATT"], 0.01f, "MATT: lost to KIM, tied GRANDMA, beat ZOMBIE");
            Assert.AreEqual(0f, deltas["GRANDMA"], 0.01f);
            Assert.AreEqual(-k * 1.5f, deltas["ZOMBIE"], 0.01f, "ZOMBIE lost all three");
            Assert.AreEqual(0f, deltas.Values.Sum(), 0.01f, "ratings are zero-sum");
            Assert.AreEqual(AgentElo.DefaultElo + deltas["KIM"], AgentElo.GetElo("KIM"), 0.01f, "stored");
            Assert.AreEqual(3, AgentElo.GetEntry("KIM").wins);
            Assert.AreEqual(3, AgentElo.GetEntry("ZOMBIE").losses);

            // one character alone cannot be rated
            float before = AgentElo.GetElo("MATT");
            Assert.IsEmpty(AgentElo.RecordEvent(Enumerable.Repeat("MATT", 16).ToArray(), points));
            Assert.AreEqual(before, AgentElo.GetElo("MATT"), 0.001f);
        }

        [Test]
        public void MainMenu_EmptyEntrantSlotReadsBye()
        {
            Assert.AreEqual("BYE", MainMenuController.ShortName(null));
            Assert.AreEqual("BYE", MainMenuController.ShortName(""));
            Assert.AreEqual("BYE", MainMenuController.ShortName("bye"));
            Assert.AreEqual("GRA", MainMenuController.ShortName("GRANDMA"));
        }

        [Test]
        public void MeetSeries_OrdersEntrantsByPointsThenByNumber()
        {
            var points = new[] { 5, 20, 20, 0, 31, 5 };
            CollectionAssert.AreEqual(new[] { 4, 1, 2, 0, 5, 3 }, MeetSeries.Order(points).ToArray());
        }

        [Test]
        public void EventCompetition_Draws8ParticipantsWithReplacementFromCurrentAgents()
        {
            var participants = EventCompetition.DrawParticipants(MeetLineup.Roster, new System.Random(17));
            Assert.AreEqual(EventCompetition.EntrantCount, participants.Length);
            Assert.IsTrue(participants.All(MeetLineup.Roster.Contains));
            Assert.AreEqual(MeetLineup.Roster.Length,
                participants.Distinct().Count());

            var singleAgent = EventCompetition.DrawParticipants(new[] { "MATT" }, new System.Random(7));
            CollectionAssert.AreEqual(Enumerable.Repeat("MATT", EventCompetition.EntrantCount), singleAgent);
        }

        [Test]
        public void EventCompetition_RunsAll8EntrantsInOneHeat()
        {
            Assert.AreEqual(8, EventCompetition.EntrantCount, "every event has exactly 8 athletes");
            Assert.AreEqual(8, EventCompetition.HeatSize);
            Assert.AreEqual(1, EventCompetition.HeatCount);
            var participants = Enumerable.Range(0, EventCompetition.EntrantCount).Select(i => $"AGENT_{i:00}").ToArray();
            CollectionAssert.AreEqual(participants,
                EventCompetition.ParticipantsForHeat(participants, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EventCompetition.ParticipantsForHeat(participants, 1));
        }

        [Test]
        public void PlayableEvents_AreBuiltFromEightLaneScenes()
        {
            // the scene sources of the 11 playable events (EventScenes.EventScenePaths): 8 lanes, every roster body in each
            var layouts = new[]
            {
                EventScenes.PedestalRosterLayout, EventScenes.ShakerLayout, EventScenes.CrawlLayout, EventScenes.InvertedLayout,
                EventScenes.CrabLayout, EventScenes.SlalomLayout, EventScenes.TurntableLayout, EventScenes.TrackLayout,
            };
            string root = Path.GetDirectoryName(Application.dataPath);
            foreach (var layout in layouts)
            {
                string json = File.ReadAllText(Path.Combine(root, layout));
                var lanes = System.Text.RegularExpressions.Regex.Matches(json, "\"lane\":\\s*(\\d+)")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
                CollectionAssert.AreEquivalent(Enumerable.Range(0, EventCompetition.HeatSize), lanes.Distinct(), layout);
                foreach (var body in MeetLineup.Roster)
                    Assert.AreEqual(EventCompetition.HeatSize,
                        System.Text.RegularExpressions.Regex.Matches(json, $"\"body\":\\s*\"{body.ToLowerInvariant()}\"").Count,
                        $"{layout}: {body} in every lane");
            }
        }
    }
}
