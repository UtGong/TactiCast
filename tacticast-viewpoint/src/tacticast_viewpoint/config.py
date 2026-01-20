from __future__ import annotations

from dataclasses import dataclass
from typing import Dict


@dataclass
class AlgoConfig:
    """
    Configuration for TactiCast Viewpoint (v2).

    Design goals:
    - Deterministic by default (baseline reproducibility)
    - Explicit weights (paper-friendly ablations)
    - Safe defaults for sparse keyframes
    """

    # -----------------------
    # Core behavior
    # -----------------------

    top_k: int = 1
    attacking_team: str = "A"
    attack_direction: int = +1  # +1 means +x direction

    # -----------------------
    # Pseudo-time inference
    # -----------------------

    # Max speed (m/s) assumed for normalization when inferring time
    max_player_speed: float = 8.0

    # Minimum delta-t between frames (avoid zero)
    min_dt: float = 0.2

    # -----------------------
    # Graph construction
    # -----------------------

    teammate_radius: float = 12.0
    opponent_radius: float = 10.0

    # -----------------------
    # Candidate generation
    # -----------------------

    enable_ball_focus: bool = True
    enable_pass_targets: bool = True
    enable_marking_threats: bool = True
    enable_space_targets: bool = True
    enable_goal_focus: bool = True

    # Space / lane heuristics
    space_grid_dx: float = 6.0
    space_grid_dy: float = 6.0
    min_space_clearance: float = 4.0

    # -----------------------
    # Deterministic scoring weights
    # (baseline policy)
    # -----------------------

    w_ball_distance: float = -1.0
    w_ball_motion: float = 1.5
    w_pass_likelihood: float = 3.0

    w_opponent_pressure: float = 2.0
    w_teammate_support: float = 0.8

    w_space_value: float = 1.2
    w_goal_proximity: float = 2.5

    w_role_prior: float = 1.0

    # -----------------------
    # Temporal smoothing
    # -----------------------

    # Penalty for switching focus between frames
    switch_penalty: float = 1.5

    # Encourage persistence of previous focus
    persistence_bonus: float = 0.8

    # -----------------------
    # Debug / safety
    # -----------------------

    clamp_scores: bool = True
    score_min: float = -10.0
    score_max: float = 10.0

    def as_dict(self) -> Dict[str, float]:
        """Convenience for logging / experiment tracking."""
        return self.__dict__.copy()
