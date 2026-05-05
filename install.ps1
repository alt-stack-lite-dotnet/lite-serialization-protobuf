# Установка зависимостей: mise (Python + .NET), venv, pre-commit, dotnet tools.
# Запуск из корня репо: .\install.ps1

$ErrorActionPreference = "Stop"
$root = git rev-parse --show-toplevel 2>$null; if (-not $root) { $root = $PSScriptRoot }
Set-Location $root

Write-Host "==> Lite.Serialization.Protobuf: установка зависимостей" -ForegroundColor Cyan

if (-not (Get-Command mise -ErrorAction SilentlyContinue)) {
    Write-Host "mise не найден. Установите: https://mise.jdx.dev/getting-started.html" -ForegroundColor Yellow
    Write-Host "  winget install jdx.mise"
    Write-Host "Продолжаем без mise (предполагается, что Python/.NET уже установлены)." -ForegroundColor Yellow
} else {
    Write-Host "==> mise install (Python + .NET)" -ForegroundColor Cyan
    mise install
}

Write-Host "==> Python venv и pre-commit" -ForegroundColor Cyan
if (-not (Test-Path .venv)) {
    python -m venv .venv
}

& .\.venv\Scripts\Activate.ps1
pip install -q -r requirements.txt
pre-commit install --install-hooks
pre-commit install --hook-type pre-push --install-hooks
Write-Host "Pre-commit: pre-commit и pre-push хуки установлены."

Write-Host "==> dotnet tool restore" -ForegroundColor Cyan
dotnet tool restore

Write-Host ""
Write-Host "Done. Activate venv: .\\.venv\\Scripts\\Activate.ps1" -ForegroundColor Green

