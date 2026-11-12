using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PoOlympic
{
    /// <summary>
    /// Main menu (UI Toolkit, Assets/PoOlympic/UI/MainMenu.uxml) — one viewport, nothing scrolls (UI consolidation
    /// 2026-09-29):
    ///   HudAnchors frame  TL POOLYMPICS + the lineup / gauntlet status · TC FPS · TR menu → the bottom sheet (Actions:
    ///                     lineup presets, demo · Settings) · BL debug · BR version; bottom centre: the last gauntlet result
    ///   roster            one card per athlete (MeetLineup.Roster): tap = that athlete in all 8 lanes
    ///   lanes             4 × 2 tiles: tap a lane = the next athlete in it
    ///   events            full catalogue numbered 01–16; each playable event draws 16 entrants for two heats of 8
    ///   detail            the selected event: name, brain, rules and its world record — or, with ★ on, its all-time
    ///                     top 5 (the ★ toggle also puts every event's world record on its tile: the old records board)
    ///   actions           ★ records · GAUNTLET (tapping events builds an ordered series, Gauntlet; one heat per event,
    ///                     points per place) · PLAY (loads the event scene / starts the gauntlet)
    /// Runs in edit mode too, so the full layout shows in the Game view / Device Simulator without entering Play mode.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Serializable]
        public class MenuEvent
        {
            public int number;        // canonical event ID used for scene and record lookups
            public int displayNumber;
            public string name;
            public string rules;
            public string brain;
            public string scene;   // scene name in Build Settings
            public string[] lineup; // athlete per lane in the built scene (MATT / ZOMBIE); scenes are built per lineup
            [Tooltip("Sixteen random entrants from the current agent roster; duplicates are allowed.")]
            public string[] participants;
            [Tooltip("Roster athletes the event scene holds (empty = the whole roster). An athlete without a brain for " +
                     "the event sits out: LaneLineup runs the first of these in that lane.")]
            public string[] bodies;
        }

        public List<MenuEvent> events = new();
        [Tooltip("Event tiles per row of the grid.")]
        public int columns = 3;
        [Tooltip("Seconds without a tap / key before demo mode starts on its own (0 = never).")]
        public float idleDemoSeconds = 45f;
        float _lastInput;

        readonly string[] _lineup = new string[8];
        readonly List<(Button tile, Label name)> _slots = new();
        readonly List<Button> _eventButtons = new();
        readonly List<Label> _orderBadges = new();
        readonly List<Label> _marks = new();
        readonly List<MenuEvent> _gauntlet = new();
        bool _gauntletMode, _recordsMode, _tournamentMode;
        Button _gauntletButton, _recordsButton, _tournamentButton, _reseedButton, _play;
        VisualElement _root, _panel, _slotsRoot, _detail, _recordTop, _tournamentView;
        MenuEvent _selected;
        Label _rules, _eventRecord, _eventTitle, _eventBrain, _last;
        HudAnchors _anchors;

        public HudAnchors Anchors => _anchors;

        void OnEnable()
        {
            _lastInput = Time.unscaledTime;
            Populate();
        }

        void Update()
        {
            if (Application.isPlaying) IdleDemo();
            // the UIDocument rebuilds its tree when its assets reload: repopulate when our elements are gone
            if (_slotsRoot == null || _slotsRoot.panel == null || _slotsRoot.childCount == 0) Populate();
            if (_anchors == null) return;
            _anchors.Tick();
            FitSafeArea();
        }

        /// <summary>Demo mode after idleDemoSeconds without a tap, click, key or gamepad button.</summary>
        void IdleDemo()
        {
            if (DemoRunner.AnyInput(held: true)) _lastInput = Time.unscaledTime;
            else if (idleDemoSeconds > 0f && !DemoMode.Active && !TournamentMode.Active && Time.unscaledTime - _lastInput > idleDemoSeconds) StartDemo();
        }

        /// <summary>Endless gauntlet of every playable event with random lineups (DemoMode); scenes with a fixed
        /// lineup are left out (their lineup would not match the random one).</summary>
        public void StartDemo()
        {
            if (!Application.isPlaying) return;
            _lastInput = Time.unscaledTime;
            DemoMode.Begin(events.Where(e => IsPlayable(e) && (e.lineup == null || e.lineup.Length != _lineup.Length))
                .Select(e => (e.number, e.scene)));
        }

        /// <summary>Keep the column inside the safe area (status bar, camera cut-out, navigation bar).</summary>
        public void FitSafeArea()
        {
            var full = _root?.panel?.visualTree.layout ?? Rect.zero;
            if (float.IsNaN(full.width) || full.width <= 1f) return;
            var safe = HudAnchors.SafeRectIn(full.size);
            _root.style.paddingLeft = safe.xMin;
            _root.style.paddingTop = safe.yMin;
            _root.style.paddingRight = Mathf.Max(0f, full.width - safe.xMax);
            _root.style.paddingBottom = Mathf.Max(0f, full.height - safe.yMax);
        }

        public void Populate()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc != null ? doc.rootVisualElement : null;
            var slots = root?.Q<VisualElement>("slots");
            if (slots == null) return;
            _root = root.Q<VisualElement>("root") ?? root;
            _panel = root.Q<VisualElement>("panel");
            var roster = root.Q<VisualElement>("roster");
            var grid = root.Q<VisualElement>("eventList");
            var top = root.Q<VisualElement>("anchorTop");
            var bottom = root.Q<VisualElement>("anchorBottom");
            var overlay = root.Q<VisualElement>("overlay");
            _detail = root.Q<VisualElement>("detail");
            _tournamentView = root.Q<VisualElement>("tournamentView");
            _rules = root.Q<Label>("eventRules");
            _eventTitle = root.Q<Label>("eventTitle");
            _eventBrain = root.Q<Label>("eventBrain");
            _eventRecord = root.Q<Label>("eventRecord");
            _recordTop = root.Q<VisualElement>("recordTop");
            _play = root.Q<Button>("playButton");
            _gauntletButton = root.Q<Button>("gauntletButton");
            _recordsButton = root.Q<Button>("recordsButton");
            _tournamentButton = root.Q<Button>("tournamentButton");
            _reseedButton = root.Q<Button>("reseedButton");
            _slotsRoot = slots;
            root.style.flexGrow = 1;                     // the document root fills the panel: the column is one viewport tall
            foreach (var v in new[] { slots, roster, grid, top, bottom, overlay, _tournamentView }) v?.Clear();
            _slots.Clear();
            _eventButtons.Clear();
            _orderBadges.Clear();
            _marks.Clear();

            _anchors = new HudAnchors(top, bottom, overlay, overlay, "POOLYMPICS", "", "v" + Application.version);
            _anchors.SetMenu(MenuItems);
            var participantRng = new System.Random();
            foreach (var ev in events)
                ev.participants = EventCompetition.DrawParticipants(MeetLineup.Roster, participantRng);
            _last = new Label();
            _last.AddToClassList("menu-last");
            _anchors.BottomCentre.Add(_last);

            foreach (var athlete in MeetLineup.Roster)
            {
                int elo = AgentElo.GetRoundedElo(athlete);
                var card = new Button(() => FillAll(athlete)) { tooltip = $"{MeetLineup.Stats(athlete).Replace("\n", " · ")} · ELO {elo}" };
                card.AddToClassList("athlete-card");
                card.EnableInClassList("athlete-card--gap", athlete != MeetLineup.Roster[^1]);
                // three cards share the row: chip + name + ×8 on one line, the stats + ELO on their own line below
                var head = new VisualElement();
                head.AddToClassList("athlete-head");
                head.Add(Chip(athlete));
                head.Add(MakeLabel(athlete, "athlete-name"));
                head.Add(MakeLabel("×8", "athlete-fill"));
                card.Add(head);
                card.Add(MakeLabel($"{MeetLineup.Stats(athlete).Split('\n')[0]} · ELO {elo}", "athlete-stats"));
                roster.Add(card);
            }

            for (int k = 0; k < _lineup.Length; k++)
            {
                string a = MeetLineup.Athletes[k];
                _lineup[k] = Array.IndexOf(MeetLineup.Roster, a) >= 0 ? a : MeetLineup.Roster[0];
                int lane = k;
                var tile = new Button(() => NextAthlete(lane));
                tile.AddToClassList("slot");
                tile.Add(MakeLabel($"LANE {k + 1}", "slot-lane"));
                var name = MakeLabel("", "slot-athlete");
                tile.Add(name);
                slots.Add(tile);
                _slots.Add((tile, name));
            }

            int cols = Mathf.Max(1, columns);
            VisualElement row = null;
            for (int i = 0; i < events.Count || i % cols != 0; i++)
            {
                if (i % cols == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("event-row");
                    row.EnableInClassList("event-row--gap", i + cols < events.Count);
                    grid.Add(row);
                }
                string gap = i % cols < cols - 1 ? "event-button--gap" : "event-button--end";
                if (i >= events.Count) { var pad = new VisualElement(); pad.AddToClassList("event-button"); pad.AddToClassList(gap); pad.AddToClassList("event-button--empty"); row.Add(pad); continue; }
                var ev = events[i];
                bool playable = IsPlayable(ev);
                var button = new Button(() => SelectEvent(ev))
                {
                    tooltip = playable
                        ? $"{ev.brain} · 16 participants, 2 heats of 8"
                        : "Coming soon — playable event scene is not built yet."
                };
                button.SetEnabled(playable);
                button.AddToClassList("event-button");
                button.AddToClassList(gap);
                button.EnableInClassList("event-button--coming-soon", !playable);
                button.Add(MakeLabel($"{(ev.displayNumber > 0 ? ev.displayNumber : i + 1):00}", "event-number"));
                button.Add(MakeLabel(ev.name, "event-name"));
                var mark = MakeLabel("", "event-mark");
                mark.EnableInClassList("event-mark--unavailable", !playable);
                button.Add(mark);
                var order = MakeLabel("", "event-order");
                order.style.display = DisplayStyle.None;
                button.Add(order);
                row.Add(button);
                _eventButtons.Add(button);
                _orderBadges.Add(order);
                _marks.Add(mark);
            }

            _play.clicked -= Play;
            _play.clicked += Play;
            _gauntletButton.clicked -= ToggleGauntlet;
            _gauntletButton.clicked += ToggleGauntlet;
            _recordsButton.clicked -= ToggleRecords;
            _recordsButton.clicked += ToggleRecords;
            if (_tournamentButton != null)
            {
                _tournamentButton.clicked -= ToggleTournament;
                _tournamentButton.clicked += ToggleTournament;
            }
            if (_reseedButton != null)
            {
                _reseedButton.clicked -= ReseedTournament;
                _reseedButton.clicked += ReseedTournament;
            }
            _tournamentMode = TournamentMode.ShowBracketOnMenu;
            RefreshSlots();
            RefreshRecords();
            RefreshTournament();
            SelectEvent(events.FirstOrDefault(e => e.number == MeetLineup.EventNumber && IsPlayable(e))
                ?? events.FirstOrDefault(IsPlayable));
        }

        IEnumerable<(string, Action)> MenuItems()
        {
            foreach (var a in MeetLineup.Roster) yield return ($"All {a}", () => FillAll(a));
            if (MeetLineup.Roster.Length > 1)
                yield return ("Alternate " + string.Join(" / ", MeetLineup.Roster), () =>
                {
                    if (LineupFixed) return;
                    for (int k = 0; k < _lineup.Length; k++) _lineup[k] = MeetLineup.Roster[k % MeetLineup.Roster.Length];
                    RefreshSlots(updateSelectedParticipants: true);
                });
            yield return (_tournamentMode ? "16-player bracket: hide" : "16-player tournament bracket", ToggleTournament);
            yield return ($"Demo mode (starts by itself after {idleDemoSeconds:0} s idle)", StartDemo);
            if (DemoSeason.SeriesPlayed > 0) yield return ("Demo season: reset", () => { DemoSeason.Reset(); UpdateStatus(); });
        }

        static Label Chip(string athlete)
        {
            var chip = MakeLabel(athlete.Substring(0, 1), "menu-chip");
            chip.AddToClassList("menu-chip--" + athlete.ToLowerInvariant());
            return chip;
        }

        /// <summary>The selected event's scene holds this athlete (roster scenes: every body with a brain for the event).</summary>
        bool InEvent(string athlete) =>
            _selected?.bodies == null || _selected.bodies.Length == 0 || Array.IndexOf(_selected.bodies, athlete) >= 0;

        /// <summary>Who runs in the event for a picked athlete without a brain for it (LaneLineup's stand-in).</summary>
        string StandIn => _selected?.bodies != null && _selected.bodies.Length > 0 ? _selected.bodies[0] : MeetLineup.Roster[0];

        /// <summary>The selected event's scene fixes the lineup (athlete taps are ignored). Unity serialises a null
        /// array as an empty one, so "any lineup" (roster scenes) arrives here as lineup = [] — only a full 8-lane
        /// lineup is fixed.</summary>
        bool LineupFixed => _selected?.lineup != null && _selected.lineup.Length == _lineup.Length;

        /// <summary>Tap a lane: the next athlete of the roster in it.</summary>
        void NextAthlete(int lane)
        {
            if (LineupFixed) return;                     // lineup fixed by the event scene
            int i = Array.IndexOf(MeetLineup.Roster, _lineup[lane]);
            _lineup[lane] = MeetLineup.Roster[(i + 1) % MeetLineup.Roster.Length];
            RefreshSlots(updateSelectedParticipants: true);
        }

        void FillAll(string athlete)
        {
            if (LineupFixed) return;
            for (int k = 0; k < _lineup.Length; k++) _lineup[k] = athlete;
            RefreshSlots(updateSelectedParticipants: true);
        }

        void RefreshSlots(bool updateSelectedParticipants = false)
        {
            if (updateSelectedParticipants && _selected?.participants != null &&
                _selected.participants.Length == EventCompetition.EntrantCount)
                Array.Copy(_lineup, _selected.participants, _lineup.Length);
            for (int k = 0; k < _slots.Count; k++)
            {
                var (tile, name) = _slots[k];
                name.text = _lineup[k] ?? "empty";
                foreach (var a in MeetLineup.Roster) tile.EnableInClassList("slot--" + a.ToLowerInvariant(), _lineup[k] == a);
                tile.EnableInClassList("slot--sub", _lineup[k] != null && !InEvent(_lineup[k]));   // sits this event out
                tile.EnableInClassList("slot--fixed", LineupFixed);
            }
            UpdateStatus();
        }

        void ToggleGauntlet()
        {
            _gauntletMode = !_gauntletMode;
            _gauntlet.Clear();
            if (_gauntletMode && _selected != null) _gauntlet.Add(_selected);
            RefreshGauntlet();
        }

        void RefreshGauntlet()
        {
            _gauntletButton.text = _gauntletMode ? $"GAUNTLET {_gauntlet.Count}" : "GAUNTLET";
            _gauntletButton.EnableInClassList("gauntlet-toggle--on", _gauntletMode);
            for (int i = 0; i < _orderBadges.Count && i < events.Count; i++)
            {
                int at = _gauntlet.IndexOf(events[i]);
                _orderBadges[i].style.display = _gauntletMode && at >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _orderBadges[i].text = (at + 1).ToString();
                _eventButtons[i].EnableInClassList("event-button--in-gauntlet", _gauntletMode && at >= 0);
            }
            _play.text = _gauntletMode ? $"PLAY {_gauntlet.Count}" : "PLAY";
            UpdateStatus();
        }

        void ToggleRecords()
        {
            _recordsMode = !_recordsMode;
            RefreshRecords();
        }

        /// <summary>★: every tile shows its event's world record and the detail panel the selected event's top 5.</summary>
        void RefreshRecords()
        {
            _recordsButton.EnableInClassList("records-button--on", _recordsMode);
            _detail.EnableInClassList("detail--records", _recordsMode);
            for (int i = 0; i < _marks.Count && i < events.Count; i++)
            {
                bool playable = IsPlayable(events[i]);
                var top = playable ? Records.Top(events[i].number) : null;
                _marks[i].text = !playable ? "SOON" : top.Count > 0 ? top[0].mark : "no mark yet";
                _marks[i].EnableInClassList("event-mark--none", playable && top.Count == 0);
                _marks[i].EnableInClassList("event-mark--unavailable", !playable);
                _marks[i].style.display = _recordsMode || !playable ? DisplayStyle.Flex : DisplayStyle.None;
            }
            FillTop5();
        }

        void FillTop5()
        {
            _recordTop.Clear();
            if (_selected == null) return;
            var top = Records.Top(_selected.number);
            if (top.Count == 0) _recordTop.Add(MakeLabel("No mark yet: the first valid winning mark sets it.", "record-top-none"));
            for (int i = 0; i < top.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("record-top-row");
                row.Add(MakeLabel((i + 1).ToString(), "record-top-rank"));
                row.Add(MakeLabel(top[i].mark, "record-top-mark"));
                row.Add(MakeLabel(top[i].holder + (string.IsNullOrEmpty(top[i].body) ? "" : $" · {top[i].body}"), "record-top-who"));
                row.Add(MakeLabel(top[i].date ?? "", "record-top-date"));
                _recordTop.Add(row);
            }
        }

        void SelectEvent(MenuEvent ev)
        {
            if (!IsPlayable(ev)) return;
            if (_gauntletMode && ev != null)
            {
                if (!_gauntlet.Remove(ev)) _gauntlet.Add(ev);   // tap = add to the end / remove
                RefreshGauntlet();
            }
            _selected = ev;
            if (LineupFixed) Array.Copy(ev.lineup, _lineup, _lineup.Length);   // the scene's own lineup
            else if (ev.participants != null && ev.participants.Length == EventCompetition.EntrantCount)
                Array.Copy(ev.participants, _lineup, _lineup.Length);
            RefreshSlots();
            for (int i = 0; i < _eventButtons.Count; i++)
                _eventButtons[i].EnableInClassList("event-button--selected", events[i] == ev);
            int displayNumber = ev != null && ev.displayNumber > 0 ? ev.displayNumber : ev != null ? events.IndexOf(ev) + 1 : 0;
            _eventTitle.text = ev != null ? $"{displayNumber:00}  {ev.name}" : "";
            _eventBrain.text = ev?.brain ?? "";
            _eventBrain.style.display = string.IsNullOrEmpty(ev?.brain) ? DisplayStyle.None : DisplayStyle.Flex;
            _rules.text = ev?.rules ?? "";
            _eventRecord.text = ev != null && Records.TryGet(ev.number, out _, out var wr) ? $"★ WORLD RECORD  {wr}" : ev != null ? "★ WORLD RECORD  none yet" : "";
            FillTop5();
            UpdateStatus();
        }

        /// <summary>Toggle the 16-player symmetric tournament bracket view (8 Left · Winner Middle · 8 Right).</summary>
        public void ToggleTournament()
        {
            _tournamentMode = !_tournamentMode;
            TournamentMode.ShowBracketOnMenu = _tournamentMode;
            RefreshTournament();
            UpdateStatus();
        }

        public void ReseedTournament()
        {
            TournamentMode.ResetBracket(shuffleOrder: true);
            RefreshTournament();
            UpdateStatus();
        }

        TournamentMode.EventChoice[] TournamentChoices() =>
            events.Where(e => IsPlayable(e) && (e.lineup == null || e.lineup.Length != _lineup.Length))
                  .Select(e => new TournamentMode.EventChoice(e.number, e.name, e.scene, e.bodies))
                  .ToArray();

        void RefreshTournament()
        {
            _panel?.EnableInClassList("menu-panel--tourney", _tournamentMode);
            _tournamentButton?.EnableInClassList("tourney-toggle--on", _tournamentMode);
            if (_tournamentButton != null) _tournamentButton.text = _tournamentMode ? "EVENTS" : "TOURNEY";
            if (_tournamentView == null) return;
            _tournamentView.Clear();
            if (!_tournamentMode)
            {
                if (_play != null) _play.text = _gauntletMode ? $"PLAY {_gauntlet.Count}" : "PLAY";
                return;
            }

            var bracket = TournamentMode.Bracket;
            var next = bracket.NextPlayableMatch();
            var finalMatch = bracket.GetMatch(3, 0);
            int activeCount = MeetLineup.Roster.Length;
            int playedReal = bracket.matches.Count(m => m.IsComplete && !m.IsByeMatch);

            var head = new VisualElement();
            head.AddToClassList("tourney-head");
            head.Add(MakeLabel($"TOURNAMENT #{bracket.tournamentNumber} · 16 SLOTS (8 LEFT · WINNER · 8 RIGHT)", "tourney-title"));
            head.Add(MakeLabel(bracket.IsFinished ? $"CHAMPION: {bracket.champion}" : $"{activeCount}/16 ACTIVE · {playedReal} MATCH{(playedReal == 1 ? "" : "ES")}", "tourney-count"));
            _tournamentView.Add(head);

            string eloSummary = "ELO  " + string.Join(" · ", AgentElo.Leaderboard(MeetLineup.Roster).Select(e => $"{e.agent} {Mathf.RoundToInt(e.elo)}"));
            _tournamentView.Add(MakeLabel(eloSummary, "tourney-elo-bar"));

            var board = new VisualElement();
            board.AddToClassList("tourney-bracket");

            // Col 0: Left 8 starting players (matches 0..3 -> 4 pairs)
            var leftR16 = MakeCol("tourney-col--r16");
            for (int m = 0; m < 4; m++) leftR16.Add(BuildR16Pair(bracket.GetMatch(0, m), next, isRight: false));
            board.Add(leftR16);

            // Col 1: Left QF (round 1, matches 0..1)
            var leftQf = MakeCol("tourney-col--qf");
            for (int m = 0; m < 2; m++) leftQf.Add(BuildRoundNode(bracket.GetMatch(1, m), next, "L-QF"));
            board.Add(leftQf);

            // Col 2: Left SF (round 2, match 0)
            var leftSf = MakeCol("tourney-col--sf");
            leftSf.Add(BuildRoundNode(bracket.GetMatch(2, 0), next, "L-SF"));
            board.Add(leftSf);

            // Col 3: Winner in the middle + Grand Final
            var center = MakeCol("tourney-col--center");
            var champCard = new VisualElement();
            champCard.AddToClassList("tourney-champion-card");
            champCard.Add(MakeLabel("WINNER", "tourney-champion-cap"));
            champCard.Add(MakeLabel(bracket.IsFinished ? bracket.champion : "TBD", "tourney-champion-name"));
            champCard.Add(MakeLabel(bracket.IsFinished ? $"ELO {AgentElo.GetRoundedElo(bracket.champion)}" : $"{16 - activeCount} BYE SLOTS", "tourney-champion-elo"));
            center.Add(champCard);

            var finalCard = new VisualElement();
            finalCard.AddToClassList("tourney-final-card");
            finalCard.Add(MakeLabel("FINAL", "tourney-final-cap"));
            string leftF = string.IsNullOrEmpty(finalMatch?.playerA) ? "LEFT" : finalMatch.playerA;
            string rightF = string.IsNullOrEmpty(finalMatch?.playerB) ? "RIGHT" : finalMatch.playerB;
            finalCard.Add(MakeLabel($"{leftF} vs {rightF}", "tourney-final-vs"));
            string finalStatus = finalMatch != null && finalMatch.IsComplete
                ? (finalMatch.IsByeMatch ? $"{finalMatch.winner} (BYE)" : $"{finalMatch.winner} {finalMatch.scoreA}-{finalMatch.scoreB}")
                : next != null ? $"NEXT: {next.playerA} v {next.playerB}" : "READY";
            finalCard.Add(MakeLabel(finalStatus, "tourney-final-status"));
            center.Add(finalCard);
            board.Add(center);

            // Col 4: Right SF (round 2, match 1)
            var rightSf = MakeCol("tourney-col--sf");
            rightSf.Add(BuildRoundNode(bracket.GetMatch(2, 1), next, "R-SF"));
            board.Add(rightSf);

            // Col 5: Right QF (round 1, matches 2..3)
            var rightQf = MakeCol("tourney-col--qf");
            for (int m = 2; m < 4; m++) rightQf.Add(BuildRoundNode(bracket.GetMatch(1, m), next, "R-QF"));
            board.Add(rightQf);

            // Col 6: Right 8 starting players (matches 4..7 -> 4 pairs)
            var rightR16 = MakeCol("tourney-col--r16");
            for (int m = 4; m < 8; m++) rightR16.Add(BuildR16Pair(bracket.GetMatch(0, m), next, isRight: true));
            board.Add(rightR16);

            _tournamentView.Add(board);
            if (_play != null) _play.text = bracket.IsFinished ? "NEW TOURNEY" : "START TOURNEY";
        }

        static VisualElement MakeCol(string cls)
        {
            var col = new VisualElement();
            col.AddToClassList("tourney-col");
            col.AddToClassList(cls);
            return col;
        }

        static VisualElement BuildR16Pair(TournamentBracket.Match match, TournamentBracket.Match next, bool isRight)
        {
            var pair = new VisualElement();
            pair.AddToClassList("tourney-pair");
            pair.AddToClassList(isRight ? "tourney-pair--right" : "tourney-pair--left");
            bool isActive = match != null && next != null && match.round == next.round && match.index == next.index;
            pair.Add(BuildSlotTile(match?.playerA, match?.winner == match?.playerA, isActive));
            pair.Add(BuildSlotTile(match?.playerB, match?.winner == match?.playerB, isActive));
            return pair;
        }

        static VisualElement BuildSlotTile(string athlete, bool isWinner, bool isActive)
        {
            bool isBye = AgentElo.IsBye(athlete);
            string display = isBye ? TournamentBracket.Bye : athlete;
            var slot = new VisualElement();
            slot.AddToClassList("tourney-slot");
            if (isBye) slot.AddToClassList("tourney-slot--bye");
            else slot.AddToClassList("tourney-slot--" + athlete.ToLowerInvariant());
            slot.EnableInClassList("tourney-slot--winner", isWinner && !isBye);
            slot.EnableInClassList("tourney-slot--active", isActive && !isBye);

            var name = MakeLabel(display, "tourney-slot-name");
            name.EnableInClassList("tourney-slot-name--bye", isBye);
            slot.Add(name);
            slot.Add(MakeLabel(isBye ? "—" : $"{AgentElo.GetRoundedElo(athlete)}", "tourney-slot-elo"));
            return slot;
        }

        static VisualElement BuildRoundNode(TournamentBracket.Match match, TournamentBracket.Match next, string tag)
        {
            var node = new VisualElement();
            node.AddToClassList("tourney-node");
            bool isActive = match != null && next != null && match.round == next.round && match.index == next.index;
            bool hasWinner = match != null && !string.IsNullOrEmpty(match.winner);
            node.EnableInClassList("tourney-node--winner", hasWinner && !AgentElo.IsBye(match.winner));
            node.EnableInClassList("tourney-node--active", isActive);

            string primary = tag;
            string sub = "TBD";
            if (match != null)
            {
                if (hasWinner)
                {
                    primary = match.winner;
                    sub = match.IsByeMatch ? "BYE" : $"{match.scoreA}-{match.scoreB}";
                }
                else if (match.IsPlayable)
                {
                    primary = $"{ShortName(match.playerA)}v{ShortName(match.playerB)}";
                    sub = isActive ? "NEXT" : tag;
                }
                else if (!string.IsNullOrEmpty(match.playerA) || !string.IsNullOrEmpty(match.playerB))
                {
                    primary = !string.IsNullOrEmpty(match.playerA) ? match.playerA : match.playerB;
                    sub = "WAIT";
                }
            }
            node.Add(MakeLabel(primary, "tourney-node-text"));
            node.Add(MakeLabel(sub, "tourney-node-sub"));
            return node;
        }

        static string ShortName(string athlete)
        {
            if (string.IsNullOrEmpty(athlete)) return "?";
            return athlete.Length <= 3 ? athlete : athlete.Substring(0, 3);
        }

        void UpdateStatus()
        {
            if (_play == null || _anchors == null) return;
            if (_tournamentMode)
            {
                var bracket = TournamentMode.Bracket;
                var choices = TournamentChoices();
                bool playable = choices.Any(e => !Application.isPlaying || Application.CanStreamedLevelBeLoaded(e.Scene));
                _play.SetEnabled(playable);
                var next = bracket.NextPlayableMatch();
                _anchors.Sub.text = bracket.IsFinished
                    ? $"Champion: {bracket.champion} · ELO {AgentElo.GetRoundedElo(bracket.champion)}"
                    : next != null
                        ? $"Auto Bracket · Next: {next.playerA} vs {next.playerB}"
                        : "16-Player Autonomous Tournament";
                _last.text = TournamentMode.LastMatchSummary;
                return;
            }
            bool full = _lineup.All(a => a != null);
            bool loadable = _gauntletMode
                ? _gauntlet.Count > 0 && _gauntlet.All(e => IsPlayable(e) && (!Application.isPlaying || Application.CanStreamedLevelBeLoaded(e.scene)))
                : IsPlayable(_selected) && (!Application.isPlaying || Application.CanStreamedLevelBeLoaded(_selected.scene));
            _play.SetEnabled(full && loadable);
            // the status line lives in the title chip (TL): no separate status label under PLAY
            _anchors.Sub.text = _selected == null ? "No playable events"
                : !IsPlayable(_selected) ? $"{_selected.number:00} is coming soon"
                : !full ? "Fill all 8 lanes"
                : !loadable ? $"{_selected.scene} is not in Build Settings"
                : _gauntletMode ? (_gauntlet.Count == 0 ? "Tap events to build the gauntlet"
                    : $"Gauntlet {string.Join(" → ", _gauntlet.Select(e => e.number.ToString("00")))} · 10-8-6-5-4-3-2-1")
                : _tournamentMode
                    ? SitOutNote() ?? $"{string.Join(" · ", _lineup.GroupBy(a => a).Select(g => $"{g.Count()}× {g.Key}"))}"
                    : "16 entrants · 2 heats";
            _last.text = (Gauntlet.LastResult.Length > 0 ? "Last gauntlet: " + Gauntlet.LastResult.Replace("\n", " · ") : "") +
                         (DemoSeason.SeriesPlayed > 0 ? (Gauntlet.LastResult.Length > 0 ? "\n" : "") + "Demo " + DemoSeason.Summary() : "");
        }

        /// <summary>"GRANDMA → MATT here" when picked athletes have no brain for the selected event (their lane tiles are
        /// dimmed), "MATT-only event" when the scene holds one body; null when everyone picked takes part.</summary>
        string SitOutNote()
        {
            var sitting = _lineup.Where(a => a != null && !InEvent(a)).Distinct().ToArray();
            if (sitting.Length == 0) return null;
            return _selected.bodies.Length == 1 ? $"{StandIn}-only event" : $"{string.Join(", ", sitting)} → {StandIn} here";
        }

        static Label MakeLabel(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        void Play()
        {
            if (!Application.isPlaying) return;
            if (_tournamentMode)
            {
                TournamentMode.Begin(TournamentChoices(), freshIfFinished: true);
                return;
            }
            if (!IsPlayable(_selected)) return;
            if (_gauntletMode && _gauntlet.Count > 0)
            {
                MeetLineup.Set(_lineup, _gauntlet[0].number);
                Gauntlet.Begin(_gauntlet.Select(e => e.number).ToArray(), _gauntlet.Select(e => e.scene).ToArray());
                return;
            }
            Gauntlet.Abandon();
            EventCompetition.Begin(_selected.number, _selected.scene, _selected.participants);
        }

        static bool IsPlayable(MenuEvent ev) => ev != null && !string.IsNullOrWhiteSpace(ev.scene);
    }
}
