from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict


def load_json(path: str) -> Any:
    p = Path(path)
    if not p.is_file():
        raise FileNotFoundError(f"JSON file not found: {path}")
    with p.open("r", encoding="utf-8") as f:
        return json.load(f)


def save_json(obj: Any, path: str, indent: int = 2) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with p.open("w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=indent)


def ensure_tactic_schema(tactic: Dict[str, Any]) -> None:
    """
    Light schema validation for your coach-authored tactic.
    Raises ValueError if required keys are missing.
    """
    if not isinstance(tactic, dict):
        raise ValueError("Tactic must be a dict.")

    if "meta" not in tactic or not isinstance(tactic["meta"], dict):
        raise ValueError("Tactic missing 'meta' dict.")

    if "frames" not in tactic or not isinstance(tactic["frames"], list):
        raise ValueError("Tactic missing 'frames' list.")
