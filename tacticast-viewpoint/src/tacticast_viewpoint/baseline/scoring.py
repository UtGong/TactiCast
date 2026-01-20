from __future__ import annotations

from typing import Dict, List, Tuple

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.core.geometry import dist, in_forward_cone, is_ahead
from tacticast_viewpoint.types import CandidateEvent, FrameGraph, ScoredEvent


def score_candidates(
    graph: FrameGraph,
    player_id: str,
    candidates: List[CandidateEvent],
    summaries: Dict[str, Dict[str, float]],
    cfg: AlgoConfig,
    role: str = "",
) -> List[ScoredEvent]:
    """
    Deterministically score candidate events for a player at one frame.

    Output is a ranked list of ScoredEvent (descending score).

    Notes:
    - This is the publishable baseline: interpretable features + explicit weights.
    - role can be used to apply role priors without hardcoding soccer rules too aggressively.
    """
    if player_id not in graph.nodes:
        return []

    pos = graph.nodes[player_id].pos

    scored: List[ScoredEvent] = []
    for c in candidates:
        s, reasons = _score_one(graph, player_id, pos, c, summaries, cfg, role=role)
        if cfg.clamp_scores:
            s = max(cfg.score_min, min(cfg.score_max, s))

        scored.append(
            ScoredEvent(
                name=c.name,
                score=float(s),
                focus=c.focus,
                reasons=reasons,
                meta=c.meta,
            )
        )

    scored.sort(key=lambda e: e.score, reverse=True)
    return scored


def _score_one(
    graph: FrameGraph,
    player_id: str,
    pos: Tuple[float, float],
    c: CandidateEvent,
    summaries: Dict[str, Dict[str, float]],
    cfg: AlgoConfig,
    role: str = "",
) -> Tuple[float, List[str]]:
    s = 0.0
    reasons: List[str] = []

    # Shared context
    ball_d = float(summaries[player_id]["ball_d"])
    pressure_n = float(summaries[player_id]["pressure_n"])
    min_opp_d = float(summaries[player_id]["min_opp_d"])
    support_n = float(summaries[player_id]["support_n"])

    # Role prior (light touch)
    role_prior = _role_prior(role, c.name)
    if role_prior != 0.0:
        s += cfg.w_role_prior * role_prior
        reasons.append(f"role_prior({role})={role_prior:+.2f}")

    # Candidate-specific scoring
    if c.name == "BALL_NEARBY":
        # nearer ball -> higher (note cfg.w_ball_distance is negative)
        s += cfg.w_ball_distance * ball_d
        reasons.append(f"ball_d={ball_d:.2f}")

        # motion cue: if player is in front cone of ball relative to attack direction
        # (rough proxy for "ball path relevance" without ownership)
        if in_forward_cone(pos, graph.ball_pos, cfg.attack_direction, cos_threshold=0.0):
            s += cfg.w_ball_motion * 0.6
            reasons.append("ball_in_forward_half")

    elif c.name == "OPP_PRESSURE":
        opp_d = float(c.features.get("opp_d", min_opp_d))
        # more pressure and closer opponent => higher
        if opp_d < float("inf"):
            s += cfg.w_opponent_pressure * (1.0 / max(opp_d, 0.5))
            reasons.append(f"opp_d={opp_d:.2f}")
        s += cfg.w_opponent_pressure * 0.2 * pressure_n
        reasons.append(f"pressure_n={pressure_n:.0f}")

    elif c.name == "TEAM_SUPPORT":
        mate_score = float(c.features.get("mate_score", 0.0))
        s += cfg.w_teammate_support * mate_score
        reasons.append(f"mate_score={mate_score:.2f}")
        s += cfg.w_teammate_support * 0.1 * support_n
        reasons.append(f"support_n={support_n:.0f}")

        # pass likelihood proxy: if ball is relatively near player, support becomes more relevant
        if ball_d < 18.0:
            s += cfg.w_pass_likelihood * (1.0 - ball_d / 18.0)
            reasons.append("ball_close_boost_for_pass")

    elif c.name == "OPEN_SPACE":
        space_value = float(c.features.get("space_value", 0.0))
        s += cfg.w_space_value * space_value
        reasons.append(f"space_value={space_value:.2f}")

        # also prefer space that is ahead of the player
        if is_ahead(c.focus.anchor, pos, cfg.attack_direction):
            s += cfg.w_space_value * 0.5
            reasons.append("space_ahead_bonus")

    elif c.name == "GOAL":
        goal_d = float(c.features.get("goal_d", dist(pos, c.focus.anchor)))
        s += cfg.w_goal_proximity * (1.0 / max(goal_d, 1.0))
        reasons.append(f"goal_d={goal_d:.2f}")

        # goal focus becomes more relevant if ball is closer (proxy for attack phase)
        if ball_d < 25.0:
            s += cfg.w_goal_proximity * 0.4
            reasons.append("ball_close_goal_bonus")

    else:
        # fallback: no score
        reasons.append("unknown_candidate")

    return s, reasons


def _role_prior(role: str, candidate_name: str) -> float:
    """
    Small, interpretable priors:
    - GK/CB: more sensitive to OPP_PRESSURE
    - ST/W: more sensitive to GOAL / OPEN_SPACE
    - CM/CDM: more sensitive to TEAM_SUPPORT
    """
    r = role.upper().strip()

    if candidate_name == "OPP_PRESSURE":
        if r in {"GK", "CB", "LB", "RB"}:
            return 0.8
        if r in {"ST", "LW", "RW"}:
            return 0.2

    if candidate_name == "TEAM_SUPPORT":
        if r in {"CM", "CDM"}:
            return 0.7

    if candidate_name == "OPEN_SPACE":
        if r in {"ST", "LW", "RW", "CM"}:
            return 0.5

    if candidate_name == "GOAL":
        if r in {"ST", "LW", "RW"}:
            return 0.8

    return 0.0
