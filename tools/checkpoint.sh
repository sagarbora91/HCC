#!/usr/bin/env bash
# Crash-safety checkpoint: every 7 minutes, commit acceptance evidence and HANDOFF/planning notes to the local git repo.
# Only paths that agents write during acceptance are staged; product code is never auto-committed. Never pushes.
# Usage (from the workspace root): bash tools/checkpoint.sh [interval-seconds]   — runs until stopped.
cd "$(dirname "$0")/.." || exit 1
INTERVAL="${1:-420}"
PATHS=("05 Delivery Evidence" "HANDOFF.md" "02 Planning/HCC_Acceptance_Matrix.md")
while true; do
  git add -- "${PATHS[@]}" 2>/dev/null
  if ! git diff --cached --quiet -- "${PATHS[@]}"; then
    n=$(git diff --cached --name-only -- "${PATHS[@]}" | wc -l)
    if git commit -q -m "Checkpoint $(date +%H:%M): acceptance evidence ($n files)" -- "${PATHS[@]}"; then
      echo "$(date +%H:%M:%S) checkpoint committed ($n files)"
    else
      echo "$(date +%H:%M:%S) checkpoint commit failed (index busy?) — retrying next cycle"
    fi
  else
    echo "$(date +%H:%M:%S) nothing new"
  fi
  sleep "$INTERVAL"
done
