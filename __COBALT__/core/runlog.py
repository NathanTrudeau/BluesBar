"""Simple run log recorder for COBALT."""

from __future__ import annotations

import datetime as dt
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Sequence

from .utils import COBALT_DIR, ensure_dir


@dataclass
class RunEntry:
    timestamp: str
    command: Sequence[str]
    exit_code: int
    stdout: str
    stderr: str


@dataclass
class RunLog:
    entries: List[RunEntry] = field(default_factory=list)

    def record(self, command: Sequence[str], exit_code: int, stdout: str, stderr: str) -> None:
        timestamp = dt.datetime.utcnow().isoformat() + "Z"
        self.entries.append(RunEntry(timestamp, list(command), exit_code, stdout.strip(), stderr.strip()))

    def write(self, path: Path | None = None) -> Path:
        if path is None:
            scan_dir = ensure_dir(COBALT_DIR / "_scanresults")
            path = scan_dir / "RUNLOG.txt"
        lines = []
        for entry in self.entries:
            lines.append(f"[{entry.timestamp}] $ {' '.join(entry.command)} (exit={entry.exit_code})")
            if entry.stdout:
                lines.append("STDOUT:")
                lines.extend(f"  {line}" for line in entry.stdout.splitlines()[:20])
            if entry.stderr:
                lines.append("STDERR:")
                lines.extend(f"  {line}" for line in entry.stderr.splitlines()[:20])
            lines.append("")
        path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
        return path

    def log_command(self, command: Sequence[str], runner) -> None:
        result = runner(command)
        self.record(command, result.returncode, result.stdout, result.stderr)


__all__ = ["RunLog", "RunEntry"]
