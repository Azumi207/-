#!/bin/sh
set -eu
umask 077
DATA=${FRAMEFLOW_TEAM_DATA:-/data}
if [ "$#" -eq 0 ]; then set -- serve; fi
case "$1" in
  serve)
    shift
    exec python /app/launcher.py --data "$DATA" --no-browser --serve-only \
      --host 0.0.0.0 --allow-insecure-http \
      --max-video-gb "${FRAMEFLOW_MAX_VIDEO_GB:-20}" --quota-gb "${FRAMEFLOW_QUOTA_GB:-100}" "$@"
    ;;
  init) exec python /app/server.py --data "$DATA" "$@" ;;
  reset-password|disable-user|backup|cleanup-uploads)
    [ -f "$DATA/team.sqlite3" ] || { echo 'No existing database; refusing to create an empty workspace.' >&2; exit 1; }
    exec python /app/server.py --data "$DATA" "$@"
    ;;
  *) echo 'Commands: serve, init, reset-password USER, disable-user USER, backup DEST, cleanup-uploads' >&2; exit 2 ;;
esac
