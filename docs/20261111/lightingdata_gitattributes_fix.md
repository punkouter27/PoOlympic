# 2026-11-11 — `.gitattributes` & `LightingData.asset` Fix

## Policy Impact (per `AGENTS.md` §3)
- **Category:** Visuals / repository asset attributes (`.gitattributes` + baked scene `LightingData.asset`)
- **Policies touched:** **None.** No body physics, observation layouts, action maps, contracts, directors, or ONNX brains are modified.

## Root Cause
- `.gitattributes` line 5 (`*.asset text eol=lf merge=unityyamlmerge`) matched Unity's binary `LightingData.asset` files across 21 scenes, stripping `\r\n` bytes during git text normalization.
- Every scene load logged `Unknown error occurred while loading 'Assets/PoOlympic/Scenes/.../LightingData.asset'`, failing `StateFlowTests` (PlayMode) and scene-loading EditMode tests (`SceneIntegrityTests`, etc.).

## Results (2026-11-11)
1. Updated `.gitattributes` with `LightingData.asset binary !eol` and `*.exr binary` (verified with `git check-attr --all`: `binary: set`, `text: unset`, no `eol: lf`).
2. Re-baked all 21 scenes with `LightingData.asset` in Unity `6000.6.0f1`; all `Unknown error occurred while loading .../LightingData.asset` errors resolved.
3. **Test verification after final edit:**
   - PlayMode `StateFlowTests`: **1/1 passed**
   - EditMode `SceneIntegrityTests`: **20/20 passed**
   - EditMode `ContractV4Tests`: **4/4 passed**
   - EditMode 12 classes (`CubePoolTests` 1/1, `DemoModeTests` 3/3, `GameSettingsTests` 2/2, `HandTunedSaveTests` 1/1, `PhysXGuardTests` 3/3, `PortraitLayoutTests` 16/16, `RuntimeRulesTests` 2/2, `ShowcaseTests` 3/3, `SpectacleTests` 4/4, `SteeringTests` 1/1, `UiTokenTests` 1/1, `ParityGateTests` 39/39): **77/77 passed**
   - EditMode `BroadcastFxTests`: **5/6 passed, 1 failed** (`EveryEventBrainHasACalibratedCritic`: `crawl_grandma` and `crawl_kim` in `Assets/PoOlympic/Models/confidence_model.json` have `auc: 0.0`, `danger_samples: 0`, `"calibrated": false`)
   - EditMode `CrowdContactTests`: stopped after 6/18 scenes (`Event_30mAllFours`, `Event_360Turntable`, `Event_AlpineRamp`, `Event_CadenceMarch`, `Event_CrabShuffle`, `Event_DeepSquat` all completed with `0` failures) because the remaining 12 roster contact matrices take ~15+ minutes synchronously.
