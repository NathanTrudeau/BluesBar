"""Repo scanner for COBALT Phase 1."""

from __future__ import annotations

import datetime as dt
from pathlib import Path
from typing import Any, Dict, List

from .config import CobaltConfig
from .runlog import RunLog
from .utils import REPO_ROOT, glob_match, list_repo_files, run_command


class RepoScanner:
    """Performs read-only repository introspection."""

    def __init__(self, config: CobaltConfig, runlog: RunLog) -> None:
        self.config = config
        self.runlog = runlog

    def _run_git(self, *args: str) -> str:
        cmd = ["git", *args]
        result = run_command(cmd)
        self.runlog.record(cmd, result.returncode, result.stdout, result.stderr)
        return result.stdout.strip()

    def _collect_git_metadata(self) -> Dict[str, Any]:
        head = self._run_git("rev-parse", "HEAD")
        branch = self._run_git("rev-parse", "--abbrev-ref", "HEAD")
        status = self._run_git("status", "-sb")
        return {
            "head": head,
            "branch": branch,
            "status": status,
        }

    def _collect_repo_map(self) -> Dict[str, Any]:
        files = list_repo_files(max_files=5000)
        languages: Dict[str, int] = {}
        for rel in files:
            ext = Path(rel).suffix.lower()
            if ext:
                languages[ext] = languages.get(ext, 0) + 1
        top_languages = sorted(languages.items(), key=lambda kv: (-kv[1], kv[0]))[:10]
        directories: Dict[str, int] = {}
        for rel in files:
            directory = str(Path(rel).parent)
            directories[directory] = directories.get(directory, 0) + 1
        hotspots = [
            {"path": path, "file_count": count}
            for path, count in sorted(directories.items(), key=lambda kv: (-kv[1], kv[0]))
            if path not in (".", "__COBALT__")
        ][:10]
        return {
            "total_files_counted": len(files),
            "top_languages": top_languages,
            "hotspots": hotspots,
        }

    def _detect_build_commands(self) -> List[Dict[str, str]]:
        commands: List[Dict[str, str]] = []
        if (REPO_ROOT / "package.json").exists():
            commands.append({
                "command": "npm run build",
                "confidence": "MEDIUM",
                "reason": "package.json detected",
            })
            commands.append({
                "command": "npm test",
                "confidence": "MEDIUM",
                "reason": "package.json detected",
            })
        if (REPO_ROOT / "requirements.txt").exists():
            commands.append({
                "command": "pytest",
                "confidence": "LOW",
                "reason": "requirements.txt detected",
            })
        if not commands:
            commands.append({
                "command": "(none detected)",
                "confidence": "LOW",
                "reason": "No known build files",
            })
        return commands

    def _policy_findings(self) -> Dict[str, Any]:
        policies = self.config.policies
        denylist = policies.get("scopes", {}).get("denylist", [])
        confirm = policies.get("scopes", {}).get("confirm_first", [])
        hits_deny: List[str] = []
        hits_confirm: List[str] = []
        for rel in list_repo_files(max_files=3000):
            path = REPO_ROOT / rel
            if glob_match(path, denylist):
                hits_deny.append(rel)
            elif glob_match(path, confirm):
                hits_confirm.append(rel)
            if len(hits_deny) >= 100:
                break
        return {
            "denylist_matches": sorted(hits_deny),
            "confirm_first_matches": sorted(hits_confirm),
        }

    def perform_scan(self) -> Dict[str, Any]:
        timestamp = dt.datetime.utcnow().isoformat() + "Z"
        git_meta = self._collect_git_metadata()
        repo_map = self._collect_repo_map()
        build_commands = self._detect_build_commands()
        policy = self._policy_findings()
        findings: List[str] = []
        if policy["denylist_matches"]:
            findings.append(
                f"Denylist files present: {len(policy['denylist_matches'])} (read-only enforced)."
            )
        if repo_map["hotspots"]:
            top = repo_map["hotspots"][0]
            findings.append(
                f"Highest file concentration in '{top['path']}' with {top['file_count']} files."
            )
        return {
            "timestamp": timestamp,
            "git": git_meta,
            "repo_map": repo_map,
            "build_commands": build_commands,
            "policy": policy,
            "findings": findings,
        }
