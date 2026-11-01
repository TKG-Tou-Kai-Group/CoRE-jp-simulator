#!/bin/bash
# Unity をバッチモードで起動してプレイヤーをビルドする。
#   tools/build.sh Windows64 | Linux64 | Android [出力パス]
# Unity Hub にサインインした状態で実行すること (ライセンスクライアントが要る)。
set -eu
TARGET=${1:-Windows64}
OUTPUT=${2:-}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
UNITY=${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Unity}
LOG=$ROOT/Logs/build_${TARGET}.log
mkdir -p "$ROOT/Logs"
ARGS=(-batchmode -quit -projectPath "$ROOT" -logFile "$LOG"
      -executeMethod CoreSim.EditorTools.CoreBuild.BuildFromCommandLine -playerTarget "$TARGET")
if [ -n "$OUTPUT" ]; then ARGS+=(-buildOutput "$OUTPUT"); fi
echo "[build] $UNITY ${ARGS[*]}"
"$UNITY" "${ARGS[@]}" && echo "[build] ok (log: $LOG)" || { echo "[build] failed (log: $LOG)"; tail -30 "$LOG"; exit 1; }
