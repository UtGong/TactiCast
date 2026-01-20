from __future__ import annotations

from typing import Dict, List, Tuple

from tacticast_viewpoint.config import AlgoConfig
from tacticast_viewpoint.core.geometry import dist
from tacticast_viewpoint.types import (
    Frame,
    FrameGraph,
    GraphEdge,
    GraphNode,
    Vec2,
)


def build_frame_graphs(
    frames: List[Frame],
    t_rel: List[float],
    v_by_frame: Dict[int, Dict[str, Vec2]],
    player_team: Dict[str, str],
    player_role: Dict[str, str],
    cfg: AlgoConfig,
) -> List[FrameGraph]:
    """
    Build per-frame graphs with nodes (players) and typed proximity edges.

    Nodes:
      - One per player (frame0 player set already enforced upstream)

    Edges:
      - TEAM_NEAR: teammate within cfg.teammate_radius
      - OPP_NEAR: opponent within cfg.opponent_radius
      - BALL_LINK: ball-to-player edge (always present as features only)

    Returns:
      List[FrameGraph] aligned with frames.
    """
    graphs: List[FrameGraph] = []

    for fr in frames:
        i = fr.frame_idx

        # Nodes
        nodes: Dict[str, GraphNode] = {}
        for pid, pos in fr.players.items():
            team = player_team.get(pid, "A")
            role = player_role.get(pid, "")
            vel = v_by_frame[i][pid]
            nodes[pid] = GraphNode(
                player_id=pid,
                team=team,   # type: ignore
                role=role,
                pos=pos,
                vel=vel,
            )

        # Edges
        edges: List[GraphEdge] = []
        pids = list(fr.players.keys())

        # Pairwise proximity edges
        for a in range(len(pids)):
            pa = pids[a]
            posa = fr.players[pa]
            ta = player_team.get(pa, "A")

            for b in range(len(pids)):
                if a == b:
                    continue
                pb = pids[b]
                posb = fr.players[pb]
                tb = player_team.get(pb, "A")

                d = dist(posa, posb)
                if ta == tb:
                    if d <= cfg.teammate_radius:
                        edges.append(
                            GraphEdge(
                                src=pa,
                                dst=pb,
                                etype="TEAM_NEAR",
                                features={"d": float(d)},
                            )
                        )
                else:
                    if d <= cfg.opponent_radius:
                        edges.append(
                            GraphEdge(
                                src=pa,
                                dst=pb,
                                etype="OPP_NEAR",
                                features={"d": float(d)},
                            )
                        )

        # Ball link edges (one per player, as features)
        bx, by = fr.ball_pos
        for pid, pos in fr.players.items():
            d = dist(pos, (bx, by))
            edges.append(
                GraphEdge(
                    src=pid,
                    dst=pid,
                    etype="BALL_LINK",
                    features={"ball_d": float(d), "ball_x": float(bx), "ball_y": float(by)},
                )
            )

        graphs.append(
            FrameGraph(
                frame_idx=i,
                nodes=nodes,
                edges=edges,
                ball_pos=fr.ball_pos,
                t_rel=t_rel[i],
            )
        )

    return graphs


# -----------------------
# Feature summaries
# -----------------------

def summarize_pressure_support(
    graph: FrameGraph,
    cfg: AlgoConfig,
) -> Dict[str, Dict[str, float]]:
    """
    Produce lightweight per-player summary features used by candidate generation and scoring.

    For each player p:
      - pressure_n: number of opponents within opponent_radius
      - min_opp_d: nearest opponent distance (inf if none)
      - support_n: number of teammates within teammate_radius
      - min_team_d: nearest teammate distance (inf if none)
      - ball_d: distance to ball
    """
    out: Dict[str, Dict[str, float]] = {}

    # init
    for pid in graph.nodes.keys():
        out[pid] = {
            "pressure_n": 0.0,
            "min_opp_d": float("inf"),
            "support_n": 0.0,
            "min_team_d": float("inf"),
            "ball_d": dist(graph.nodes[pid].pos, graph.ball_pos),
        }

    # scan edges
    for e in graph.edges:
        if e.etype == "OPP_NEAR":
            d = float(e.features["d"])
            out[e.src]["pressure_n"] += 1.0
            if d < out[e.src]["min_opp_d"]:
                out[e.src]["min_opp_d"] = d

        elif e.etype == "TEAM_NEAR":
            d = float(e.features["d"])
            out[e.src]["support_n"] += 1.0
            if d < out[e.src]["min_team_d"]:
                out[e.src]["min_team_d"] = d

        elif e.etype == "BALL_LINK":
            # already computed above, but keep consistent if needed
            out[e.src]["ball_d"] = float(e.features["ball_d"])

    return out
