#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"
[ -x .venv/bin/python ] || { echo 'Run sh install-native.sh first.' >&2; exit 1; }
command -v systemctl >/dev/null || { echo 'This host has no systemd. Use Docker or your process manager.' >&2; exit 1; }
systemctl --user show-environment >/dev/null || { echo 'No systemd user session. See docs/LINUX.md; do not silently install a root service.' >&2; exit 1; }
.venv/bin/python linux/render_service.py --data "${FRAMEFLOW_TEAM_DATA:-$ROOT/data}" "$@"
systemctl --user daemon-reload
systemctl --user enable --now frameflow.service
systemctl --user status frameflow.service --no-pager
