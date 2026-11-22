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
    /// Verifies character voice sound coverage (4 emotions x 5 variants x 5 expressiveness levels = 100/character),
    /// procedural WAV placeholder generation + runtime fallback, persistent ELO ratings (head-to-head and from the
    /// placings of a 16-entrant event), and the 16-entrant event field.
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
        public void CharacterVoices_EveryRosterCharacterHasAll100SoundsAndPlaceholderWavs()
        {
            CharacterVoiceAssets.EnsurePlaceholders();

            var bank = AssetDatabase.LoadAssetAtPath<CharacterVoiceBank>(CharacterVoiceAssets.BankAssetPath);
            Assert.NotNull(bank, "CharacterVoiceBank asset missing");

            Assert.AreEqual(4, CharacterVoiceSynth.EmotionCount);
            Assert.AreEqual(5, CharacterVoiceSynth.VariantsPerEmotion);
            Assert.AreEqual(5, CharacterVoiceSynth.ExpressivenessLevels);
            Assert.AreEqual(100, CharacterVoiceSynth.SoundsPerCharacter);

            foreach (var characterId in MeetLineup.Roster)
            {
                foreach (CharacterEmotion emotion in Enum.GetValues(typeof(CharacterEmotion)))
                {
                    for (int variant = 0; variant < CharacterVoiceSynth.VariantsPerEmotion; variant++)
                    {
                        float rms1 = Rms(CharacterVoiceSynth.GenerateSamples(characterId, emotion, variant, 1));
                        float rms5 = Rms(CharacterVoiceSynth.GenerateSamples(characterId, emotion, variant, 5));
                        Assert.Greater(rms5, rms1, $"{characterId} {emotion} v{variant}: expressiveness 5 should be louder/stronger than 1");

                        for (int lvl = 1; lvl <= CharacterVoiceSynth.ExpressivenessLevels; lvl++)
                        {
                            string wavPath = $"{BroadcastFx.AudioFolder}/{CharacterVoiceBank.RelativeWavPath(characterId, emotion, variant, lvl)}";
                            Assert.IsTrue(File.Exists(wavPath), $"Missing placeholder WAV: {wavPath}");
                            Assert.IsTrue(bank.HasWavClip(characterId, emotion, variant, lvl), $"Bank missing WAV clip for {characterId} {emotion} v{variant} L{lvl}");
                            var clip = bank.GetClip(characterId, emotion, variant, lvl);
                            Assert.NotNull(clip, $"Null clip for {characterId} {emotion} v{variant} L{lvl}");
                        }
                    }
                }
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
        public void AgentElo_UpdatesAndIgnoresByeAndPersists()
        {
            AgentElo.Reset();
            Assert.AreEqual(1200, AgentElo.GetRoundedElo("MATT"));
            Assert.AreEqual(1200, AgentElo.GetRoundedElo("KIM"));
            Assert.IsTrue(AgentElo.IsBye("BYE"));
            Assert.IsTrue(AgentElo.IsBye(""));

            var (wElo, lElo, delta) = AgentElo.RecordMatch("MATT", "KIM");
            Assert.Greater(delta, 0f);
            Assert.AreEqual(1200f + delta, wElo, 0.01f);
            Assert.AreEqual(1200f - delta, lElo, 0.01f);
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
            // 16 entrants, two heats of 8: a character's mark is its best entrant
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
            Assert.AreEqual(1200f + deltas["KIM"], AgentElo.GetElo("KIM"), 0.01f, "stored");
            Assert.AreEqual(3, AgentElo.GetEntry("KIM").wins);
            Assert.AreEqual(3, AgentElo.GetEntry("ZOMBIE").losses);

            // one character alone cannot be rated
            float before = AgentElo.GetElo("MATT");
            Assert.IsEmpty(AgentElo.RecordEvent(Enumerable.Repeat("MATT", 16).ToArray(), points));
            Assert.AreEqual(before, AgentElo.GetElo("MATT"), 0.001f);
        }

        [Test]
        public void MeetSeries_OrdersEntrantsByPointsThenByNumber()
        {
            var points = new[] { 5, 20, 20, 0, 31, 5 };
            CollectionAssert.AreEqual(new[] { 4, 1, 2, 0, 5, 3 }, MeetSeries.Order(points).ToArray());
        }

        [Test]
        public void EventCompetition_Draws16ParticipantsWithReplacementFromCurrentAgents()
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
        public void EventCompetition_RunsAll16EntrantsInOneHeat()
        {
            var participants = Enumerable.Range(0, EventCompetition.EntrantCount).Select(i => $"AGENT_{i:00}").ToArray();
            CollectionAssert.AreEqual(participants,
                EventCompetition.ParticipantsForHeat(participants, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EventCompetition.ParticipantsForHeat(participants, 1));
        }
    }
}
