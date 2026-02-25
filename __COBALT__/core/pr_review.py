"""PR review helper (read-only)."""

from __future__ import annotations

from typing import Any, Dict, List

from .config import CobaltConfig
from .runlog import RunLog
from .utils import glob_match, run_command


class PRReview:
    def __init__(self, config: CobaltConfig, runlog: RunLog) -> None:
        self.config = config
        self.runlog = runlog

    def _run_git(self, *args: str) -> str:
        cmd = ["git", *args]
        result = run_command(cmd)
        self.runlog.record(cmd, result.returncode, result.stdout, result.stderr)
        return result.stdout

    def review(self, base_ref: str, head_ref: str) -> str:
        summary = self._create_summary(base_ref, head_ref)
        return summary

    def _create_summary(self, base_ref: str, head_ref: str) -> str:
        diff = self._run_git("diff", f"{base_ref}..{head_ref}", "--stat")
        files_raw = self._run_git("diff", f"{base_ref}..{head_ref}", "--name-only")
        files = [line.strip() for line in files_raw.splitlines() if line.strip()]
        policies = self.config.policies
        deny = policies.get("scopes", {}).get("denylist", [])
        confirm = policies.get("scopes", {}).get("confirm_first", [])
        deny_hits = [path for path in files if glob_match(path, deny)]
        confirm_hits = [path for path in files if glob_match(path, confirm)]
        sections = [
            f"Reviewing diff {base_ref}..{head_ref}",
            "Findings:",
        ]
        if not files:
            sections.append(" - No changed files detected.")
        else:
            sections.append(f" - {len(files)} files changed.")
        if deny_hits:
            sections.append(
                f" - Denylist match (requires operator decision): {', '.join(sorted(deny_hits))}"
            )
        if confirm_hits:
            sections.append(
                f" - Confirmation needed before touching: {', '.join(sorted(confirm_hits))}"
            )
        sections.append("Diff summary:")
        sections.append(diff if diff.strip() else "(no diff stats)")
        return "\n".join(sections)
