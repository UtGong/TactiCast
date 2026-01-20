from __future__ import annotations

from typing import Any, Dict, Optional


def select_tactic(
    obj: Any,
    tactic_id: Optional[str] = None,
    tactic_index: int = 0,
) -> Dict[str, Any]:
    """
    Select a single tactic dict from either:
      - a single tactic object (dict), or
      - a list of tactic objects (DB export).

    Selection rules when obj is a list:
      - if tactic_id is provided, select first tactic whose meta.tactic_id matches
      - else select tactic_index (default 0)

    Returns:
      tactic: Dict[str, Any] with keys: "meta", "frames"
    """
    if isinstance(obj, dict):
        return obj

    if isinstance(obj, list):
        if not obj:
            raise ValueError("Tactic JSON is an empty list.")

        if tactic_id is not None:
            tid = str(tactic_id)
            for t in obj:
                if isinstance(t, dict) and isinstance(t.get("meta"), dict):
                    if str(t["meta"].get("tactic_id", "")) == tid:
                        return t
            raise ValueError(f"tactic_id '{tactic_id}' not found in tactic list.")

        idx = int(tactic_index)
        if idx < 0 or idx >= len(obj):
            raise ValueError(f"tactic_index {idx} out of range (len={len(obj)}).")

        t = obj[idx]
        if not isinstance(t, dict):
            raise ValueError(f"tactic_data[{idx}] is not a dict.")
        return t

    raise ValueError("Tactic JSON must be a dict (single tactic) or list (tactic DB export).")
