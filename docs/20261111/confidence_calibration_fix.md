# 2026-11-11 — Crawl Confidence Calibration Fix (`crawl_grandma`, `crawl_kim`)

## Policy Impact (per `AGENTS.md` §3)
- **Category:** HUD telemetry confidence calibration (`training/tools/fit_confidence.py`, `Assets/PoOlympic/Models/confidence_model.json`, `parity/confidence/report.json`)
- **Policies touched:** **None.** Only updates the post-hoc logistic regression coefficients used by `BrainConfidence` for display confidence in `AthleteTelemetry`; no policy ONNX weights, observations, actions, physics, or directors are changed.

## Root Cause
- Straight-crawl brains `crawl_grandma` and `crawl_kim` maintain pelvis height `z/lam` between `0.315` and `0.470` under `fit_confidence.py`'s horizontal XY shoves (`0.3–2.4 m/s`), producing `0` tumbles (`z < 0.25 * lam`) and `calibrated: false`, failing `BroadcastFxTests.EveryEventBrainHasACalibratedCritic`.

## Results (2026-11-11)
- Also enforced monotonic non-negative `w_drop >= 0` in `training/tools/fit_confidence.py`: if unconstrained IRLS yields `w_drop < 0` (contradicting the value-drop confidence invariant), it refits on `(V, |cmd_xy|)` with `w_drop = 0.0`.
- Re-ran `uv run python tools/fit_confidence.py --groups crawl_g,crawl_k` (8 seeds × 150 s):
  - `crawl_grandma`: `n = 155,521`, `danger_samples = 12,584`, `AUC = 0.636`, `w = (+0.0887, +0.2630, 0.0)`, `b = +0.496`, `calibrated = true`.
  - `crawl_kim`: `n = 108,610`, `danger_samples = 5,133`, `AUC = 0.743`, `w = (+0.4291, 0.0, 0.0)`, `b = -6.391`, `calibrated = true`.
- Verified full suites after all edits:
  - Unity EditMode (`PoOlympic.Tests` across all 16 classes): **125/125 passed** (`BroadcastFxTests` 6/6, `ContractV4Tests` 4/4, `CrowdContactTests` 18/18, `CubePoolTests` 1/1, `DemoModeTests` 3/3, `GameSettingsTests` 2/2, `HandTunedSaveTests` 1/1, `PhysXGuardTests` 3/3, `PortraitLayoutTests` 16/16, `RuntimeRulesTests` 2/2, `SceneIntegrityTests` 20/20, `ShowcaseTests` 3/3, `SpectacleTests` 4/4, `SteeringTests` 1/1, `UiTokenTests` 1/1, `ParityGateTests` 39/39)
  - Unity PlayMode (`StateFlowTests`): **1/1 passed**
  - Python (`uv run pytest` across all 7 test modules): **97/97 passed** (`test_audit.py`, `test_contract.py`, `test_model.py`: 34/34; `test_events.py`, `test_crowd_contacts.py`, `test_stance.py`, `test_symmetry.py`: 63/63)
