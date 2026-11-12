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
    /// procedural WAV placeholder generation + runtime fallback, persistent ELO ratings, and the symmetric 16-player
    /// tournament bracket (8 Left · Winner Middle · 8 Right).
    /// </summary>
    public class TournamentAndVoiceTests
    {
        string _savedElo;
        string _savedBracket;
        bool _savedShowBracket;

        [SetUp]
        public void SetUp()
        {
            _savedElo = PlayerPrefs.GetString(AgentElo.Key, null);
            _savedBracket = PlayerPrefs.GetString("PoOlympic.TournamentBracket", null);
            _savedShowBracket = TournamentMode.ShowBracketOnMenu;
        }

        [TearDown]
        public void TearDown()
        {
            RestorePref(AgentElo.Key, _savedElo);
            RestorePref("PoOlympic.TournamentBracket", _savedBracket);
            TournamentMode.ShowBracketOnMenu = _savedShowBracket;
            TournamentMode.ClearSavedBracket();
            if (!string.IsNullOrEmpty(_savedBracket))
            {
                PlayerPrefs.SetString("PoOlympic.TournamentBracket", _savedBracket);
                PlayerPrefs.Save();
            }
        }

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
        public void TournamentBracket_Symmetric16SlotsWithByesReachesSingleChampion()
        {
            AgentElo.Reset();

            var slots = MeetLineup.TournamentSlots();
            Assert.AreEqual(16, slots.Length);
            Assert.AreEqual(16 - MeetLineup.Roster.Length, slots.Count(s => s == MeetLineup.ByeName));

            // Test with 4 current players -> auto-BYEs resolve down to 4 players reaching QF/SF cleanly and winning
            var b4 = TournamentBracket.Create(MeetLineup.Roster, shuffleOrder: false, tournamentNumber: 1, rng: new System.Random(42));
            Assert.AreEqual(16, b4.initialSlots.Length);
            // Left half (0..7) and Right half (8..15) both contain active players
            Assert.Greater(b4.initialSlots.Take(8).Count(s => !AgentElo.IsBye(s)), 0);
            Assert.Greater(b4.initialSlots.Skip(8).Count(s => !AgentElo.IsBye(s)), 0);

            int safety = 0;
            while (!b4.IsFinished && safety++ < 20)
            {
                var m = b4.NextPlayableMatch();
                Assert.NotNull(m, "Bracket not finished yet but no playable match found");
                Assert.IsFalse(AgentElo.IsBye(m.playerA));
                Assert.IsFalse(AgentElo.IsBye(m.playerB));
                Assert.IsTrue(b4.RecordMatchResult(m.round, m.index, m.playerA, 4, 1, 1, "Test Event", 16f));
            }
            Assert.IsTrue(b4.IsFinished);
            Assert.IsFalse(AgentElo.IsBye(b4.champion));
            Assert.AreEqual(b4.champion, b4.GetMatch(3, 0).winner);

            // Test with full 16 players -> 15 played matches -> single champion
            var sixteen = Enumerable.Range(1, 16).Select(i => $"BOT_{i:00}").ToArray();
            var b16 = TournamentBracket.Create(sixteen, shuffleOrder: false, tournamentNumber: 2, rng: new System.Random(7));
            Assert.IsFalse(b16.initialSlots.Any(AgentElo.IsBye));
            int played16 = 0;
            while (!b16.IsFinished && played16 < 30)
            {
                var m = b16.NextPlayableMatch();
                Assert.NotNull(m);
                b16.RecordMatchResult(m.round, m.index, m.playerA, 5, 3);
                played16++;
            }
            Assert.AreEqual(15, played16);
            Assert.IsTrue(b16.IsFinished);
        }

        [Test]
        public void TournamentMode_EventCompatibilityAndMatchupLineup()
        {
            var pool = new[]
            {
                new TournamentMode.EventChoice(1, "Pedestal", "Event_1", new[] { "MATT" }),
                new TournamentMode.EventChoice(8, "All Fours", "Event_8", new[] { "MATT", "ZOMBIE", "GRANDMA", "KIM" }),
            };

            var picked = TournamentMode.PickEventForMatch(pool, "GRANDMA", "KIM");
            Assert.AreEqual(8, picked.Number);

            var lineup = TournamentMode.BuildMatchLineup("ZOMBIE", "KIM");
            Assert.AreEqual(8, lineup.Length);
            Assert.AreEqual(4, lineup.Take(4).Count(x => x == "ZOMBIE"));
            Assert.AreEqual(4, lineup.Skip(4).Count(x => x == "KIM"));
        }
    }
}
