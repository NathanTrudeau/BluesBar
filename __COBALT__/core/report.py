"""Report writer helpers."""

from __future__ import annotations

import datetime as dt
from pathlib import Path
from typing import Any, Dict

from .config import CobaltConfig
from .runlog import RunLog
from .utils import write_json


class ReportWriter:
    def __init__(self, config: CobaltConfig, runlog: RunLog, scan_dir: Path) -> None:
        self.config = config
        self.runlog = runlog
        self.scan_dir = scan_dir

    def write_outputs(self, scan: Dict[str, Any]) -> None:
        index = {
            "generated_at": scan["timestamp"],
            "branch": scan["git"]["branch"],
            "head": scan["git"]["head"],
            "findings_count": len(scan["findings"]),
        }
        write_json(self.scan_dir / "COBALT_INDEX.json", index)
        report_md = self._render_report(scan)
        (self.scan_dir / "COBALT_REPORT.md").write_text(report_md, encoding="utf-8")

    def _render_report(self, scan: Dict[str, Any]) -> str:
        lines = []
        lines.append("# COBALT Report (Phase 1)")
        lines.append("")
        lines.append(f"Generated: {scan['timestamp']}")
        lines.append(f"Branch: {scan['git']['branch']}")
        lines.append(f"HEAD: {scan['git']['head']}")
        lines.append("")
        lines.append("## Findings")
        if scan["findings"]:
            for item in scan["findings"]:
                lines.append(f"- {item}")
        else:
            lines.append("- No issues detected.")
        lines.append("")
        lines.append("## Policy Signals")
        policy = scan["policy"]
        lines.append(f"- Denylist matches: {len(policy['denylist_matches'])}")
        lines.append(f"- Confirm-first matches: {len(policy['confirm_first_matches'])}")
        lines.append("")
        lines.append("## Repo Map")
        lines.append(f"- Files counted: {scan['repo_map']['total_files_counted']}")
        lines.append("- Top languages:")
        for lang, count in scan["repo_map"]["top_languages"]:
            lines.append(f"  - {lang or '(no extension)'}: {count}")
        lines.append("- Hotspots:")
        for hs in scan["repo_map"]["hotspots"]:
            lines.append(f"  - {hs['path']}: {hs['file_count']} files")
        lines.append("")
        lines.append("## Build/Test Candidates")
        for cmd in scan["build_commands"]:
            lines.append(f"- {cmd['command']} (confidence: {cmd['confidence']} — {cmd['reason']})")
        lines.append("")
        lines.append("## Git Status")
        lines.append("```")
        lines.append(scan["git"]["status"])
        lines.append("```")
        lines.append("")
        lines.append("Confidence: MEDIUM (Phase 1 passive scan)")
        return "\n".join(lines).strip() + "\n"

    def write_runlog(self, path: Path | None = None) -> Path:
        target = path or (self.scan_dir / "RUNLOG.txt")
        return self.runlog.write(target)
