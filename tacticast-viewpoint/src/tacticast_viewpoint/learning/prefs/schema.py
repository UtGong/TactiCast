from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Literal, Optional, Tuple

HitType = Literal["player", "ball", "pitch", "ui", "none"]
PlaybackState = Literal["playing", "paused", "scrubbing", "replaying"]
TargetType = Literal["player", "ball", "zone", "goal"]

Vec3 = Tuple[float, float, float]
Quat = Tuple[float, float, float, float]
Vec2 = Tuple[float, float]


@dataclass(frozen=True)
class AlgorithmMeta:
    algorithm_version: str
    config_hash: str
    tactic_hash: str
    seed: int = 0


@dataclass(frozen=True)
class DeviceMeta:
    hmd: str = ""
    fps: Optional[int] = None
    refresh_hz: Optional[float] = None


@dataclass(frozen=True)
class SessionMeta:
    session_id: str
    user_id: str
    tactic_id: str
    player_id: str
    start_ms: int
    end_ms: Optional[int] = None

    device: DeviceMeta = field(default_factory=DeviceMeta)
    locomotion_mode: str = ""
    comfort_settings: Dict[str, Any] = field(default_factory=dict)

    algorithm: AlgorithmMeta = field(default_factory=lambda: AlgorithmMeta("", "", "", 0))


@dataclass(frozen=True)
class TelemetrySample:
    """
    One high-frequency sample (telemetry.jsonl).
    """
    t_ms: int
    frame_idx: int
    playback_state: PlaybackState
    playback_speed: float

    frame_progress: Optional[float] = None  # 0..1 if interpolating

    head_pos_xyz: Vec3 = (0.0, 0.0, 0.0)
    head_rot_quat: Quat = (0.0, 0.0, 0.0, 1.0)

    gaze_origin_xyz: Vec3 = (0.0, 0.0, 0.0)
    gaze_dir_xyz: Vec3 = (0.0, 0.0, 1.0)

    hit_type: HitType = "none"
    hit_id: Optional[str] = None

    hit_point_xyz: Optional[Vec3] = None
    fov_player_ids: Optional[List[str]] = None


@dataclass(frozen=True)
class EventRecord:
    """
    One discrete interaction event (events.jsonl).
    """
    t_ms: int
    frame_idx: int
    type: str
    payload: Dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class Candidate:
    """
    One candidate attention target for a (player, frame).
    """
    candidate_id: str
    target_type: TargetType
    anchor_xy: Vec2

    target_player_id: Optional[str] = None
    baseline_score: float = 0.0
    features: Dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class CandidateSetRecord:
    """
    One candidate-set snapshot (candidates.jsonl).
    """
    frame_idx: int
    player_id: str
    candidates: List[Candidate]
    chosen_candidate_id: str


@dataclass(frozen=True)
class DerivedReward:
    """
    One derived reward for offline RL or policy evaluation.
    Produced by Python from logs (not emitted by VR).
    """
    session_id: str
    tactic_id: str
    player_id: str
    frame_idx: int
    chosen_candidate_id: str
    reward: float
    components: Dict[str, float] = field(default_factory=dict)


@dataclass(frozen=True)
class PreferencePair:
    """
    One preference pair for ranker training:
      preferred_candidate_id ≻ other_candidate_id
    """
    session_id: str
    tactic_id: str
    player_id: str
    frame_idx: int
    preferred_candidate_id: str
    other_candidate_id: str
    weight: float = 1.0
    evidence: Dict[str, Any] = field(default_factory=dict)
