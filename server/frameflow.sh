#!/bin/sh
# Docker Compose management. Never deletes volumes or edits firewall rules.
set -eu
umask 077
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT"
usage() {
  cat <<'EOF'
Frameflow Team / Linux
  sh install.sh [--username NAME]  Build, initialize admin, start in background
  sh frameflow.sh start|stop|restart|status|logs
  sh frameflow.sh reset-password USER
  sh frameflow.sh backup           Copy a consistent snapshot into ./backups/
  sh frameflow.sh upgrade          Backup, build current source, restart (no git pull)
Docker Engine + Compose plugin required. Default port is loopback-only 8765.
No Docker? See docs/LINUX.md for the Python 3.12+ native installation.
EOF
}
command=${1:-help}
[ "$#" -eq 0 ] || shift
case "$command" in help|-h|--help) usage; exit 0 ;; esac
command -v docker >/dev/null 2>&1 || { echo 'Docker is not installed. See https://docs.docker.com/engine/install/ or docs/LINUX.md for native Python.' >&2; exit 1; }
docker compose version >/dev/null || { echo 'Docker Compose plugin v2 is required.' >&2; exit 1; }
backup() {
  name="frameflow-$(date -u +%Y%m%dT%H%M%SZ)-$$"
  docker compose exec -T app python /app/server.py --data /data backup "/backups/$name"
  mkdir -p "$ROOT/backups"
  if ! docker compose cp "app:/backups/$name" "$ROOT/backups/"; then
    echo "Snapshot remains in the backups volume: /backups/$name (host copy failed)." >&2
    return 1
  fi
  echo "Backup copied to $ROOT/backups/$name (contains private accounts and media)."
}
case "$command" in
  install)
    docker compose build --pull
    docker compose run --rm --no-deps app init "$@"
    docker compose up -d --wait --wait-timeout 120
    echo 'Started. Only localhost is exposed; use SSH forwarding or configure HTTPS. See docs/LINUX.md.'
    ;;
  start) [ "$#" -eq 0 ] || { usage; exit 2; }; docker compose up -d --wait --wait-timeout 120 ;;
  stop) [ "$#" -eq 0 ] || { usage; exit 2; }; docker compose stop ;;
  restart) [ "$#" -eq 0 ] || { usage; exit 2; }; docker compose restart; docker compose up -d --wait --wait-timeout 120 ;;
  status) docker compose ps ;;
  logs) docker compose logs --tail 100 -f app ;;
  reset-password)
    [ "$#" -eq 1 ] || { echo 'Usage: sh frameflow.sh reset-password USER' >&2; exit 2; }
    docker compose exec app python /app/server.py --data /data reset-password "$1"
    ;;
  backup) [ "$#" -eq 0 ] || { usage; exit 2; }; backup ;;
  upgrade)
    [ "$#" -eq 0 ] || { usage; exit 2; }
    backup
    docker compose build --pull
    docker compose up -d --wait --wait-timeout 120
    ;;
  *) usage >&2; exit 2 ;;
esac
