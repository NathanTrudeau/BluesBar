"""Write guard helpers enforcing policies."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from .config import ConfigLoader
from .utils import COBALT_DIR, REPO_ROOT


@dataclass
class SafeWriter:
    allowed_roots: tuple[Path, ...]

    @classmethod
    def validate(cls) -> "SafeWriter":
        loader = ConfigLoader()
        policies = loader.load_all().policies
        always_paths = policies.get("write_controls", {}).get("always_allowed_write_paths", [])
        allowed = []
        for rel in always_paths:
            if rel == "__COBALT__/**":
                allowed.append(COBALT_DIR)
        if not allowed:
            allowed.append(COBALT_DIR)
        return cls(tuple(allowed))

    def ensure_allowed(self, path: Path) -> None:
        normalized = path.resolve()
        if not any(normalized.is_relative_to(root) for root in self.allowed_roots):  # type: ignore[attr-defined]
            raise PermissionError(f"Writes outside allowed paths are forbidden: {normalized}")

    def safe_write(self, path: Path, content: str) -> None:
        self.ensure_allowed(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


__all__ = ["SafeWriter"]
