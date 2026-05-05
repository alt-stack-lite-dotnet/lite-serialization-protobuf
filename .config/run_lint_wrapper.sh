#!/bin/sh
# Обёртка для pre-commit: переходит в корень репо и запускает run_lint.py через venv.
set -e
root="$(git rev-parse --show-toplevel)"
cd "$root"
if [ -f .venv/bin/activate ]; then
  . .venv/bin/activate
elif [ -f .venv/Scripts/activate ]; then
  . .venv/Scripts/activate
fi
exec python .config/run_lint.py

