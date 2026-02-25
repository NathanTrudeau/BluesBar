"""Helpers for managing COBALT run directories."""

from __future__ import annotations

import datetime as dt
import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List

from .utils import COBALT_DIR, ensure_dir

RUNS_DIR = COBALT_DIR / "_runs"


def generate_run_id() -> str:
    return dt.datetime.utcnow().strftime("%Y%m%dT%H%M%SZ")


def slugify(text: str, max_len: int = 40) -> str:
    text = re.sub(r"[^A-Za-z0-9_-]+", "-", text.strip())
    return text[:max_len].strip("-") or "cmd"


@dataclass
class RunCommandResult:
    command: str
    status: str
    exit_code: int | None
    log_path: str
    reason: str | None = None
    duration_seconds: float | None = None


@dataclass
class RunMetadata:
    run_id: str
    started_at: str
    finished_at: str | None = None
    commands: List[RunCommandResult] = field(default_factory=list)

    def to_dict(self) -> Dict:
        return {
            "run_id": self.run_id,
            "started_at": self.started_at,
            "finished_at": self.finished_at,
            "commands": [
                {
                    "command": c.command,
                    "status": c.status,
                    "exit_code": c.exit_code,
                    "log_path": c.log_path,
                    "reason": c.reason,
                    "duration_seconds": c.duration_seconds,
                }
                for c in self.commands
            ],
        }


class RunStore:
    def __init__(self) -> None:
        self.dir = ensure_dir(RUNS_DIR)

    def start_run(self) -> tuple[RunMetadata, Path]:
        run_id = generate_run_id()
        run_dir = ensure_dir(self.dir / run_id)
        metadata = RunMetadata(run_id=run_id, started_at=dt.datetime.utcnow().isoformat() + "Z")
        return metadata, run_dir

    def save_metadata(self, metadata: RunMetadata, run_dir: Path) -> Path:
        metadata.finished_at = dt.datetime.utcnow().isoformat() + "Z"
        path = run_dir / "metadata.json"
        path.write_text(json.dumps(metadata.to_dict(), indent=2) + "\n", encoding="utf-8")
        return path

    def latest_run_dir(self) -> Path | None:
        if not self.dir.exists():
            return None
        dirs = sorted(p for p in self.dir.iterdir() if p.is_dir())
        return dirs[-1] if dirs else None


__all__ = ["RunStore", "RunMetadata", "RunCommandResult", "slugify"]
