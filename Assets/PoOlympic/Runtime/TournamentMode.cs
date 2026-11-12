using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoOlympic
{
    /// <summary>
    /// Autonomous 16-player single-elimination tournament mode:
    ///   - Uses a 16-slot symmetric bracket (8 Left, Winner in Middle, 8 Right) filled from MeetLineup.Roster + BYE.
    ///   - Plays each real matchup automatically as a 4v4 showdown (Lanes 1-4 = Player A vs Lanes 5-8 = Player B) in a
    ///     compatible playable event scene, advancing automatically from match to match until a single Champion wins.
    ///   - Updates persistent ELO ratings (<see cref="AgentElo"/>) after every decisive match.
    /// </summary>
    public static class TournamentMode
    {
        public const float ResultSeconds = 4.5f;
        const string BracketKey = "PoOlympic.TournamentBracket";

        public readonly struct EventChoice
        {
            public readonly int Number;
            public readonly string Name;
            public readonly string Scene;
            public readonly string[] Bodies;

            public EventChoice(int number, string name, string scene, string[] bodies)
            {
                Number = number;
                Name = name ?? "";
                Scene = scene ?? "";
                Bodies = bodies ?? Array.Empty<string>();
            }

            public bool Supports(string a, string b)
            {
                if (Bodies == null || Bodies.Length == 0) return true;
                return Array.IndexOf(Bodies, a) >= 0 && Array.IndexOf(Bodies, b) >= 0;
            }
        }

        public static bool Active { get; private set; }
        public static bool ShowBracketOnMenu { get; set; }
        public static TournamentBracket.Match CurrentMatch { get; private set; }
        public static bool CurrentMatchScored { get; private set; }
        public static string LastMatchSummary { get; private set; } = "";

        static TournamentBracket _bracket;
        static EventChoice[] _events = Array.Empty<EventChoice>();
        static readonly System.Random Rng = new();
        static int _advancedKey = -1;
        static int _lastEventNumber = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            ShowBracketOnMenu = false;
            CurrentMatch = null;
            CurrentMatchScored = false;
            LastMatchSummary = "";
            _bracket = null;
            _events = Array.Empty<EventChoice>();
            _advancedKey = -1;
            _lastEventNumber = -1;
        }

        public static TournamentBracket Bracket
        {
            get
            {
                if (_bracket == null)
                {
                    _bracket = TournamentBracket.FromJson(PlayerPrefs.GetString(BracketKey, ""));
                    if (_bracket == null) ResetBracket(shuffleOrder: false);
                    else RefreshEmptyRosterChanges();
                }
                return _bracket;
            }
        }

        /// <summary>If the user hasn't started any matches yet and the roster grew (e.g. new trained characters), rebuild slots.</summary>
        static void RefreshEmptyRosterChanges()
        {
            if (_bracket == null) return;
            bool anyRealPlayed = _bracket.matches.Any(m => m.IsComplete && !m.IsByeMatch);
            if (anyRealPlayed) return;
            var expected = MeetLineup.Roster.Where(a => !AgentElo.IsBye(a)).Select(a => a.ToUpperInvariant()).OrderBy(a => a).ToArray();
            var current = _bracket.initialSlots.Where(a => !AgentElo.IsBye(a)).Select(a => a.ToUpperInvariant()).OrderBy(a => a).ToArray();
            if (!expected.SequenceEqual(current))
                ResetBracket(shuffleOrder: false);
        }

        public static TournamentBracket ResetBracket(bool shuffleOrder = false)
        {
            int nextNum = (_bracket?.tournamentNumber ?? 0) + (_bracket != null && _bracket.IsFinished ? 1 : 0);
            if (nextNum < 1) nextNum = 1;
            _bracket = TournamentBracket.Create(MeetLineup.Roster, shuffleOrder, nextNum, Rng);
            CurrentMatch = null;
            CurrentMatchScored = false;
            LastMatchSummary = "";
            SaveBracket();
            return _bracket;
        }

        public static void SaveBracket()
        {
            if (_bracket == null) return;
            PlayerPrefs.SetString(BracketKey, _bracket.ToJson());
            PlayerPrefs.Save();
        }

        public static void ClearSavedBracket()
        {
            _bracket = null;
            PlayerPrefs.DeleteKey(BracketKey);
            PlayerPrefs.Save();
        }

        /// <summary>Start or resume autonomous 16-player tournament play until a single winner is crowned.</summary>
        public static void Begin(IEnumerable<EventChoice> events, bool freshIfFinished = true)
        {
            _events = events.Where(e => !string.IsNullOrEmpty(e.Scene) && Application.CanStreamedLevelBeLoaded(e.Scene)).ToArray();
            if (_events.Length == 0)
            {
                Debug.LogWarning("[TournamentMode] no loadable event scenes");
                return;
            }
            if (freshIfFinished && Bracket.IsFinished)
                ResetBracket(shuffleOrder: false);

            Active = true;
            ShowBracketOnMenu = true;
            _advancedKey = -1;
            LastMatchSummary = "";
            TournamentRunner.Ensure();
            LaunchNextMatch();
        }

        public static EventChoice PickEventForMatch(EventChoice[] pool, string a, string b, int avoidEvent = -1, System.Random rng = null)
        {
            rng ??= Rng;
            var compat = pool.Where(e => e.Supports(a, b)).ToArray();
            if (compat.Length == 0) compat = pool;
            if (compat.Length > 1 && avoidEvent >= 0)
            {
                var diff = compat.Where(e => e.Number != avoidEvent).ToArray();
                if (diff.Length > 0) compat = diff;
            }
            return compat[rng.Next(compat.Length)];
        }

        public static string[] BuildMatchLineup(string playerA, string playerB) => new[]
        {
            playerA, playerA, playerA, playerA,
            playerB, playerB, playerB, playerB,
        };

        public static void LaunchNextMatch()
        {
            if (!Active) return;
            var b = Bracket;
            b.AutoResolveByes();
            var next = b.NextPlayableMatch();
            if (next == null)
            {
                FinishTournament();
                return;
            }

            var ev = PickEventForMatch(_events, next.playerA, next.playerB, _lastEventNumber, Rng);
            _lastEventNumber = ev.Number;
            next.eventNumber = ev.Number;
            next.eventName = ev.Name;
            CurrentMatch = next;
            CurrentMatchScored = false;

            MeetLineup.Set(BuildMatchLineup(next.playerA, next.playerB), ev.Number);
            SaveBracket();
            Debug.Log($"[TournamentMode] {TournamentBracket.RoundLabel(next.round)} M{next.index + 1}: {next.playerA} vs {next.playerB} in E{ev.Number:00} {ev.Name}");
            MeetLineup.Load(ev.Scene);
        }

        /// <summary>Score a finished 8-lane heat (Lanes 1-4 = Player A, Lanes 5-8 = Player B) and update ELO.</summary>
        public static string ScoreHeat(IEnumerable<(string name, int place)> results)
        {
            if (!Active || CurrentMatch == null || CurrentMatchScored) return LastMatchSummary;
            CurrentMatchScored = true;

            int scoreA = 0, scoreB = 0;
            int bestPlaceA = 99, bestPlaceB = 99;
            foreach (var (name, place) in results)
            {
                int lane = Gauntlet.LaneOf(name);
                if (lane < 1 || lane > 8 || place < 1 || place > Gauntlet.PointsTable.Length) continue;
                int pts = Gauntlet.PointsTable[place - 1];
                if (lane <= 4)
                {
                    scoreA += pts;
                    if (place < bestPlaceA) bestPlaceA = place;
                }
                else
                {
                    scoreB += pts;
                    if (place < bestPlaceB) bestPlaceB = place;
                }
            }

            bool aWins = scoreA > scoreB || (scoreA == scoreB && bestPlaceA <= bestPlaceB);
            string winner = aWins ? CurrentMatch.playerA : CurrentMatch.playerB;
            string loser = aWins ? CurrentMatch.playerB : CurrentMatch.playerA;

            var (wElo, lElo, delta) = AgentElo.RecordMatch(winner, loser);
            Bracket.RecordMatchResult(CurrentMatch.round, CurrentMatch.index, winner, scoreA, scoreB,
                CurrentMatch.eventNumber, CurrentMatch.eventName, delta);
            SaveBracket();

            LastMatchSummary = $"{winner} def. {loser} ({scoreA}–{scoreB}) · ELO {winner} {Mathf.RoundToInt(wElo)} (+{Mathf.RoundToInt(delta)}) · {loser} {Mathf.RoundToInt(lElo)}";
            return LastMatchSummary;
        }

        public static void Advance(string why = null)
        {
            if (!Active) return;
            if (CurrentMatch != null)
            {
                int key = Bracket.tournamentNumber * 100 + CurrentMatch.round * 16 + CurrentMatch.index;
                if (_advancedKey == key) return;
                _advancedKey = key;

                if (!CurrentMatchScored)
                {
                    // Watchdog fallback: settle by ELO/coin flip if the heat stalled
                    if (why != null) Debug.LogWarning($"[TournamentMode] match stalled ({why}), settling fallback");
                    bool aWins = Rng.NextDouble() < 0.5;
                    string winner = aWins ? CurrentMatch.playerA : CurrentMatch.playerB;
                    string loser = aWins ? CurrentMatch.playerB : CurrentMatch.playerA;
                    var (_, _, delta) = AgentElo.RecordMatch(winner, loser);
                    Bracket.RecordMatchResult(CurrentMatch.round, CurrentMatch.index, winner, aWins ? 1 : 0, aWins ? 0 : 1,
                        CurrentMatch.eventNumber, CurrentMatch.eventName, delta);
                    CurrentMatchScored = true;
                    SaveBracket();
                }
            }
            LaunchNextMatch();
        }

        static void FinishTournament()
        {
            Active = false;
            CurrentMatch = null;
            CurrentMatchScored = false;
            ShowBracketOnMenu = true;
            if (!string.IsNullOrEmpty(Bracket.champion) && !AgentElo.IsBye(Bracket.champion))
                AgentElo.RecordTournamentWin(Bracket.champion);
            SaveBracket();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Debug.Log($"[TournamentMode] Champion crowned: {Bracket.champion}");
            MeetLineup.ReturnToMenu();
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            CurrentMatch = null;
            CurrentMatchScored = false;
            ShowBracketOnMenu = true;
            SaveBracket();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            MeetLineup.ReturnToMenu();
        }
    }

    /// <summary>Per-scene companion (DontDestroyOnLoad) keeping autonomous tournament play moving if an event stalls.</summary>
    public class TournamentRunner : MonoBehaviour
    {
        static TournamentRunner _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        float _sceneStart, _phaseStart;
        BoardPhase? _phase;
        int _exceptions;
        BroadcastHud _hud;

        public static void Ensure()
        {
            if (_instance != null) { _instance.ResetScene(); return; }
            var go = new GameObject("TournamentRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TournamentRunner>();
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
            if (!TournamentMode.Active) return;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (SceneManager.GetActiveScene().name == MeetLineup.MenuScene) return;

            float now = Time.unscaledTime, inScene = now - _sceneStart;
            if (_hud == null) _hud = FindFirstObjectByType<BroadcastHud>();
            var board = _hud != null ? _hud.Board : null;
            if (board == null) { if (inScene > DemoRunner.NoBoardSeconds) TournamentMode.Advance("no event board"); return; }
            if (_phase != board.BoardState) { _phase = board.BoardState; _phaseStart = now; }
            float inPhase = now - _phaseStart;
            bool scored = TournamentMode.CurrentMatchScored;
            if (_exceptions >= 10) TournamentMode.Advance($"{_exceptions} exceptions");
            else if (!scored && _phase == BoardPhase.Ready && inPhase > DemoRunner.MaxReadySeconds) TournamentMode.Advance("stuck in Ready");
            else if (_phase == BoardPhase.Live && inPhase > DemoRunner.MaxLiveSeconds) TournamentMode.Advance($"heat over {DemoRunner.MaxLiveSeconds:0} s");
            else if (scored && now - _hud.ResultSince > TournamentMode.ResultSeconds + DemoRunner.ResultGraceSeconds) TournamentMode.Advance("result not advanced");
            else if (inScene > DemoRunner.MaxSceneSeconds) TournamentMode.Advance($"scene over {DemoRunner.MaxSceneSeconds:0} s");
        }
    }
}
