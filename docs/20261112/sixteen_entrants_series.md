# 2026-11-12 — Bracket removed; 16 entrants simultaneous in 2 rows of 8; RUN ALL series; ELO from placings

## Policy scope (`AGENTS.md` §3)

- **Policies touched: none.** Brain ONNX files, observation/action layouts, joint ordering, timing, and per-athlete collision/kinematic fingerprints are unchanged; `test_wide_bits.py` verifies each 16-lane athlete matches the 8-lane athlete apart from the shared crowd collision bits (`CROWD_SHIFT`).
- **Verification after final edits:**
  - Python MJCF tests: `pytest training/tests/test_wide_bits.py -v` **7/7 passed** (3.27 s), and `compose_mixed.py --verify` **all PASS** across all 8-lane scenes (`meet8`..`hill8`) and all eight 16-lane roster compositions (`pedestal16`, `shaker16`, `crawl16`, `track16`, `inverted16`, `crab16`, `slalom16`, `turntable16`).
  - Unity PlayMode: `PoOlympic.Tests.StateFlowTests.Menu_Heat_Result_AndBack_Twice` **1/1 passed** (61.34 s) playing full 16-entrant heats (single heat per event) to result, toggling `Telemetry`/`Places`, returning via `OK` and sheet `Main menu`, with 0 console errors/warnings.
  - Unity EditMode (non-CrowdContact suites after scene rebuild & lightmap bake): **105/108 passed** (`ShowcaseTests` 3/3, `SceneIntegrityTests` 15/15, `PortraitLayoutTests` 16/16, `SeriesAndVoiceTests` 7/7, `ContractV4Tests` 4/4, `SpectacleTests` 4/4, `PhysXGuardTests` 3/3, `DemoModeTests` 3/3, `GameSettingsTests` 2/2, `RuntimeRulesTests` 2/2, `CubePoolTests` 1/1, `HandTunedSaveTests` 1/1, `SteeringTests` 1/1, `UiTokenTests` 1/1).
    - The 3 failures are pre-existing `grandma_rung2` brain/critic items unrelated to the 16-entrant work: `BroadcastFxTests.EveryEventBrainHasACalibratedCritic` (`grandma_rung2: no calibrated confidence entry`, 5/6) and `ParityGateTests.Gates_G2_G3_G4("grandma_rung2" | "grandma_rung2_walk")` (37/39).
  - Unity `CrowdContactTests` spot-check on `Event_30mAllFours` timed out at the 600 s CLI limit (64 bodies × all-pairs collision checks); Python `test_wide_bits.py` verifies all-pairs contacts and non-overlap at rest for all 16-lane MJCF models in 3.27 s.

## What changed

- **Removed bracket mode:** Deleted `TournamentBracket.cs`, `TournamentMode.cs`, and the UI bracket/TOURNEY/RESEED/BYE flow; `SeriesAndVoiceTests` covers the `MeetSeries` / `EventCompetition` / `AgentElo` paths.
- **Simultaneous 16-athlete events (2 rows of 8):**
  - `training/tools/compose_mixed.py` & generated `*16_roster.xml` / `*16_roster_layout.json` assets: all 8 base 16-athlete templates (`pedestal16`, `shaker16`, `crawl16`, `track16`, `inverted16`, `crab16`, `slalom16`, `turntable16`) place 16 entrants (64 roster bodies total per scene) in 2 non-overlapping rows of 8, including widened `shaker16` (`row_offset = (0.0, -2.6, 0.0)`, `_shaker_floor16()`).
  - `MeetLineup.cs` / `EventCompetition`: `HeatSize = EntrantCount` (16), `HeatCount = 1`; `DrawParticipants` draws per `rowSize = 8` so both rows include every roster character; `Gauntlet` points table expanded to 16 entries.
  - `SlalomEvent.cs`: `TacticSteer` compares across all 16 racers (`racers.Count`) using world X (`x + runner.laneOriginX`) and Y spacing.
  - `EventScenes.cs`: Pointed event builders to the `*16_roster` assets, widened Iron Pedestal visuals (`pedestal` renderer enabled, `E01_Beam` hidden) and broadcast camera offsets (`Event_IronPedestal_Heat`, `Event_GustGauntlet_Heat`, `Event_Turntable_Heat`), updated reversed lane labels for row 2 (`L9..L16`), rebuilt the 11 playable scenes (`BuildPlayableSixteen`) + `MainMenu.unity`, and baked lightmaps (`BakeMissingLightmaps`, lightmaps=1 on all 11 scenes).
- **UI & HUD (`MainMenuController.cs`, `BroadcastHud.cs`, `BroadcastHud.uss`):**
  - Menu displays `16 entrants · 2 rows of 8` and `RUN ALL` (`MeetSeries`).
  - Top dock standings cycle Top 4 → `1–8 ▼` → `9–16 ▼` (`_expandPage`) so the live camera viewport stays open; Match Card renders 16 `Places` and `Telemetry` rows in `.bh-match-grid` (2 columns).
  - Visual report in `SCREENSHOTS/entrants16_before_after.html` (`menu16_after.png`, `pedestal16_live.png`, `allfours16_live.png`).
- **URP RenderGraph fix (`Assets/Settings/PC_Renderer.asset`, `Assets/Settings/Mobile_Renderer.asset`):**
  - Set `m_IntermediateTextureMode: 1` (`Always`) so URP allocates `cameraColor` on WebGL/Mobile `ScreenSpace` `DecalScreenSpaceRenderPass`, fixing a `NullReferenceException` (`Render Graph Execution error` in `RenderingUtils.SetScaleBiasRt`) on every event frame when active build target is WebGL.
