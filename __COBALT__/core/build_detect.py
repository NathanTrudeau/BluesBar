"""Heuristics for discovering build/test commands."""

from __future__ import annotations

from pathlib import Path
from typing import List, Dict

from .utils import REPO_ROOT


def discover_commands() -> List[Dict[str, str]]:
    commands: List[Dict[str, str]] = []
    root = REPO_ROOT

    def add(command: str, reason: str, confidence: str = "LOW") -> None:
        commands.append({
            "command": command,
            "reason": reason,
            "confidence": confidence,
        })

    if (root / "package.json").exists():
        add("npm install", "package.json detected", "MEDIUM")
        add("npm run build", "package.json detected", "MEDIUM")
        add("npm test", "package.json detected", "MEDIUM")

    if (root / "requirements.txt").exists():
        add("pip install -r requirements.txt", "requirements.txt detected", "LOW")
        add("pytest", "requirements.txt detected", "LOW")

    csproj_files = list(root.glob("**/*.csproj"))
    if csproj_files:
        add("dotnet restore", "C# project detected", "LOW")
        add("dotnet build", "C# project detected", "LOW")
        add("dotnet test", "C# project detected", "LOW")

    if not commands:
        add("(none detected)", "No known build/test markers", "LOW")
    return commands
