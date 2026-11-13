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
    ///   roster            one card per athlete (MeetLineup.Roster): tap = that athlete in all 16 entrant slots
    ///   entrants          2 rows of 8: the field of 16 every event is run with (the roster's characters, repeated
    ///                     as needed); tap a slot = the next athlete in it. Row 1 runs as heat 1, row 2 as heat 2.
    ///   events            full catalogue numbered 01–16
    ///   detail            the selected event: name, brain, rules and its world record — or, with ★ on, its all-time
    ///                     top 5 (the ★ toggle also puts every event's world record on its tile: the old records board)
    ///   actions           ★ records · GAUNTLET (tapping events builds an ordered series, Gauntlet; one heat per event
    ///                     with entrants 1-8) · RUN ALL (MeetSeries: every playable event with the 16 entrants,
    ///                     unattended, to one winner) · PLAY (the selected event / the gauntlet)
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

        readonly string[] _entrants = new string[EventCompetition.EntrantCount];
        /// <summary>The field picked in the menu, kept across the event scenes (the menu scene is reloaded).</summary>
        static string[] _savedEntrants;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _savedEntrants = null;

        readonly List<(Button tile, Label name)> _slots = new();
        readonly List<Button> _eventButtons = new();
        readonly List<Label> _orderBadges = new();
        readonly List<Label> _marks = new();
        readonly List<MenuEvent> _gauntlet = new();
        bool _gauntletMode, _recordsMode;
        Button _gauntletButton, _recordsButton, _runAllButton, _play;
        VisualElement _root, _panel, _slotsRoot, _detail, _recordTop;
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
            else if (idleDemoSeconds > 0f && !DemoMode.Active && !MeetSeries.Active && Time.unscaledTime - _lastInput > idleDemoSeconds) StartDemo();
        }

        /// <summary>Endless gauntlet of every playable event with random lineups (DemoMode); scenes with a fixed
        /// lineup are left out (their lineup would not match the random one).</summary>
        public void StartDemo()
        {
            if (!Application.isPlaying) return;
            _lastInput = Time.unscaledTime;
            DemoMode.Begin(events.Where(e => IsPlayable(e) && !FixedLineup(e))
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
            _rules = root.Q<Label>("eventRules");
            _eventTitle = root.Q<Label>("eventTitle");
            _eventBrain = root.Q<Label>("eventBrain");
            _eventRecord = root.Q<Label>("eventRecord");
            _recordTop = root.Q<VisualElement>("recordTop");
            _play = root.Q<Button>("playButton");
            _gauntletButton = root.Q<Button>("gauntletButton");
            _recordsButton = root.Q<Button>("recordsButton");
            _runAllButton = root.Q<Button>("runAllButton");
            _slotsRoot = slots;
            root.style.flexGrow = 1;                     // the document root fills the panel: the column is one viewport tall
            foreach (var v in new[] { slots, roster, grid, top, bottom, overlay }) v?.Clear();
            _slots.Clear();
            _eventButtons.Clear();
            _orderBadges.Clear();
            _marks.Clear();

            _anchors = new HudAnchors(top, bottom, overlay, overlay, "POOLYMPICS", "", "v" + Application.version);
            _anchors.SetMenu(MenuItems);
            // one field of 16 for every event. Kept from the last visit to the menu; else a fresh draw: each row
            // of 8 holds every character at least once, the other slots are repeats.
            var field = _savedEntrants != null && _savedEntrants.Length == _entrants.Length &&
                        _savedEntrants.All(a => Array.IndexOf(MeetLineup.Roster, a) >= 0)
                ? _savedEntrants
                : EventCompetition.DrawParticipants(MeetLineup.Roster, new System.Random());
            Array.Copy(field, _entrants, _entrants.Length);
            foreach (var ev in events) ev.participants = _entrants;
            _last = new Label();
            _last.AddToClassList("menu-last");
            _anchors.BottomCentre.Add(_last);

            foreach (var athlete in MeetLineup.Roster)
            {
                int elo = AgentElo.GetRoundedElo(athlete);
                var card = new Button(() => FillAll(athlete)) { tooltip = $"{MeetLineup.Stats(athlete).Replace("\n", " · ")} · ELO {elo} · tap: all 16 entrants" };
                card.AddToClassList("athlete-card");
                card.EnableInClassList("athlete-card--gap", athlete != MeetLineup.Roster[^1]);
                // three cards share the row: chip + name + ×8 on one line, the stats + ELO on their own line below
                var head = new VisualElement();
                head.AddToClassList("athlete-head");
                head.Add(Chip(athlete));
                head.Add(MakeLabel(athlete, "athlete-name"));
                head.Add(MakeLabel("×16", "athlete-fill"));
                card.Add(head);
                card.Add(MakeLabel($"{MeetLineup.Stats(athlete).Split('\n')[0]} · ELO {elo}", "athlete-stats"));
                roster.Add(card);
            }

            for (int k = 0; k < _entrants.Length; k++)
            {
                int slot = k;
                var tile = new Button(() => NextAthlete(slot));
                tile.AddToClassList("entrant");
                tile.Add(MakeLabel($"{k + 1:00}", "entrant-no"));
                var name = MakeLabel("", "entrant-name");
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
            if (_runAllButton != null)
            {
                _runAllButton.clicked -= RunAll;
                _runAllButton.clicked += RunAll;
            }
            RefreshSlots();
            RefreshRecords();
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
                    for (int k = 0; k < _entrants.Length; k++) _entrants[k] = MeetLineup.Roster[k % MeetLineup.Roster.Length];
                    RefreshSlots();
                });
            yield return ("Shuffle the 16 entrants", () =>
            {
                if (LineupFixed) return;
                Array.Copy(EventCompetition.DrawParticipants(MeetLineup.Roster, new System.Random()), _entrants, _entrants.Length);
                RefreshSlots();
            });
            yield return ("Run all events (16 entrants, plays by itself)", RunAll);
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
        bool LineupFixed => FixedLineup(_selected);

        static bool FixedLineup(MenuEvent e) => e?.lineup != null && e.lineup.Length > 0 && (e.lineup.Length == 8 || e.lineup.Length == EventCompetition.HeatSize);

        /// <summary>Tap an entrant slot: the next athlete of the roster in it.</summary>
        void NextAthlete(int slot)
        {
            if (LineupFixed) return;                     // lineup fixed by the event scene
            int i = Array.IndexOf(MeetLineup.Roster, _entrants[slot]);
            _entrants[slot] = MeetLineup.Roster[(i + 1) % MeetLineup.Roster.Length];
            RefreshSlots();
        }

        void FillAll(string athlete)
        {
            if (LineupFixed) return;
            for (int k = 0; k < _entrants.Length; k++) _entrants[k] = athlete;
            RefreshSlots();
        }

        void RefreshSlots()
        {
            _savedEntrants = _entrants.ToArray();
            for (int k = 0; k < _slots.Count; k++)
            {
                var (tile, name) = _slots[k];
                string a = _entrants[k];
                name.text = ShortName(a);
                tile.tooltip = $"Entrant {k + 1:00}: {a} · row {k / 8 + 1}, slot {k % 8 + 1}, lane {k + 1}";
                foreach (var r in MeetLineup.Roster) tile.EnableInClassList("entrant--" + r.ToLowerInvariant(), a == r);
                tile.EnableInClassList("entrant--sub", a != null && !InEvent(a));   // sits this event out
                tile.EnableInClassList("entrant--fixed", LineupFixed);
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

        /// <summary>RUN ALL: every playable event in turn with the 16 entrants, unattended, to one winner (MeetSeries).
        /// Scenes with a fixed lineup are left out.</summary>
        public void RunAll()
        {
            if (!Application.isPlaying) return;
            Gauntlet.Abandon();
            MeetSeries.Begin(events.Where(e => IsPlayable(e) && !FixedLineup(e))
                .Select(e => new MeetSeries.EventChoice(e.number, e.name, e.scene)), _entrants);
        }

        /// <summary>3-letter code of an athlete for the narrow entrant tiles ("GRA", "ZOM").</summary>
        public static string ShortName(string athlete)
        {
            if (string.IsNullOrEmpty(athlete)) return "?";
            return athlete.Length <= 3 ? athlete : athlete.Substring(0, 3);
        }

        void UpdateStatus()
        {
            if (_play == null || _anchors == null) return;
            bool full = _entrants.All(a => a != null);
            bool loadable = _gauntletMode
                ? _gauntlet.Count > 0 && _gauntlet.All(e => IsPlayable(e) && (!Application.isPlaying || Application.CanStreamedLevelBeLoaded(e.scene)))
                : IsPlayable(_selected) && (!Application.isPlaying || Application.CanStreamedLevelBeLoaded(_selected.scene));
            _play.SetEnabled(full && loadable);
            // the status line lives in the title chip (TL): no separate status label under PLAY
            _anchors.Sub.text = _selected == null ? "No playable events"
                : !IsPlayable(_selected) ? $"{_selected.number:00} is coming soon"
                : !full ? "Fill all 16 entrants"
                : !loadable ? $"{_selected.scene} is not in Build Settings"
                : _gauntletMode ? (_gauntlet.Count == 0 ? "Tap events to build the gauntlet"
                    : $"Gauntlet {string.Join(" → ", _gauntlet.Select(e => e.number.ToString("00")))} · 10-8-6-5-4-3-2-1")
                : SitOutNote() ?? "16 entrants · 2 rows of 8";
            _runAllButton?.SetEnabled(full && events.Any(e => IsPlayable(e) && !FixedLineup(e)));
            _last.text = (MeetSeries.LastResult.Length > 0 ? "Series: " + MeetSeries.LastResult.Replace("\n", " · ") + "\n" : "") +
                         (Gauntlet.LastResult.Length > 0 ? "Last gauntlet: " + Gauntlet.LastResult.Replace("\n", " · ") : "") +
                         (DemoSeason.SeriesPlayed > 0 ? (Gauntlet.LastResult.Length > 0 ? "\n" : "") + "Demo " + DemoSeason.Summary() : "");
        }

        /// <summary>"GRANDMA → MATT here" when picked athletes have no brain for the selected event (their lane tiles are
        /// dimmed), "MATT-only event" when the scene holds one body; null when everyone picked takes part.</summary>
        string SitOutNote()
        {
            var sitting = _entrants.Where(a => a != null && !InEvent(a)).Distinct().ToArray();
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
            if (!IsPlayable(_selected)) return;
            if (_gauntletMode && _gauntlet.Count > 0)
            {
                MeetLineup.Set(_entrants.Take(EventCompetition.HeatSize).ToArray(), _gauntlet[0].number);   // one heat per stage: entrants 1-8
                Gauntlet.Begin(_gauntlet.Select(e => e.number).ToArray(), _gauntlet.Select(e => e.scene).ToArray());
                return;
            }
            Gauntlet.Abandon();
            EventCompetition.Begin(_selected.number, _selected.scene, _entrants);
        }

        static bool IsPlayable(MenuEvent ev) => ev != null && !string.IsNullOrWhiteSpace(ev.scene);
    }
}
