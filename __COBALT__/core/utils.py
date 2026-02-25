"""Utility helpers shared across COBALT modules."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Iterable, Sequence

# Paths
CORE_DIR = Path(__file__).resolve().parent
COBALT_DIR = CORE_DIR.parent
REPO_ROOT = COBALT_DIR.parent


def ensure_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    return path


def write_json(path: Path, data: dict) -> None:
    ensure_dir(path.parent)
    path.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def run_command(cmd: Sequence[str], cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        cmd,
        cwd=str(cwd or REPO_ROOT),
        text=True,
        capture_output=True,
        check=False,
    )
    return result


def relativize(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def list_repo_files(max_files: int = 5000) -> list[str]:
    entries: list[str] = []
    for path in sorted(REPO_ROOT.rglob("*")):
        if len(entries) >= max_files:
            break
        if any(part in {".git", "node_modules", "__COBALT__/_scanresults"} for part in path.parts):
            continue
        if path.is_file():
            entries.append(relativize(path))
    return entries


def glob_match(path: str | Path, patterns: Iterable[str]) -> bool:
    from fnmatch import fnmatch

    rel_path = Path(path) if not isinstance(path, Path) else path
    rel = relativize(rel_path)
    return any(fnmatch(rel, pattern) for pattern in patterns)
