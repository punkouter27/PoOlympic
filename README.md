# PoOlympics

Physics-driven humanoids (MATT, ZOMBIE, GRANDMA, KIM) compete in stadium events. Every body is simulated in MuJoCo
and driven by its own reinforcement-learning brain (PPO on MuJoCo Warp via mjlab, run in Unity through the Inference
Engine). No animation and no PhysX.

- Play: https://icy-field-055e0430f.6.azurestaticapps.net · status: https://icy-field-055e0430f.6.azurestaticapps.net/diag
- Unity 6000.6.9f1 (exact), URP, UI Toolkit, MuJoCo 3.11.0 (`Packages/org.mujoco`, embedded), WebGL + Android.
- Training: `Training/` (uv project, Python 3.11, MuJoCo Warp + mjlab), see `docs/DESIGN.md` and `docs/tasks.md`.

## Run locally
- Fresh clone: `git lfs pull`, open the folder in Unity 6000.6.9f1, scene `Assets/_Project/Scenes/MainMenu.unity`.
- Python: `cd Training; uv sync; uv run pytest tests -q -m "not slow"`.
- `Tab` (or the bottom-left debug button) shows FPS, episode reward, inference latency, curriculum stage and a training-speed benchmark button; after
  30 s without input the game runs itself (demo mode).

## Ratings and athlete voices
- ELO (`Core/Runtime/AgentElo.cs`, PlayerPrefs `PoOlympic.AgentElo`): K = 32, new athletes start at **1000** (was 1200
  until 2026-11-22). Ratings already saved on a device keep their values (athletes rated before the change sit near
  1200) until the save is wiped (`GameSettings.ResetSave`, which calls `AgentElo.Reset()`).
- Voices: `Assets/_Project/Environments/Audio/Characters/voices.json` lists every athlete x 5 emotions (angry, happy,
  hurt, insult, celebrate) x 5 levels, each with its 5 takes by file name
  (`<ATHLETE>/<EMOTION>/<emotion>_<take 01..05>_L<level 1..5>.wav`, relative to that folder), an `exists` flag and the
  athlete's procedural `voice` profile (pitch, formants, breath, rasp, vibrato, tempo, nasal, timbre).
  - Add a recording: drop the WAV at its listed path; the editor rewrites voices.json and rebinds
    `Data/Resources/CharacterVoiceBank.asset` on import (or run PoOlympic › Audio › Rebuild Voice Manifest). It logs
    "N of M clips recorded; missing: …". Edit a `voice` profile in voices.json by hand; the generator keeps it.
  - What plays: a recorded take when voices.json says it exists, else the advanced procedural imitation
    (`CharacterVoiceSynth`: glottal pulses with jitter/shimmer, 4 formants, consonant onsets, per-emotion prosody,
    deterministic and cached). No recordings exist today (0 of 500), so everything is procedural.
  - Placeholders never count: every WAV the synth bakes carries a `PoOlympic CharacterVoiceSynth placeholder` marker
    (RIFF LIST/INFO), and the generator sets `exists` true only for WAVs without it. The 400 unmarked placeholder WAVs
    the old tool baked were deleted on 2026-11-22 (they matched the old synth output).
  - Celebrate plays on an event win, for each medallist at the podium, and for the series champion.
- Main menu: an empty entrant slot reads "BYE". Heats (`MeetSeries`) are unchanged; there is no bracket.

## Eight athletes per event
Every event runs exactly 8 athletes, one per lane, in one heat (`EventCompetition.EntrantCount` = 8; it was 16 in
two rows of 8 from 2026-11-12). The menu's 8 entrant slots are filled from the roster; with only 4 roster athletes
the lanes repeat athletes, and each keeps its lane label (MATT `L<n>`, ZOMBIE `Z<n>`, GRANDMA `G<n>`, KIM `K<n>`), so
two MATTs read e.g. `L2` and `L6`. The 11 playable events are built from the 8-lane roster scenes
(`Training/assets/*8_roster*`); the `*16_roster` assets stay in the repo, unused by Unity.
**Rebuild the scenes** after pulling this: PoOlympic › Events › Build All 11 Playable 8-Athlete Events + Menu
(the committed `Event_*` scenes of events 1, 5, 8, 9, 10, 11, 12, 13, 14, 19, 27 still hold 16 lanes; until they are
rebuilt `LaneLineup` switches lanes 9–16 off, so they already run 8 athletes but framed for two rows).
Solo scenes stay solo: `Event_IronPedestal.unity` (practice, not in the menu) and the `Testbed_*` dev scenes.

## Deploy
`pwsh -File scripts/publish_webgl.ps1` (Unity closed, HEAD pushed): EditMode tests, WebGL build, GitHub release
`webgl-*`, which `.github/workflows/webgl.yml` deploys to Azure Static Web Apps. Details: `AGENTS.md`.

## Docs
`AGENTS.md` (conventions and repo notes), `docs/` (design, task log, training log), `SCREENSHOTS/` (UI before/after
reports).
