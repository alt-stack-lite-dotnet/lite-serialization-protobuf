#!/usr/bin/env python3
"""
Pre-push: сборка и тесты.
Запускается из корня репозитория.
"""

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def run(cmd: list[str], cwd: Path | None = None) -> int:
    return subprocess.call(cmd, cwd=cwd or ROOT, shell=(sys.platform == "win32"))


def main() -> int:
    if run(["dotnet", "build", "Lite.Serialization.Protobuf.sln", "-c", "Release"]) != 0:
        print("run_pre_push: dotnet build failed")
        return 1
    if run(["dotnet", "test", "Lite.Serialization.Protobuf.sln", "--no-build", "-c", "Release"]) != 0:
        print("run_pre_push: dotnet test failed")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

