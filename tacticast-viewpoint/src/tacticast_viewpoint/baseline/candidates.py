from __future__ import annotations

from typing import Dict, List, Optional, Tuple

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.core.geometry import (
    attacking_goal_center,
    dist,
    in_forward_cone,
    is_ahead,
)
from tacticast_viewpoint.types import (
    CandidateEvent,
    FocusTarget,
    FrameGraph,
    Pitch,
    Vec2,
)


def generate_candidates_for_player(
    graph: FrameGraph,
    pitch: Pitch,
    player_id: str,
    player_team: Dict[str, str],
    cfg: AlgoConfig,
    summaries: Dict[str, Dict[str, float]],
) -> List[CandidateEvent]:
    """
    Generate interpretable candidate focus targets for one player at one frame.

    Candidates are meant to be:
    - small in number
    - soccer-reasonable
    - stable enough for deterministic ranking and future learning

    Returns:
      List[CandidateEvent]
    """
    if player_id not in graph.nodes:
        return []

    node = graph.nodes[player_id]
    pos = node.pos
    bx, by = graph.ball_pos

    out: List[CandidateEvent] = []

    # 1) Ball focus
    if cfg.enable_ball_focus:
        out.append(
            CandidateEvent(
                name="BALL_NEARBY",
                focus=FocusTarget(
                    target_type="BALL",
                    anchor=(bx, by),
                    tag="ball",
                ),
                features={
                    "ball_d": float(summaries[player_id]["ball_d"]),
                },
                meta={},
            )
        )

    # 2) Nearest opponent pressure (if any opponents exist)
    if cfg.enable_marking_threats:
        opp_id, opp_d = _nearest_opponent(graph, player_id, player_team)
        if opp_id is not None:
            opp_pos = graph.nodes[opp_id].pos
            out.append(
                CandidateEvent(
                    name="OPP_PRESSURE",
                    focus=FocusTarget(
                        target_type="PLAYER",
                        anchor=opp_pos,
                        target_player_id=opp_id,
                        tag="press",
                    ),
                    features={
                        "opp_d": float(opp_d),
                        "pressure_n": float(summaries[player_id]["pressure_n"]),
                    },
                    meta={"opponent_id": opp_id},
                )
            )

    # 3) Best teammate support option (simple: closest teammate ahead-ish)
    if cfg.enable_pass_targets:
        mate_id, mate_score = _best_support_teammate(graph, player_id, player_team, cfg)
        if mate_id is not None:
            mate_pos = graph.nodes[mate_id].pos
            out.append(
                CandidateEvent(
                    name="TEAM_SUPPORT",
                    focus=FocusTarget(
                        target_type="PLAYER",
                        anchor=mate_pos,
                        target_player_id=mate_id,
                        tag="support",
                    ),
                    features={
                        "mate_score": float(mate_score),
                        "support_n": float(summaries[player_id]["support_n"]),
                    },
                    meta={"teammate_id": mate_id},
                )
            )

    # 4) Open forward space target (grid sample)
    if cfg.enable_space_targets:
        space_anchor, space_value = _best_open_space_anchor(
            graph=graph,
            pitch=pitch,
            player_id=player_id,
            player_team=player_team,
            cfg=cfg,
        )
        if space_anchor is not None:
            out.append(
                CandidateEvent(
                    name="OPEN_SPACE",
                    focus=FocusTarget(
                        target_type="ZONE",
                        anchor=space_anchor,
                        tag="space",
                    ),
                    features={
                        "space_value": float(space_value),
                    },
                    meta={},
                )
            )

    # 5) Goal focus (attacking direction)
    if cfg.enable_goal_focus:
        goal = attacking_goal_center(pitch, cfg.attack_direction)
        out.append(
            CandidateEvent(
                name="GOAL",
                focus=FocusTarget(
                    target_type="GOAL",
                    anchor=goal,
                    tag="goal",
                ),
                features={
                    "goal_d": float(dist(pos, goal)),
                },
                meta={},
            )
        )

    return out


