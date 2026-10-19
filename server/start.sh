#!/bin/sh
set -eu
umask 077
cd -- "$(dirname -- "$0")"
sh ./bootstrap.sh
DATA=${FRAMEFLOW_TEAM_DATA:-"$PWD/data"}
exec .venv/bin/python launcher.py --data "$DATA" --no-browser "$@"
