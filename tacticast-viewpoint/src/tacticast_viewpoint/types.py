from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Literal, Optional, Tuple


# -----------------------
# Basic domain primitives
# -----------------------

Vec2 = Tuple[float, float]
TeamId = Literal["A", "B"]

FocusTargetType = Literal["BALL", "PLAYER", "ZONE", "GOAL"]


@dataclass(frozen=True)
class Pitch:
    """Pitch dimensions in meters (or consistent unit)."""
    length: float
    width: float


@dataclass(frozen=True)
class TeamMeta:
    name: str
    color: str  # hex color string


@dataclass(frozen=True)
class PlayerMeta:
    id: str
    team: TeamId
    label: str  # jersey number string
    role: str   # e.g., GK, CB, ST


@dataclass(frozen=True)
class TacticMeta:
    tactic_id: str
    title: str
    pitch: Pitch
    teams: Dict[TeamId, TeamMeta]
    players: Dict[str, PlayerMeta]  # player_id -> meta
    last_modified: Optional[int] = None


# -----------------------
# Input / canonical frames
# -----------------------

@dataclass(frozen=True)
class RawBall:
    x: Optional[float]
    y: Optional[float]
    owner_id: Optional[str] = None


@dataclass(frozen=True)
class RawFrame:
    """
    User-provided frame (keyframe).

    IMPORTANT CONTRACT:
      - Order in the list defines frame_idx (index only).
      - Frame IDs are NOT timestamps.
      - player_pos may include noise; canonicalization will enforce frame0 player set.
    """
    id: str
    player_pos: Dict[str, Vec2]
    ball: RawBall
    note: str = ""


@dataclass
class Frame:
    """
    Canonical frame used by the algorithm.

    - players includes ONLY the valid player set P = players_in_frame0
    - positions may be filled/interpolated by canonicalization
    """
    frame_idx: int
    players: Dict[str, Vec2]  # player_id -> (x,y)
    ball_pos: Vec2            # (x,y), filled if missing by carry-forward
    ball_owner_id: Optional[str] = None
    note: str = ""


# -----------------------
# Graph and candidates
# -----------------------

EdgeType = Literal["TEAM_NEAR", "OPP_NEAR", "BALL_LINK"]


@dataclass(frozen=True)
class GraphNode:
    """
    A node in the per-frame graph.
    For now we only model players explicitly; ball is treated as a special feature
    (you may add a dedicated ball node later for GNN pretraining).
    """
    player_id: str
    team: TeamId
    role: str
    pos: Vec2
    vel: Vec2  # inferred from pseudo-time


@dataclass(frozen=True)
class GraphEdge:
    src: str
    dst: str
    etype: EdgeType
    features: Dict[str, float]


@dataclass(frozen=True)
class FrameGraph:
    frame_idx: int
    nodes: Dict[str, GraphNode]  # player_id -> node
    edges: List[GraphEdge]
    ball_pos: Vec2
    t_rel: float  # relative time since start (pseudo-time)


@dataclass(frozen=True)
class FocusTarget:
    """
    A target the player should focus on.

    - BALL: anchor is ball position
    - PLAYER: anchor is target player's position
    - ZONE: anchor is an (x,y) in space (e.g., open lane)
    - GOAL: anchor is goal center (attacking direction +x implies opponent goal)
    """
    target_type: FocusTargetType
    anchor: Vec2
    target_player_id: Optional[str] = None
    tag: Optional[str] = None  # short label for debugging/UI


@dataclass(frozen=True)
class CandidateEvent:
    """
    Interpretable candidate event for attention.
    """
    name: str
    focus: FocusTarget
    features: Dict[str, float]  # candidate-specific features for scoring / learning
    meta: Dict[str, Any]        # optional extra debug payload


@dataclass(frozen=True)
class ScoredEvent:
    name: str
    score: float
    focus: FocusTarget
    reasons: List[str]
    meta: Dict[str, Any]


# -----------------------
# Final output
# -----------------------

@dataclass(frozen=True)
class PlayerFocusRecommendation:
    player_id: str
    frame_idx: int
    t_rel: Optional[float]
    primary: FocusTarget
    primary_score: float
    rationale: List[str]
    top_k: List[ScoredEvent]
