#!/bin/bash
# usage: scripts/unity_shot.sh <out png, project-relative>  (Play mode)
cd "$(dirname "${BASH_SOURCE[0]}")/.."
./scripts/unity_eval.sh "return PoOlympic.Editor.CaptureTools.Screenshot(\"$1\");" | grep -o '"result": ".*"'
