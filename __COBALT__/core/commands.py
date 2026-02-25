"""High-level command handlers for COBALT CLI."""

from __future__ import annotations

import json
import textwrap
from pathlib import Path
from typing import List, Optional

from .config import ConfigLoader
from .policy_guard import SafeWriter
from .report import ReportWriter
from .runlog import RunLog
from .scanner import RepoScanner
from .pr_review import PRReview
from .qa import QAExecutor, TriageReporter
from .utils import COBALT_DIR, REPO_ROOT


def _ensure_scanresults_dir() -> Path:
    scan_dir = COBALT_DIR / "_scanresults"
    scan_dir.mkdir(parents=True, exist_ok=True)
    return scan_dir


def run_init(_: Optional[List[str]] = None) -> None:
    """Validate configuration and ensure internal directories exist."""
    loader = ConfigLoader()
    loader.load_all()
    SafeWriter.validate()
    _ensure_scanresults_dir()
    print("COBALT init complete. Configuration validated.")


def run_scan(_: Optional[List[str]] = None) -> None:
    loader = ConfigLoader()
    config = loader.load_all()
    runlog = RunLog()
    scan_dir = _ensure_scanresults_dir()

    scanner = RepoScanner(config=config, runlog=runlog)
    scan_results = scanner.perform_scan()

    writer = ReportWriter(config=config, runlog=runlog, scan_dir=scan_dir)
    writer.write_outputs(scan_results)

    runlog_path = writer.write_runlog()
    print("Scan complete. Outputs:")
    for path in ["COBALT_INDEX.json", "COBALT_REPORT.md", "RUNLOG.txt"]:
        print(f"  - {scan_dir / path}")
    print(f"Run log captured at {runlog_path}")


def run_report(_: Optional[List[str]] = None) -> None:
    scan_dir = _ensure_scanresults_dir()
    report_path = scan_dir / "COBALT_REPORT.md"
    if not report_path.exists():
        raise SystemExit("No report found. Run 'cobalt scan' first.")
    print(report_path.read_text(encoding="utf-8"))


def run_pr_review(args: List[str]) -> None:
    if len(args) != 2:
        raise SystemExit("Usage: cobalt pr-review <base> <head>")
    base, head = args
    loader = ConfigLoader()
    config = loader.load_all()
    runlog = RunLog()
    reviewer = PRReview(config=config, runlog=runlog)
    summary = reviewer.review(base_ref=base, head_ref=head)
    print(summary)
    scan_dir = _ensure_scanresults_dir()
    writer = ReportWriter(config=config, runlog=runlog, scan_dir=scan_dir)
    writer.write_runlog(scan_dir / "RUNLOG_pr_review.txt")


def run_qa(args: Optional[List[str]] = None) -> None:
    loader = ConfigLoader()
    config = loader.load_all()
    runlog = RunLog()
    executor = QAExecutor(config=config, runlog=runlog)
    result = executor.execute()
    scan_dir = _ensure_scanresults_dir()
    writer = ReportWriter(config=config, runlog=runlog, scan_dir=scan_dir)
    writer.write_runlog(scan_dir / "RUNLOG_qa.txt")
    print("QA run complete:")
    print(json.dumps(result, indent=2))


def run_triage(args: Optional[List[str]] = None) -> None:
    reporter = TriageReporter()
    summary = reporter.triage_last()
    print(json.dumps(summary, indent=2))


def format_cli_summary(title: str, lines: List[str]) -> str:
    bullet = "\n".join(f" - {line}" for line in lines)
    return textwrap.dedent(
        f"""\
        {title}
        {bullet}
        """
    )
