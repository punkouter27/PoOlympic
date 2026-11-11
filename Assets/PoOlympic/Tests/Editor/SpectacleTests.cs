using NUnit.Framework;
using UnityEngine;

namespace PoOlympic.Tests
{
    /// <summary>Real weather, the AI commentator's request / reply handling and the slalom tactician's speed command —
    /// the parts that need no running scene.</summary>
    public class SpectacleTests
    {
        [Test]
        public void WindDragPointsDownwindAndIsCapped()
        {
            Weather.Set("Test", windMps: 30f, gustMps: 30f, fromDeg: 0f, rainMm: 0f, tempC: 10f);   // from the north = headwind
            var f = Weather.WindForce(0.0, 1.0);
            double capped = 0.5 * Weather.AirDensity * Weather.DragArea * Weather.MaxWind * Weather.MaxWind;
            Assert.AreEqual(-capped, f.x, 1e-6, "a north wind pushes against the running direction (+x), at the capped speed");
            Assert.AreEqual(0.0, f.y, 1e-6);
            Weather.Set("Test", 5f, 10f, 90f, 2f, 10f);                                             // from the east
            f = Weather.WindForce(Weather.GustPeriod / 4.0, 1.0);                                   // the top of a gust
            Assert.AreEqual(0.5 * Weather.AirDensity * Weather.DragArea * 49.0, f.y, 1e-6, "an east wind pushes to +y (left); gusts stop at 1.4 × the mean");
            StringAssert.Contains("crosswind", Weather.Headline);
            StringAssert.Contains("wet track", Weather.Headline);
        }

        [Test]
        public void CommentatorRequestIsValidJsonAndRepliesBecomeOneLine()
        {
            string body = AiCommentator.Body("L3 \"KIM\" takes the lead\nfrom L1", "Event 8 · 30 m\tDash");
            StringAssert.Contains("\"model\":\"" + AiCommentator.Model + "\"", body);
            StringAssert.Contains("L3 \\\"KIM\\\" takes the lead\\nfrom L1", body);
            StringAssert.Contains("30 m\\tDash", body);
            Assert.AreEqual("KIM storms past!", AiCommentator.Parse("{\"content\":[{\"type\":\"text\",\"text\":\"\\\"KIM storms\\npast!\\\"\"}],\"stop_reason\":\"end_turn\"}"));
            Assert.AreEqual("", AiCommentator.Parse("{\"content\":[{\"type\":\"text\",\"text\":\"no\"}],\"stop_reason\":\"refusal\"}"), "a refusal keeps the plain line");
            Assert.AreEqual("", AiCommentator.Parse("not json"));
            Assert.LessOrEqual(AiCommentator.Parse("{\"content\":[{\"type\":\"text\",\"text\":\"" + new string('a', 400) + "\"}]}").Length, AiCommentator.MaxChars);
        }

        [Test]
        public void SlalomSteerKeepsTheYawLimitWhenTheTacticianEasesOff()
        {
            var go = new GameObject("slalom");
            try
            {
                var ev = go.AddComponent<SlalomEvent>();
                var full = ev.Steer(5.0, 0.3, 0.0, 0.5);
                var eased = ev.Steer(5.0, 0.3, 0.0, 0.5, ev.speed * ev.yieldPace);
                Assert.AreEqual(ev.speed, full.x, 1e-6f);
                Assert.AreEqual(ev.speed * ev.yieldPace, eased.x, 1e-6f);
                Assert.AreEqual(full.z, eased.z, 1e-6f, "= slalom.py: the yaw command and its limit do not depend on the eased speed");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AggressiveRacersSurgeAndSwingIntoRivals()
        {
            var go = new GameObject("aggressive");
            try
            {
                var slalom = go.AddComponent<SlalomEvent>();
                Assert.IsTrue(slalom.aggressive, "Feature 4: slalom racers default to aggressive collision mode");
                var (vx, a) = slalom.Brawl(-0.42, slalom.speed);
                Assert.Greater(vx, slalom.speed, "brawler surges speed into convergence pole");
                Assert.AreEqual(-slalom.brawlLine, a, 1e-6, "brawler swings wide into the neighbour's line");

                var track = go.AddComponent<TrackRaceEvent>();
                Assert.IsTrue(track.aggressivePack, "Feature 4: track racers default to aggressive pack brawl");
                var contract = new Contract { steering = new Contract.Steering { heading_gain = 2.0, lane_gain = 1.0, wz_limit = 1.0 } };
                var cmd = track.BrawlCommand(myY: 1.22, laneOriginY: 1.22, rivalY: 0.0, yaw: 0.0, baseVx: 3.5f, contract);
                Assert.Greater(cmd.x, 3.5f, "track brawler surges forward speed when alongside a rival");
                Assert.Less(cmd.z, 0f, "track brawler steers toward rival on the right (y = 0 < 1.22)");
                Assert.AreEqual("261111", HudAnchors.BuildStamp, "BR build watermark matches YYMMDD");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
