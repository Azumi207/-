#!/bin/sh
set -eu
umask 077
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT"
if [ ! -x .venv/bin/python ]; then
  [ ! -e .venv ] || { echo '.venv exists but is not a Linux environment. Use a fresh checkout, not a copied Windows venv.' >&2; exit 1; }
  PYTHON=${PYTHON:-python3}
  "$PYTHON" -c 'import sys; assert sys.version_info >= (3,12), "Python 3.12+ required; use Docker if unavailable"'
  "$PYTHON" -m venv .venv || { echo 'venv creation failed. Install matching Python venv support, or use Docker.' >&2; exit 1; }
fi
.venv/bin/python -c 'import sys; assert sys.version_info >= (3,12), "Python 3.12+ required"'
if [ ! -f .venv/requirements.txt ] || ! cmp -s requirements.txt .venv/requirements.txt; then
  .venv/bin/python -m pip install -r requirements.txt
  cp requirements.txt .venv/requirements.txt
fi
