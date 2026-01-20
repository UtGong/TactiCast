from __future__ import annotations

from typing import Dict, List, Tuple

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.types import Frame, Vec2


def infer_pseudotime(frames: List[Frame], cfg: AlgoConfig) -> Tuple[List[float], List[float]]:
    """
    Infer pseudo-time for ordered keyframes.

    Contract reminder:
    - Frame order defines index, not timestamp.
    - We infer dt from maximum player displacement to obtain stable velocity features.

    For i=0: dt[0] = 0, t_rel[0] = 0
    For i>0:
      dmax_i = max_p ||x_p^i - x_p^{i-1}||
      dt_i = clamp(dmax_i / max_player_speed, min_dt, +inf)
      t_rel[i] = sum_{k<=i} dt_k

    Returns:
      dt: list length N (dt[0]=0)
      t_rel: list length N (t_rel[0]=0)
    """
    if not frames:
        raise ValueError("No frames provided.")

    n = len(frames)
    dt = [0.0] * n
    t = [0.0] * n

    for i in range(1, n):
        prev = frames[i - 1]
        cur = frames[i]

        dmax = 0.0
        for pid, (x, y) in cur.players.items():
            x0, y0 = prev.players[pid]
            dx = x - x0
            dy = y - y0
            d = (dx * dx + dy * dy) ** 0.5
            if d > dmax:
                dmax = d

        # convert to dt using nominal max speed
        raw_dt = dmax / max(cfg.max_player_speed, 1e-6)
        dt_i = max(cfg.min_dt, raw_dt)

        dt[i] = dt_i
        t[i] = t[i - 1] + dt_i

    return dt, t


def compute_velocities(frames: List[Frame], dt: List[float]) -> Dict[int, Dict[str, Vec2]]:
    """
    Compute per-frame per-player velocities using finite differences.
    Output is keyed by frame_idx.

    For i=0: velocity = (0,0)
    For i>0: v = (pos_i - pos_{i-1}) / dt_i
    """
    if len(frames) != len(dt):
        raise ValueError("frames and dt must have same length.")

    v_by_frame: Dict[int, Dict[str, Vec2]] = {}
    n = len(frames)

    # frame 0
    v_by_frame[0] = {pid: (0.0, 0.0) for pid in frames[0].players.keys()}

    for i in range(1, n):
        cur = frames[i]
        prev = frames[i - 1]
        denom = max(dt[i], 1e-6)

        v_i: Dict[str, Vec2] = {}
        for pid, (x, y) in cur.players.items():
            x0, y0 = prev.players[pid]
            v_i[pid] = ((x - x0) / denom, (y - y0) / denom)

        v_by_frame[i] = v_i

    return v_by_frame
