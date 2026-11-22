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

## Deploy
`pwsh -File scripts/publish_webgl.ps1` (Unity closed, HEAD pushed): EditMode tests, WebGL build, GitHub release
`webgl-*`, which `.github/workflows/webgl.yml` deploys to Azure Static Web Apps. Details: `AGENTS.md`.

## Docs
`AGENTS.md` (conventions and repo notes), `docs/` (design, task log, training log), `SCREENSHOTS/` (UI before/after
reports).
