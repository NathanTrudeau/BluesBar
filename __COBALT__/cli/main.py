"""Command line interface for COBALT."""

from __future__ import annotations

import argparse
import sys

from ..core import (
    run_init,
    run_scan,
    run_report,
    run_pr_review,
    run_qa,
    run_triage,
)


COMMANDS = {
    "init": run_init,
    "scan": run_scan,
    "report": run_report,
    "pr-review": run_pr_review,
    "qa-run": run_qa,
    "triage": run_triage,
}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="cobalt", description="COBALT Phase 1 CLI")
    parser.add_argument("command", choices=COMMANDS.keys())
    parser.add_argument("args", nargs=argparse.REMAINDER)
    return parser


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    ns = parser.parse_args(argv)
    handler = COMMANDS[ns.command]
    handler(ns.args)


if __name__ == "__main__":
    main(sys.argv[1:])
