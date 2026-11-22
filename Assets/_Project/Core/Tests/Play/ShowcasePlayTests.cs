using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PoOlympic.Tests
{
    /// <summary>
    /// The Po game standard's showcase rules, checked in a real heat (Play mode):
    ///   * every reward-penalty term of RewardPenalties (torque², body angular velocity², action rate, foot slip; joint
    ///     velocity² only when its weight is not 0) is charged to the athletes' episode reward;
    ///   * the athletes' heads look around (render-only gaze, within a neck's reach);
    ///   * the Tab overlay shows FPS, episode reward, inference latency and curriculum stage, and its "Benchmark training
    ///     speed" button measures the local simulation;
    ///   * the main menu starts demo (attract) mode after ShowcaseSettings.idleDemoSeconds without input.
    /// Run: Test Runner › PlayMode, or batch: Unity -runTests -testPlatform PlayMode -testFilter ShowcasePlayTests.
    /// </summary>
    public class ShowcasePlayTests
    {
        const float LoadSeconds = 60f, ReadySeconds = 60f;
        string _savedElo;

        [SetUp] public void Save() => _savedElo = PlayerPrefs.GetString(AgentElo.Key, null);

        [TearDown]
        public void Restore()
        {
            if (DemoMode.Active) DemoMode.Stop();
            HudAnchors.DebugOn = false;
            GameSettings.SimSpeed = 1f;
            if (_savedElo == null) PlayerPrefs.DeleteKey(AgentElo.Key);
            else PlayerPrefs.SetString(AgentElo.Key, _savedElo);
            PlayerPrefs.Save();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Heat_ChargesEveryPenalty_TurnsHeads_AndTheTabOverlayBenchmarks()
        {
            yield return Menu();
            var menu = Find<MainMenuController>();
            yield return PlayEvent(menu, 0);
            var hud = Find<BroadcastHud>();
            // let the heat run a few seconds of game time
            float end = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < end) yield return null;

            // 1. penalties: every weighted term is charged (< 0) on at least one athlete, a 0-weight term is never charged
            var runners = PolicyRunner.Live.Where(r => r != null && r.isActiveAndEnabled && r.Initialized && !r.holdDefaultPose).ToList();
            Assert.Greater(runners.Count, 0, "no athlete is driven by a brain");
            var w = RewardPenalties.Current;
            float[] weights = { w.torqueL2, w.bodyAngVelL2, w.actionRateL2, w.footSlip, w.jointVelL2 };
            var totals = Enumerable.Range(0, weights.Length).Select(i => runners.Sum(r => r.PenaltyTotals[i])).ToArray();
            string line = string.Join("  ", PolicyRunner.PenaltyNames.Select((n, i) => $"{n} {totals[i]:0.000}"));
            Debug.Log($"[ShowcasePlayTests] penalties over {runners.Count} athletes: {line}");
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] == 0f) Assert.AreEqual(0f, totals[i], $"{PolicyRunner.PenaltyNames[i]} has weight 0 but was charged ({line})");
                else Assert.Less(totals[i], 0f, $"{PolicyRunner.PenaltyNames[i]} was never charged ({line})");
            }
            Assert.IsTrue(weights.Take(4).All(x => x < 0f), "torque, angular velocity, action rate and foot slip must all carry a penalty weight");

            // 2. gaze: some head has turned, none past the neck's reach
            var binders = UnityEngine.Object.FindObjectsByType<BoneBinder>();
            Assert.Greater(binders.Length, 0, "no BoneBinder in the heat");
            float maxTurn = binders.Max(b => b.GazeAngle);
            var s = ShowcaseSettings.Current;
            Debug.Log($"[ShowcasePlayTests] largest head turn {maxTurn:0.0} deg over {binders.Length} athletes");
            Assert.Greater(maxTurn, 3f, "no athlete's head turned toward its gaze target");
            Assert.LessOrEqual(maxTurn, Mathf.Sqrt(s.gazeMaxYaw * s.gazeMaxYaw + s.gazeMaxPitch * s.gazeMaxPitch) + 1f, "a head turned past the neck's reach");

            // 3. Tab overlay: the four readings, then the benchmark
            HudAnchors.DebugOn = true;
            yield return null;
            yield return Until(() => hud.Anchors.DebugPanel.text.Contains("Curriculum stage"), 5f, "the Tab overlay shows no readout");
            string text = hud.Anchors.DebugPanel.text;
            foreach (var want in new[] { "FPS", "Episode reward", "Inference", "Curriculum stage" })
                StringAssert.Contains(want, text);
            Assert.IsTrue(HudLayoutAudit.Shown(hud.Anchors.BenchButton), "the benchmark button is not shown with the overlay");
            Click(hud.Anchors.BenchButton);
            Assert.IsTrue(SimBenchmark.Running, "the benchmark did not start");
            yield return Until(() => !SimBenchmark.Running, s.benchmarkSeconds * 20f + 10f, "the benchmark never finished");
            Debug.Log("[ShowcasePlayTests] " + SimBenchmark.Result);
            Assert.Greater(SimBenchmark.StepsPerSecond, 100.0, SimBenchmark.Result);
            StringAssert.Contains("not a training run", SimBenchmark.Result);
            yield return Until(() => hud.Anchors.DebugPanel.text.Contains("physics steps/s"), 5f, "the overlay does not show the benchmark result");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Menu_StartsDemoMode_AfterTheIdleTime()
        {
            yield return Menu();
            float idle = MainMenuController.IdleDemoSeconds;
            Assert.AreEqual(30f, idle, 0.01f, "the attract mode starts after 30 s idle (ShowcaseSettings.asset)");
            float t0 = Time.realtimeSinceStartup;
            yield return Until(() => DemoMode.Active, idle + 15f, "demo mode did not start on an idle menu");
            float took = Time.realtimeSinceStartup - t0;
            Debug.Log($"[ShowcasePlayTests] demo on after {took:0.0} s idle");
            Assert.GreaterOrEqual(took, idle - 1f, "demo mode started before the idle time");
            yield return Until(() => UnityEngine.Object.FindAnyObjectByType<BroadcastHud>() != null && !MeetLineup.Loading, LoadSeconds, "demo mode loaded no event");
            DemoMode.Stop();
            yield return Until(() => SceneManager.GetActiveScene().name == MeetLineup.MenuScene && !MeetLineup.Loading, LoadSeconds, "stopping demo mode did not return to the menu");
        }

        // ------------------------------------------------------------------------------------------ helpers
        static IEnumerator Menu()
        {
            Assert.IsTrue(MeetLineup.Load(MeetLineup.MenuScene), "the menu scene is not in Build Settings");
            yield return Until(() => SceneManager.GetActiveScene().name == MeetLineup.MenuScene && !MeetLineup.Loading &&
                                     UnityEngine.Object.FindAnyObjectByType<MainMenuController>() != null, LoadSeconds, "the main menu did not load");
            yield return null;
        }

        static IEnumerator PlayEvent(MainMenuController menu, int index)
        {
            var root = menu.GetComponent<UIDocument>().rootVisualElement;
            var tiles = root.Query<Button>(className: "event-button").ToList();
            Click(tiles[index]);
            yield return null;
            Click(root.Q<Button>("playButton"));
            string scene = menu.events[index].scene;
            yield return Until(() => SceneManager.GetActiveScene().name == scene && !MeetLineup.Loading &&
                                     UnityEngine.Object.FindAnyObjectByType<BroadcastHud>() != null, LoadSeconds, $"{scene} did not load");
            var hud = Find<BroadcastHud>();
            yield return Until(() => hud.Board != null && hud.Anchors != null, 5f, $"{scene}: the HUD has no board");
            yield return Until(() => hud.Board.BoardState == BoardPhase.Live, ReadySeconds, $"{scene}: the heat never went live");
        }

        static T Find<T>() where T : UnityEngine.Object
        {
            var o = UnityEngine.Object.FindAnyObjectByType<T>();
            Assert.IsNotNull(o, $"no {typeof(T).Name} in {SceneManager.GetActiveScene().name}");
            return o;
        }

        static void Click(Button b)
        {
            Assert.IsNotNull(b, "button not on screen");
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = b;
            b.SendEvent(e);
        }

        static IEnumerator Until(Func<bool> done, float seconds, string failure)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"{failure} (waited {seconds:0} s, scene {SceneManager.GetActiveScene().name})");
                yield return null;
            }
        }
    }
}
