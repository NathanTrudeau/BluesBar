"""QA executor for COBALT Phase 2."""

from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any, Dict, List

from .build_detect import discover_commands
from .config import CobaltConfig
from .runlog import RunLog
from .runs import RunStore, RunCommandResult, slugify
from .utils import run_command, write_json, COBALT_DIR


class QAExecutor:
    def __init__(self, config: CobaltConfig, runlog: RunLog) -> None:
        self.config = config
        self.runlog = runlog
        self.store = RunStore()
        self.scan_dir = COBALT_DIR / "_scanresults"

    def execute(self) -> Dict[str, Any]:
        metadata, run_dir = self.store.start_run()
        commands = discover_commands()
        results: List[RunCommandResult] = []
        forbid = self.config.policies.get("scan", {}).get("forbid_patterns", [])

        for idx, spec in enumerate(commands, start=1):
            command = spec["command"]
            if command == "(none detected)":
                results.append(
                    RunCommandResult(
                        command=command,
                        status="skipped",
                        exit_code=None,
                        log_path="",
                        reason=spec["reason"],
                    )
                )
                continue

            if any(pattern in command for pattern in forbid):
                results.append(
                    RunCommandResult(
                        command=command,
                        status="blocked",
                        exit_code=None,
                        log_path="",
                        reason=f"Command matched forbidden pattern",
                    )
                )
                continue

            slug = f"{idx:02d}_{slugify(command)}"
            log_file = run_dir / f"{slug}.txt"
            start = time.time()
            proc = run_command(["/bin/sh", "-c", command])
            duration = time.time() - start
            self.runlog.record([command], proc.returncode, proc.stdout, proc.stderr)
            log_contents = [
                f"$ {command}",
                f"exit={proc.returncode}",
                "--- STDOUT ---",
                proc.stdout,
                "--- STDERR ---",
                proc.stderr,
            ]
            log_file.write_text("\n".join(log_contents), encoding="utf-8")
            status = "pass" if proc.returncode == 0 else "fail"
            result = RunCommandResult(
                command=command,
                status=status,
                exit_code=proc.returncode,
                log_path=str(log_file.relative_to(COBALT_DIR)),
                duration_seconds=round(duration, 2),
            )
            results.append(result)

        metadata.commands.extend(results)
        metadata_path = self.store.save_metadata(metadata, run_dir)
        test_results = {
            "run_id": metadata.run_id,
            "results": [r.__dict__ for r in results],
        }
        write_json(self.scan_dir / "test_results.json", test_results)
        return {
            "metadata": metadata.to_dict(),
            "metadata_path": str(metadata_path),
            "results_file": str((self.scan_dir / "test_results.json").relative_to(COBALT_DIR)),
        }


class TriageReporter:
    def __init__(self) -> None:
        self.store = RunStore()
        self.scan_dir = COBALT_DIR / "_scanresults"

    def triage_last(self) -> Dict[str, Any]:
        run_dir = self.store.latest_run_dir()
        if run_dir is None:
            raise SystemExit("No QA runs found. Run 'cobalt qa-run' first.")
        metadata_path = run_dir / "metadata.json"
        if not metadata_path.exists():
            raise SystemExit("Latest run missing metadata.json")
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        failing = [cmd for cmd in metadata.get("commands", []) if cmd.get("status") == "fail"]
        summary = {
            "run_id": metadata["run_id"],
            "total_commands": len(metadata.get("commands", [])),
            "failures": failing,
        }
        write_json(self.scan_dir / "triage.json", summary)
        return summary
