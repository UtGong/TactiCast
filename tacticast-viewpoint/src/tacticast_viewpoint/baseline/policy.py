from __future__ import annotations

from typing import Dict, List

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.baseline.candidates import generate_candidates_for_player
from tacticast_viewpoint.baseline.graph import build_frame_graphs, summarize_pressure_support
from tacticast_viewpoint.baseline.scoring import score_candidates
from tacticast_viewpoint.baseline.smoothing import apply_temporal_smoothing
from tacticast_viewpoint.core.timebase import compute_velocities, infer_pseudotime
from tacticast_viewpoint.types import Frame, Pitch, PlayerFocusRecommendation, ScoredEvent


def run_baseline_policy(
    frames: List[Frame],
    pitch: Pitch,
    player_team: Dict[str, str],
    player_role: Dict[str, str],
    cfg: AlgoConfig,
) -> Dict[str, List[PlayerFocusRecommendation]]:
    """
    Run the deterministic baseline end-to-end.

    Output:
      player_id -> list of PlayerFocusRecommendation, length = num_frames

    Guarantees:
      - Only players present in frames[0] are processed (already enforced upstream)
      - Exactly one primary focus per player per frame
      - Optional top_k list retained for later learning (but top_k=1 recommended for VR arrow)
    """
    dt, t_rel = infer_pseudotime(frames, cfg)
    v_by_frame = compute_velocities(frames, dt)
    graphs = build_frame_graphs(
        frames=frames,
        t_rel=t_rel,
        v_by_frame=v_by_frame,
        player_team=player_team,
        player_role=player_role,
        cfg=cfg,
    )

    # Prepare per-player per-frame scored events
    scored_per_player: Dict[str, Dict[int, List[ScoredEvent]]] = {}
    player_ids = list(frames[0].players.keys())

    for pid in player_ids:
        scored_per_player[pid] = {}

    for g in graphs:
        summaries = summarize_pressure_support(g, cfg)
        for pid in player_ids:
            role = player_role.get(pid, "")
            cands = generate_candidates_for_player(
                graph=g,
                pitch=pitch,
                player_id=pid,
                player_team=player_team,
                cfg=cfg,
                summaries=summaries,
            )
            scored = score_candidates(
                graph=g,
                player_id=pid,
                candidates=cands,
                summaries=summaries,
                cfg=cfg,
                role=role,
            )
            scored_per_player[pid][g.frame_idx] = scored

    # Smoothing per player
    for pid in player_ids:
        scored_per_player[pid] = apply_temporal_smoothing(scored_per_player[pid], cfg)

    # Final recommendations
    out: Dict[str, List[PlayerFocusRecommendation]] = {pid: [] for pid in player_ids}

    for pid in player_ids:
        for i in range(len(frames)):
            ranked = scored_per_player[pid].get(i, [])
            if not ranked:
                # fallback: look at ball
                primary_focus = ("BALL", frames[i].ball_pos, None, "ball")
                primary_score = 0.0
                rationale = ["fallback_ball"]
                topk = []
            else:
                primary = ranked[0]
                primary_focus = (
                    primary.focus.target_type,
                    primary.focus.anchor,
                    primary.focus.target_player_id,
                    primary.focus.tag,
                )
                primary_score = float(primary.score)
                rationale = list(primary.reasons)
                topk = ranked[: max(1, int(cfg.top_k))]

            out[pid].append(
                PlayerFocusRecommendation(
                    player_id=pid,
                    frame_idx=i,
                    t_rel=t_rel[i],
                    primary=topk[0].focus if topk else _fallback_focus(frames[i]),
                    primary_score=primary_score,
                    rationale=rationale,
                    top_k=topk,
                )
            )

    return out


def _fallback_focus(fr: Frame):
    from tacticast_viewpoint.types import FocusTarget
    return FocusTarget(target_type="BALL", anchor=fr.ball_pos, tag="ball")
