using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoOlympic
{
    /// <summary>
    /// The auto-run series (main menu: RUN ALL): every playable event in turn with one field of 16 entrants (the
    /// roster's characters, repeated as needed), unattended, until one entrant has the most points.
    ///   - each event is an <see cref="EventCompetition"/> (the 16 entrants over the scene's heats); the HUD scores
    ///     each heat and calls Advance after ResultSeconds;
    ///   - an entrant's points per event (10-8-6-5-4-3-2-1 by place in its heat) add up over the series;
    ///   - ratings move after every event (EventCompetition → <see cref="AgentElo.RecordEvent"/>);
    ///   - after the last event: back to the menu, which shows the standings; the winner's character gets a series win.
    /// MeetSeriesRunner is the watchdog: a heat that stalls is skipped (no points, the event is not rated).
    /// </summary>
    public static class MeetSeries
    {
        public const float ResultSeconds = 4.5f;

        public readonly struct EventChoice
        {
            public readonly int Number;
            public readonly string Name;
            public readonly string Scene;

            public EventChoice(int number, string name, string scene)
            {
                Number = number;
                Name = name ?? "";
                Scene = scene ?? "";
            }
        }

        public static bool Active { get; private set; }
        public static int Index { get; private set; }
        public static int EventCount => _events.Length;
        /// <summary>Top of the last finished series, one entrant per line ("" before the first).</summary>
        public static string LastResult { get; private set; } = "";
        /// <summary>Character of the entrant that won the last finished series ("" before the first).</summary>
        public static string Champion { get; private set; } = "";

        static EventChoice[] _events = Array.Empty<EventChoice>();
        static string[] _entrants = Array.Empty<string>();
        static readonly int[] _points = new int[EventCompetition.EntrantCount];
        static int _advancedKey = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            Index = 0;
            LastResult = "";
            Champion = "";
            _events = Array.Empty<EventChoice>();
            _entrants = Array.Empty<string>();
            Array.Clear(_points, 0, _points.Length);
            _advancedKey = -1;
        }

        /// <summary>Series points of entrant 0-15 so far (events already closed).</summary>
        public static int PointsOf(int entrant) => _points[entrant];

        /// <summary>Start the series: every loadable event, in the given order, with these 16 entrants.</summary>
        public static bool Begin(IEnumerable<EventChoice> events, string[] entrants)
        {
            if (entrants == null || entrants.Length != EventCompetition.EntrantCount)
                throw new ArgumentException($"A series needs exactly {EventCompetition.EntrantCount} entrants.", nameof(entrants));
            _events = events.Where(e => !string.IsNullOrEmpty(e.Scene) && Application.CanStreamedLevelBeLoaded(e.Scene)).ToArray();
            if (_events.Length == 0)
            {
                Debug.LogWarning("[MeetSeries] no loadable event scenes");
                return false;
            }
            _entrants = entrants.ToArray();
            Array.Clear(_points, 0, _points.Length);
            Index = 0;
            _advancedKey = -1;
            Active = true;
            MeetSeriesRunner.Ensure();
            return LoadEvent();
        }

        static bool LoadEvent()
        {
            var ev = _events[Index];
            Debug.Log($"[MeetSeries] event {Index + 1}/{_events.Length}: E{ev.Number:00} {ev.Name}");
            if (EventCompetition.Begin(ev.Number, ev.Scene, _entrants)) return true;
            Debug.LogWarning($"[MeetSeries] could not load {ev.Scene}; series stopped");
            Active = false;
            return false;
        }

        /// <summary>"SERIES 3/11 · heat 1/2" for the HUD's title chip.</summary>
        public static string Caption =>
            $"SERIES {Index + 1}/{_events.Length} · heat {EventCompetition.HeatNumber}/{EventCompetition.HeatCount}";

        /// <summary>What comes after the heat on screen: "Heat 2", "Next event" or "Final standings".</summary>
        public static string NextLabel =>
            EventCompetition.HasNextHeat ? $"Heat {EventCompetition.HeatNumber + 1}"
            : Index + 1 < _events.Length ? "Next event" : "Final standings";

        /// <summary>The line on the result card of a heat just scored: the heat, and once the event is complete its
        /// overall top three, the rating changes and the series leaders.</summary>
        public static string HeatSummary()
        {
            string head = $"{Caption}";
            if (!EventCompetition.IsFinalHeat || !EventCompetition.CurrentHeatScored) return head;
            string elo = EventCompetition.LastEloSummary.Length > 0 ? $"\nELO {EventCompetition.LastEloSummary}" : "";
            var totals = Enumerable.Range(0, _points.Length).Select(i => _points[i] + EventCompetition.PointsOf(i)).ToArray();
            return $"{head} · {EventCompetition.FinalSummary()}{elo}\nSERIES {Standings(totals, 3)}";
        }

        /// <summary>"1. GRANDMA #03 58 · 2. KIM #08 51 · …" — the best `take` entrants by points (ties: lower number).</summary>
        static string Standings(IReadOnlyList<int> points, int take, string separator = " · ") =>
            string.Join(separator, Order(points).Take(take).Select((i, rank) => $"{rank + 1}. {_entrants[i]} #{i + 1:00} {points[i]}"));

        /// <summary>Entrant indices by points, highest first; equal points keep entrant order.</summary>
        public static IEnumerable<int> Order(IReadOnlyList<int> points) =>
            Enumerable.Range(0, points.Count).OrderByDescending(i => points[i]).ThenBy(i => i);

        /// <summary>Move on: the event's next heat, the next event, or the final standings. Called by the HUD after
        /// the result (why = null) or by the watchdog (why = what stalled). One move per heat.</summary>
        public static void Advance(string why)
        {
            if (!Active || !EventCompetition.Active) return;
            if (MeetLineup.Loading) return;               // a scene is already on its way: this request would be dropped
            int key = Index * 100 + EventCompetition.HeatNumber;
            if (_advancedKey == key) return;
            _advancedKey = key;

            if (!EventCompetition.CurrentHeatScored)
            {
                Debug.LogWarning($"[MeetSeries] E{MeetLineup.EventNumber:00} heat {EventCompetition.HeatNumber} skipped: {why ?? "not scored"}");
                EventCompetition.SkipHeat();
            }
            if (EventCompetition.HasNextHeat)
            {
                if (!EventCompetition.NextHeat()) Stop();
                return;
            }

            for (int i = 0; i < _points.Length; i++) _points[i] += EventCompetition.PointsOf(i);
            if (Index + 1 < _events.Length)
            {
                Index++;
                if (!LoadEvent()) MeetLineup.ReturnToMenu();
                return;
            }
            Finish();
        }

        static void Finish()
        {
            Active = false;
            int winner = Order(_points).First();
            Champion = _entrants[winner];
            LastResult = Standings(_points, 3, "\n");
            AgentElo.RecordTournamentWin(Champion);
            Debug.Log($"[MeetSeries] finished: {Standings(_points, 3)}");
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            MeetLineup.ReturnToMenu();
        }

        /// <summary>Leave the series (HUD menu): no winner, back to the main menu.</summary>
        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            MeetLineup.ReturnToMenu();
        }
    }

    /// <summary>The series' per-scene companion (DontDestroyOnLoad): keeps the screen awake and is the watchdog, with
    /// DemoRunner's limits — no broadcast board, stuck in Ready, a heat live too long, a result the HUD did not
    /// advance, repeated exceptions, or a scene that is simply too old.</summary>
    public class MeetSeriesRunner : MonoBehaviour
    {
        static MeetSeriesRunner _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        float _sceneStart, _phaseStart;
        BoardPhase? _phase;
        int _exceptions;
        BroadcastHud _hud;

        public static void Ensure()
        {
            if (_instance != null) { _instance.ResetScene(); return; }
            var go = new GameObject("MeetSeriesRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MeetSeriesRunner>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.logMessageReceived += OnLog;
            ResetScene();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= OnLog;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode) => ResetScene();

        void ResetScene()
        {
            _sceneStart = _phaseStart = Time.unscaledTime;
            _phase = null;
            _exceptions = 0;
            _hud = null;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception) _exceptions++;
        }

        void Update()
        {
            if (!MeetSeries.Active) return;               // idle until the next series (no Destroy in runtime code)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (SceneManager.GetActiveScene().name == MeetLineup.MenuScene) return;

            float now = Time.unscaledTime, inScene = now - _sceneStart;
            if (_hud == null) _hud = FindFirstObjectByType<BroadcastHud>();
            var board = _hud != null ? _hud.Board : null;
            if (board == null) { if (inScene > DemoRunner.NoBoardSeconds) MeetSeries.Advance("no event board"); return; }
            if (_phase != board.BoardState) { _phase = board.BoardState; _phaseStart = now; }
            float inPhase = now - _phaseStart;
            bool scored = EventCompetition.CurrentHeatScored;   // after the scored heat the board may sit in Ready (held)
            if (_exceptions >= 10) MeetSeries.Advance($"{_exceptions} exceptions");
            else if (!scored && _phase == BoardPhase.Ready && inPhase > DemoRunner.MaxReadySeconds) MeetSeries.Advance("stuck in Ready");
            else if (_phase == BoardPhase.Live && inPhase > DemoRunner.MaxLiveSeconds) MeetSeries.Advance($"heat over {DemoRunner.MaxLiveSeconds:0} s");
            else if (scored && now - _hud.ResultSince > MeetSeries.ResultSeconds + DemoRunner.ResultGraceSeconds) MeetSeries.Advance("result not advanced");
            else if (inScene > DemoRunner.MaxSceneSeconds) MeetSeries.Advance($"scene over {DemoRunner.MaxSceneSeconds:0} s");
        }
    }
}
