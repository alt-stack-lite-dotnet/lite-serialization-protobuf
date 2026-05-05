#!/usr/bin/env sh
# Установка зависимостей (Linux/macOS):
#   ./install.sh
#
# Делает аналог установщику install.ps1:
# - mise install
# - venv (.venv) + pip install -r requirements.txt
# - pre-commit install (commit) и pre-push (push)
# - dotnet tool restore

set -eu

ROOT="$(git rev-parse --show-toplevel 2>/dev/null || true)"
if [ -z "${ROOT}" ]; then
  ROOT="$(cd "$(dirname "$0")" && pwd)"
fi

cd "$ROOT"

echo "==> Lite.Serialization.Protobuf: установка зависимостей"

if command -v mise >/dev/null 2>&1; then
  echo "==> mise install (Python + .NET)"
  mise install
else
  echo "mise не найден. Продолжаем без mise (предполагаем, что Python/.NET уже установлены)."
fi

echo "==> Python venv и pre-commit"
if [ ! -d ".venv" ]; then
  python3 -m venv .venv
fi

# shellcheck disable=SC1091
. .venv/bin/activate

pip install -q -r requirements.txt

pre-commit install
pre-commit install --hook-type pre-push

echo "Pre-commit: хуки установлены."

echo "==> dotnet tool restore"
dotnet tool restore

echo "Готово."
echo "Активировать venv: . ./.venv/bin/activate"

