#!/bin/sh
set -eu
umask 077
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT"
sh ./bootstrap.sh
DATA=${FRAMEFLOW_TEAM_DATA:-"$ROOT/data"}
.venv/bin/python server.py --data "$DATA" init "$@"
echo 'Initialized. Foreground: sh start.sh --serve-only'
echo 'Background systemd user service: sh linux/install-user-service.sh'
