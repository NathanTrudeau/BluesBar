"""Configuration loading helpers for COBALT."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict

from .utils import COBALT_DIR

try:
    import yaml  # type: ignore
except ImportError:  # pragma: no cover
    yaml = None  # type: ignore


@dataclass
class CobaltConfig:
    policies: Dict[str, Any]
    models: Dict[str, Any]
    commands_doc: str
    personality_doc: str
    role_prompts: Dict[str, str]


class ConfigLoader:
    def __init__(self, root: Path | None = None) -> None:
        self.root = root or COBALT_DIR

    def _read_text(self, relative: str) -> str:
        path = self.root / relative
        return path.read_text(encoding="utf-8")

    def _load_yaml(self, relative: str) -> Dict[str, Any]:
        if yaml is None:
            raise RuntimeError(
                "PyYAML is required. Install it via 'pip install pyyaml' inside the repo environment."
            )
        path = self.root / relative
        with path.open("r", encoding="utf-8") as fh:
            data = yaml.safe_load(fh)  # type: ignore[arg-type]
        if not isinstance(data, dict):
            raise ValueError(f"Expected mapping at {relative}")
        return data

    def load_all(self) -> CobaltConfig:
        policies = self._load_yaml("policies.yaml")
        models = self._load_yaml("models.yaml")
        commands_doc = self._read_text("commands.md")
        personality_doc = self._read_text("prompts/personality.md")
        prompts_dir = self.root / "prompts"
        role_prompts = {}
        for path in sorted(prompts_dir.glob("*.md")):
            if path.name == "personality.md":
                continue
            role_prompts[path.name] = path.read_text(encoding="utf-8")
        return CobaltConfig(
            policies=policies,
            models=models,
            commands_doc=commands_doc,
            personality_doc=personality_doc,
            role_prompts=role_prompts,
        )
