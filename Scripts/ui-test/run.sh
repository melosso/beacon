#!/bin/bash
# Usage: run.sh [test.js] (default guard-test.js). Runs a headless UI test on the admin app with a stubbed API. Requires python3 and Chromium.
set -euo pipefail
DIR=$(cd "$(dirname "$0")" && pwd)
WWW=$(cd "$DIR/../../Source/Beacon/wwwroot" && pwd)
TEST=${1:-$DIR/guard-test.js}
OUT=$(mktemp -d)
CHROME=${CHROME:-$(ls -d ~/.cache/ms-playwright/chromium-*/chrome-linux*/chrome 2>/dev/null | tail -1)}
# Classic scripts share one global scope: a duplicate top-level name silently replaces the earlier one.
DUPES=$(cat "$WWW"/js/*.js "$WWW"/js/admin/*.js | grep -oE "^(async )?function [A-Za-z0-9_]+|^(const|let|var) [A-Za-z0-9_]+" | awk '{print $NF}' | sort | uniq -d)
if [ -n "$DUPES" ]; then echo "Duplicate globals: $DUPES"; exit 1; fi
"$DIR/build.sh" "$WWW" "$OUT" "$TEST"
PORT=$((20000 + RANDOM % 20000))
(cd "$OUT/site" && exec python3 -m http.server "$PORT" >/dev/null 2>&1) &
SERVER=$!
trap 'kill $SERVER; rm -rf "$OUT"' EXIT
sleep 1
LOG=$("$CHROME" --headless --no-sandbox --disable-gpu --virtual-time-budget=40000 --dump-dom "http://localhost:$PORT/admin/index.html?view=overview" 2>/dev/null | sed -n '/<pre id="guardlog">/,/<\/pre>/p')
echo "$LOG"
echo "$LOG" | grep -q "fails=0"
