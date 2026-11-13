# 2026-11-12 — Bracket removed; 16 entrants per event; RUN ALL series; ELO from placings

## Policy scope (`AGENTS.md` §3)

- **Policies touched: none.** No brain, observation/action interface, MuJoCo asset, task config or event rule changes. Game-flow layer and menu only (`visuals → none`, game flow above the directors).
- Regression after the change: `PortraitLayoutTests` 16/16, `SceneIntegrityTests` 15/15, `SeriesAndVoiceTests` 7/7, `RuntimeRulesTests` 2/2, `UiTokenTests` 1/1, PlayMode `StateFlowTests` 1/1. A full RUN ALL in the editor: 11 events × 2 heats, ~13 min, 0 console errors, no watchdog skips.

## What changed

- **Removed** the 16-player bracket mode: `TournamentBracket.cs`, `TournamentMode.cs`, the bracket view / TOURNEY / RESEED in `MainMenu.uxml|uss` and `MainMenuController`, its tests, the BYE / COMING SOON placeholders.
- **Menu:** the 8 lane tiles are now the field of 16 entrants, 2 rows of 8 (`.entrant`). One field for every event, kept across scene loads (`MainMenuController._savedEntrants`); a fresh draw puts every character in each row at least once. Tap a slot = next character; roster card = that character ×16; sheet: Shuffle, Alternate.
- **RUN ALL** (`MeetSeries.cs`): every playable event in turn with the 16 entrants, unattended. Points per entrant (10-8-6-5-4-3-2-1 by place in its heat) add up; after the last event the menu shows the top three and the winner's character gets a series win (`AgentElo.RecordTournamentWin`). `MeetSeriesRunner` is the watchdog (DemoRunner's limits): a stalled heat is skipped with no points and its event is not rated.
- **ELO from placings** (`AgentElo.RecordEvent`, called by `EventCompetition.ScoreHeat` when an event's last heat is scored — single events and the series alike): each character's mark is its best entrant; every pair of characters is one game at K/(characters−1); zero-sum; expectations from the ratings before the event.
- **Fix:** `BroadcastHud.LeaveMatch` ignored nothing while a load was pending, so a double tap on OK after heat 1 abandoned the event (heat 2 then loaded outside a competition). `StateFlowTests` had been failing on this since the two-heat change (`3f7ad5c`); the test now plays both heats and restores the ELO table.

## Not done: all 16 in one scene (two rows of 8)

Requested, not built. It is a physics-scene change, not a layout change:

- `training/tools/build_mjcf.py` / `compose_mixed.py` give lane k three collision bits: upper body `k`, legs `k+8`, crowd `16+k` (`shift_bits`, `CROWD_SHIFT = 16`, `ALL_BITS = 0xFFFF`). 8 lanes use 24 of MuJoCo's 32 contype/conaffinity bits; 16 lanes would need 48. A 16-athlete scene needs a new contact scheme (e.g. explicit `<contact><pair>/<exclude>` or one shared crowd bit plus per-body self-collision excludes).
- Contype/conaffinity are part of the per-lane model fingerprint that brains are tied to; a new scheme has to keep each athlete's own fingerprint (or be introduced as a new physics version with every policy's regression scenarios re-run).
- Venue lane origins (`venues.json`, 8 lanes per event), pedestals / beam / shaker platforms / cube pool (2 cubes per lane) and each event's rules are 8-lane. A back row needs per-event decisions (staggered start line and timing offsets for races, a second beam or a longer one for the Iron Pedestal, ring radius for the turntable …).
- Runtime: `LaneLineup`, the HUD (8 rows, lane parsing `L3`/`Z3`), `Gauntlet` (8 points), DemoLineups, cameras, `CrowdContactTests`.
- Cost: with 8 athletes the editor ran events at 20–35 fps; 16 doubles physics and inference.

Suggested order: (1) contact scheme + `compose_mixed.py` for 16 with `--verify` on per-athlete fingerprints, (2) one event (30m All Fours: flat, no props) as a 16-athlete trial scene to measure frame rate on WebGL and a phone, (3) the other events one by one. Until then the 16 entrants run as two heats of 8.