# -----------------------
# Helper selection logic
# -----------------------

def _nearest_opponent(
    graph: FrameGraph,
    player_id: str,
    player_team: Dict[str, str],
) -> Tuple[Optional[str], float]:
    team = player_team.get(player_id, "A")
    pos = graph.nodes[player_id].pos

    best_id: Optional[str] = None
    best_d = float("inf")

    for other_id, n in graph.nodes.items():
        if other_id == player_id:
            continue
        if player_team.get(other_id, "A") == team:
            continue
        d = dist(pos, n.pos)
        if d < best_d:
            best_d = d
            best_id = other_id

    return best_id, best_d


def _best_support_teammate(
    graph: FrameGraph,
    player_id: str,
    player_team: Dict[str, str],
    cfg: AlgoConfig,
) -> Tuple[Optional[str], float]:
    """
    Simple support heuristic:
      - prefer teammates that are ahead (in +x) and in forward cone
      - closer is better among those candidates
    """
    team = player_team.get(player_id, "A")
    pos = graph.nodes[player_id].pos

    best_id: Optional[str] = None
    best_score = -1e9

    for other_id, n in graph.nodes.items():
        if other_id == player_id:
            continue
        if player_team.get(other_id, "A") != team:
            continue

        d = dist(pos, n.pos)

        ahead = 1.0 if is_ahead(n.pos, pos, cfg.attack_direction) else 0.0
        cone = 1.0 if in_forward_cone(pos, n.pos, cfg.attack_direction, cos_threshold=0.3) else 0.0

        # score favors forward + not too far
        score = 2.0 * ahead + 1.0 * cone - 0.15 * d

        if score > best_score:
            best_score = score
            best_id = other_id

    return best_id, best_score


def _best_open_space_anchor(
    graph: FrameGraph,
    pitch: Pitch,
    player_id: str,
    player_team: Dict[str, str],
    cfg: AlgoConfig,
) -> Tuple[Optional[Vec2], float]:
    """
    Sample a grid in front of player, choose a point with max clearance value.

    space_value heuristic:
      - reward distance from nearest opponent
      - slight reward for being forward (larger x in attack direction)
      - penalize being too far from the player (stability)
    """
    pos = graph.nodes[player_id].pos
    px, py = pos

    # define a forward sampling window
    # attack_direction=+1: sample x in [px, px+L]
    # attack_direction=-1: sample x in [px-L, px]
    L = 24.0
    if cfg.attack_direction > 0:
        x0, x1 = px, min(pitch.length, px + L)
    else:
        x0, x1 = max(0.0, px - L), px

    y0, y1 = max(0.0, py - 18.0), min(pitch.width, py + 18.0)

    best_anchor: Optional[Vec2] = None
    best_value = -1e9

    x = x0
    while x <= x1 + 1e-6:
        y = y0
        while y <= y1 + 1e-6:
            anchor = (float(x), float(y))

            # clearance from nearest opponent
            min_opp = float("inf")
            for oid, on in graph.nodes.items():
                if oid == player_id:
                    continue
                if player_team.get(oid, "A") == player_team.get(player_id, "A"):
                    continue
                d = dist(anchor, on.pos)
                if d < min_opp:
                    min_opp = d

            # reject cramped anchors
            if min_opp < cfg.min_space_clearance:
                y += cfg.space_grid_dy
                continue

            # forward reward: normalize by pitch length
            forward_progress = (anchor[0] / pitch.length) if cfg.attack_direction > 0 else (1.0 - anchor[0] / pitch.length)

            # distance penalty to avoid far anchors
            d_self = dist(anchor, pos)

            value = 1.5 * min_opp + 8.0 * forward_progress - 0.25 * d_self

            if value > best_value:
                best_value = value
                best_anchor = anchor

            y += cfg.space_grid_dy
        x += cfg.space_grid_dx

    if best_anchor is None:
        return None, -1e9

    return best_anchor, best_value