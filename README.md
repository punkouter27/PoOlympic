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
- Voices (`CharacterVoiceBank` + `CharacterVoiceSynth`): 5 emotions (happy, angry, hurt, insult, celebrate) x 5
  variants x 5 levels per athlete. Missing WAVs fall back to the procedural synth. CELEBRATE (event win, each medallist
  at the podium, series champion) has no WAVs yet: add them as
  `Assets/_Project/Environments/Audio/Characters/<ATHLETE>/CELEBRATE/celebrate_<01..05>_L<1..5>.wav`, then run
  PoOlympic › Audio › Generate Character Voice Placeholders to bind them.
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
