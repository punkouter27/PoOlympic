using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PoOlympic.Tests
{
    /// <summary>Demo mode (2026-09-30): random lineups and the season table (the scene loop is checked in Play mode).</summary>
    public class DemoModeTests
    {
        const string SeasonKey = "PoOlympic.DemoSeason";
        string _saved;

        string _savedElo;

        [SetUp]
        public void SaveSeason()
        {
            _saved = PlayerPrefs.GetString(SeasonKey, null);
            _savedElo = PlayerPrefs.GetString(AgentElo.Key, null);
        }

        [TearDown]
        public void RestoreSeason()
        {
            if (_saved == null) PlayerPrefs.DeleteKey(SeasonKey);
            else PlayerPrefs.SetString(SeasonKey, _saved);
            if (_savedElo == null) PlayerPrefs.DeleteKey(AgentElo.Key);
            else PlayerPrefs.SetString(AgentElo.Key, _savedElo);
            PlayerPrefs.Save();
        }

        static (string, int)[] Heat(params (string athlete, int place)[] lanes) => lanes;

        [Test]
        public void DemoHeat_RatesEveryPairOfDifferentAthletesByBestPlacing()
        {
            AgentElo.Reset();
            // 8 lanes, each athlete twice: best placings KIM 1, MATT 2, ZOMBIE 3, GRANDMA 4
            var deltas = DemoElo.RecordHeat("test:1", Heat(("KIM", 1), ("MATT", 2), ("ZOMBIE", 3), ("GRANDMA", 4),
                                                           ("KIM", 5), ("MATT", 6), ("ZOMBIE", 7), ("GRANDMA", 8)));
            float k = AgentElo.KFactor / 3f;                                  // K / (athletes - 1), equal ratings: E = 1/2
            Assert.AreEqual(4, deltas.Count);
            Assert.AreEqual(1.5f * k, deltas["KIM"], 0.01f);
            Assert.AreEqual(0.5f * k, deltas["MATT"], 0.01f);
            Assert.AreEqual(-0.5f * k, deltas["ZOMBIE"], 0.01f);
            Assert.AreEqual(-1.5f * k, deltas["GRANDMA"], 0.01f);
            Assert.AreEqual(AgentElo.DefaultElo + 1.5f * k, AgentElo.GetElo("KIM"), 0.01f, "persisted in the tournament table");
            Assert.AreEqual(3, AgentElo.GetEntry("KIM").wins, "KIM never plays its own copy");
            StringAssert.StartsWith("KIM 1016 (+16)", DemoElo.LastSummary);
        }

        [Test]
        public void DemoHeat_TiesSplitAndSameAthleteFieldsAreUnrated()
        {
            AgentElo.Reset();
            var tie = DemoElo.RecordHeat("test:tie", Heat(("MATT", 1), ("KIM", 1), ("MATT", 3), ("KIM", 4)));
            Assert.AreEqual(0f, tie["MATT"], 0.001f, "same best place = ½ each");
            Assert.AreEqual(0f, tie["KIM"], 0.001f);
            Assert.IsEmpty(DemoElo.RecordHeat("test:solo", Enumerable.Range(1, 8).Select(p => ("MATT", p)).ToArray()),
                "an all-MATT field has no pairs");
            Assert.AreEqual(AgentElo.DefaultElo, AgentElo.GetElo("MATT"), 0.001f);
        }

        [Test]
        public void DemoHeat_CountsOnceAndAbortedHeatsRecordNothing()
        {
            AgentElo.Reset();
            var field = Heat(("ZOMBIE", 1), ("MATT", 2));
            Assert.AreEqual(2, DemoElo.RecordHeat("test:once", field).Count);
            float after = AgentElo.GetElo("ZOMBIE");
            Assert.IsEmpty(DemoElo.RecordHeat("test:once", field), "the same heat is never rated twice");
            Assert.AreEqual(after, AgentElo.GetElo("ZOMBIE"), 0.001f);
            // a heat left or skipped before its result never reaches RecordHeat; with no placings nothing is rated
            Assert.IsEmpty(DemoElo.RecordHeat("test:aborted", new (string, int)[0]));
            DemoMode.Stop();                                                    // not running: a no-op, rates nothing
            Assert.AreEqual(after, AgentElo.GetElo("ZOMBIE"), 0.001f);
        }

        [Test]
        public void LineupsFillAllEightLanesFromTheRoster()
        {
            var themes = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 400; i++)
            {
                var lanes = DemoLineups.Random(MeetLineup.Roster, out var theme);
                Assert.AreEqual(8, lanes.Length);
                Assert.IsTrue(lanes.All(a => MeetLineup.Roster.Contains(a)), string.Join(",", lanes));
                if (theme == "Mixed lineup") Assert.GreaterOrEqual(lanes.Distinct().Count(), 2, "a mixed lineup mixes bodies");
                themes.Add(theme.Split(':')[0].Split(' ')[0]);
            }
            CollectionAssert.IsSupersetOf(themes, new[] { "Mixed", "All", "4", "One" }, "every lineup kind is drawn");
        }

        [Test]
        public void OneBodyRosterAlwaysFillsWithThatBody()
        {
            var lanes = DemoLineups.Random(new[] { "MATT" }, out var theme);
            Assert.IsTrue(lanes.All(a => a == "MATT"));
            Assert.AreEqual("All MATT", theme);
        }

        [Test]
        public void SeasonCountsSeriesWinsAndPointsPerEvent()
        {
            DemoSeason.Reset();
            Assert.AreEqual("", DemoSeason.Summary());
            var lineup = new[] { "MATT", "MATT", "MATT", "MATT", "ZOMBIE", "ZOMBIE", "ZOMBIE", "ZOMBIE" };
            DemoSeason.Record(lineup, new[] { 10, 8, 6, 5, 20, 3, 2, 2 }, 2);   // lane 5 (ZOMBIE) wins the series
            Assert.AreEqual(1, DemoSeason.SeriesPlayed);
            StringAssert.Contains("wins ZOMBIE 1 · MATT 0", DemoSeason.Summary());
            StringAssert.Contains("MATT 3.6", DemoSeason.Summary());      // 29 points / (4 lanes × 2 events) = 3.625
            StringAssert.Contains("ZOMBIE 3.4", DemoSeason.Summary());    // 27 / 8 = 3.375
        }
    }
}
