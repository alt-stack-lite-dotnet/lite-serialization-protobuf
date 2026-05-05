#!/usr/bin/env python3
"""
Pre-commit: формат (CSharpier) и сборка.
Запускается из корня репозитория.
"""

import subprocess
import sys
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent.parent


def run(cmd: list[str], cwd: Path | None = None) -> int:
    return subprocess.call(cmd, cwd=cwd or ROOT, shell=(sys.platform == "win32"))


def main() -> int:
    # 1) Восстановить dotnet tools (csharpier)
    if run(["dotnet", "tool", "restore"], cwd=ROOT) != 0:
        print("run_lint: dotnet tool restore failed")
        return 1

    # 2) Форматировать C#
    #
    # В среде csharpier доступен как глобальная команда `csharpier`.
    # Поэтому используем её напрямую, чтобы не зависеть от local tool manifest.
    if shutil.which("csharpier"):
        # First, check formatting without writing to avoid noisy diffs.
        # If the check fails, run the formatter to auto-fix.
        if run(["csharpier", "check", "."], cwd=ROOT) != 0:
            if run(["csharpier", "format", "."], cwd=ROOT) != 0:
                print("run_lint: csharpier format failed")
                return 1
    else:
        # Fallback: если tools манифест реально содержит csharpier
        if run(["dotnet", "tool", "run", "csharpier", "."], cwd=ROOT) != 0:
            print("run_lint: csharpier not found (and dotnet tool run fallback failed)")
            return 1

    # 3) Сборка (в т.ч. Roslynator)
    if run(["dotnet", "build", "Lite.Serialization.Protobuf.sln", "-c", "Release", "--no-restore"], cwd=ROOT) != 0:
        print("run_lint: dotnet build failed")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())

