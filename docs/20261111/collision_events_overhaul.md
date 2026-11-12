# 2026-11-11 — Collision Events Overhaul (Remove 6 Zero-Contact, Add 8 High-Contact)

## Policy Impact Matrix (`AGENTS.md` §3)
- **Policies touched:** **None.** All deployed ONNX brains, contracts v3/v4, observation/action layouts, and training task configs remain bit-identical.
- **Events removed from playable programme (6 zero-collision events):**
  - `#2 Torso Archer`, `#3 Deep Squat Endurance`, `#4 Precision Javelin Reach`, `#6 The Flamingo Classic`, `#7 Cadence March` (`3.00 m` grid stations)
  - `#10 Crab Shuffle Relay` (`1.22 m` railed lanes)
- **Events added to playable programme (8 high-collision events, using existing `Rung 2` + `Get-up` brains):**
  - `#2 Sumo Ring` (`sumo8` — `2.9 m` raised circular disc, inward brawl, ring-out elimination)
  - `#3 Narrow Bridge Crossing` (`bridge8` — `14 m × 1.44 m` elevated bridge, 4v4 head-on crossing)
  - `#4 Funnel Bottleneck Sprint` (`funnel8` — `35 m` sprint through two `1.8 m` chicane gates)
  - `#6 Figure-8 Crossroad Dash` (`crossroad8` — perpendicular 4+4 intersection crossing)
  - `#7 Shrinking Safe-Zone` (`pads8` — musical-pads elimination in open arena)
  - `#10 Center Beacon Scramble` (`beacon8` — `30 s` King-of-the-Hill center melee with fall recovery)
  - `#18 Inside-Rail Pack Mile` (`track8` — `84.39 m` immediate inside-rail merge pack race)
  - `#25 Hilltop Scramble` (`hill8` — 4-way `1.5°` ramp climb to elevated `2.6 m × 2.6 m` summit brawl)
- **Runtime / Editor / Python modules touched:**
  - `training/tools/compose_mixed.py`, `training/tools/check_crowd_contacts.py`, `training/tools/fit_odds.py`
  - `training/poolympic/events/track.py`, `training/poolympic/events/arena_brawl.py`
  - `Assets/PoOlympic/Runtime/PolicyRunner.cs` (opt-in `startYawDeg`, default `0`), `Assets/PoOlympic/Runtime/TrackRaceEvent.cs`, `Assets/PoOlympic/Runtime/ArenaBrawlEvent.cs`
  - `Assets/PoOlympic/Art/Stadium/events_catalog.json`, `Assets/PoOlympic/Editor/EventScenes.cs`, `Assets/PoOlympic/Models/odds_model.json`

  ## 2026-11-11 — Remove remaining non-contact events from the playable roster

  - **Policies touched:** **None.** This only narrows event availability; policy weights, interfaces, physics, and training tasks are unchanged.
  - **Removed from playable roster:** `#9 The Inverted Sprint`, `#11 Slalom Sprint`, `#13 Steeplechase Jog`, `#14 The Alpine Ramp`, `#22 Emergency Brake`, `#23 The Trench Crawl`, and `#27 The Resurrection Dash`.
  - The events' editor/training assets remain available for development. They are excluded from the generated game menu, stadium hub play actions, and player build scene list.
