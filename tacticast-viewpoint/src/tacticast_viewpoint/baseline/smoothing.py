from __future__ import annotations

from typing import Dict, List, Optional

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.types import ScoredEvent


def apply_temporal_smoothing(
    scored_by_frame: Dict[int, List[ScoredEvent]],
    cfg: AlgoConfig,
) -> Dict[int, List[ScoredEvent]]:
    """
    Apply temporal smoothing to scored events for a single player.

    Returns a NEW dict with NEW ScoredEvent objects (ScoredEvent is frozen).
    """
    if not scored_by_frame:
        return scored_by_frame

    out: Dict[int, List[ScoredEvent]] = {}
    prev_primary: Optional[ScoredEvent] = None

    for i in sorted(scored_by_frame.keys()):
        events = scored_by_frame[i]
        if not events:
            out[i] = []
            continue

        if prev_primary is None:
            out[i] = list(events)
            prev_primary = out[i][0]
            continue

        adjusted: List[ScoredEvent] = []
        for ev in events:
            delta = cfg.persistence_bonus if _same_focus(ev, prev_primary) else -cfg.switch_penalty
            adjusted.append(
                ScoredEvent(
                    name=ev.name,
                    score=float(ev.score + delta),
                    focus=ev.focus,
                    reasons=list(ev.reasons) + (["persist_bonus"] if delta > 0 else ["switch_penalty"]),
                    meta=ev.meta,
                )
            )

        adjusted.sort(key=lambda e: e.score, reverse=True)
        out[i] = adjusted
        prev_primary = adjusted[0]

    return out


def _same_focus(a: ScoredEvent, b: ScoredEvent) -> bool:
    if a.focus.target_type != b.focus.target_type:
        return False
    if a.focus.target_player_id != b.focus.target_player_id:
        return False

    ax, ay = a.focus.anchor
    bx, by = b.focus.anchor
    return abs(ax - bx) <= 1e-3 and abs(ay - by) <= 1e-3
