from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple

from tacticast_viewpoint.baseline.policy import run_baseline_policy
from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.core import canonicalize_frames, parse_meta, select_tactic
from tacticast_viewpoint.io import ensure_tactic_schema
from tacticast_viewpoint.types import RawBall, RawFrame, TacticMeta, Frame, PlayerFocusRecommendation


def recommend_player_focus(
    tactic_data: Any,
    cfg: Optional[AlgoConfig] = None,
    *,
    tactic_id: Optional[str] = None,
    tactic_index: int = 0,
) -> Dict[str, List[PlayerFocusRecommendation]]:
    """
    Public API: compute per-player, per-frame focus recommendations.

    Inputs:
      tactic_data:
        - a single tactic dict, or
        - a list of tactic dicts (DB export), in which case you must select one
          (tactic_id or tactic_index).

    Output:
      dict[player_id] -> list[PlayerFocusRecommendation] (len = num_frames)

    Guarantees:
      - Only players included in the FIRST frame are processed (frame0 player set).
      - Exactly one primary focus per player per frame.
    """
    if cfg is None:
        cfg = AlgoConfig()

    tactic = select_tactic(tactic_data, tactic_id=tactic_id, tactic_index=tactic_index)
    ensure_tactic_schema(tactic)

    meta = parse_meta(tactic["meta"])
    raw_frames = _parse_raw_frames(tactic["frames"])

    frames, valid_player_ids = canonicalize_frames(raw_frames)

    # Build player maps
    player_team: Dict[str, str] = {}
    player_role: Dict[str, str] = {}
    for pid in valid_player_ids:
        pmeta = meta.players.get(pid)
        if pmeta is None:
            # if not listed in meta.players, fallback to team A
            player_team[pid] = "A"
            player_role[pid] = ""
        else:
            player_team[pid] = pmeta.team
            player_role[pid] = pmeta.role

    # Run baseline
    recs = run_baseline_policy(
        frames=frames,
        pitch=meta.pitch,
        player_team=player_team,
        player_role=player_role,
        cfg=cfg,
    )

    return recs


def _parse_raw_frames(frames_list: List[Dict[str, Any]]) -> List[RawFrame]:
    raw: List[RawFrame] = []
    for f in frames_list:
        ball = f.get("ball", {}) or {}
        rb = RawBall(
            x=ball.get("x"),
            y=ball.get("y"),
            owner_id=ball.get("owner_id"),
        )

        player_pos = {}
        for pid, xy in (f.get("player_pos", {}) or {}).items():
            if not isinstance(xy, (list, tuple)) or len(xy) != 2:
                continue
            player_pos[str(pid)] = (float(xy[0]), float(xy[1]))

        raw.append(
            RawFrame(
                id=str(f.get("id", "")),
                player_pos=player_pos,
                ball=rb,
                note=str(f.get("note", "") or ""),
            )
        )
    if not raw:
        raise ValueError("No frames parsed from tactic data.")
    return raw
