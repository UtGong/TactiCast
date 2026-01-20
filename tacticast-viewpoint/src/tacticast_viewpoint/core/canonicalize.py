from __future__ import annotations

from typing import Dict, List, Tuple

from tacticast_viewpoint.types import (
    Frame,
    RawFrame,
    RawBall,
    TacticMeta,
    Pitch,
    TeamMeta,
    PlayerMeta,
    Vec2,
)


# -----------------------
# Meta parsing
# -----------------------

def parse_meta(meta: Dict) -> TacticMeta:
    pitch = Pitch(
        length=float(meta["pitch"]["length"]),
        width=float(meta["pitch"]["width"]),
    )

    teams = {
        tid: TeamMeta(**tmeta)
        for tid, tmeta in meta["teams"].items()
    }

    players = {
        p["id"]: PlayerMeta(
            id=p["id"],
            team=p["team"],
            label=p.get("label", ""),
            role=p.get("role", ""),
        )
        for p in meta["players"]
    }

    return TacticMeta(
        tactic_id=meta.get("tactic_id", ""),
        title=meta.get("title", ""),
        pitch=pitch,
        teams=teams,
        players=players,
        last_modified=meta.get("last_modified"),
    )


# -----------------------
# Frame canonicalization
# -----------------------

def canonicalize_frames(
    raw_frames: List[RawFrame],
) -> Tuple[List[Frame], List[str]]:
    """
    Canonicalize frames.

    Enforced invariants:
    - Player set P is defined ONLY by players present in frame 0
    - All frames contain exactly players in P (others dropped)
    - Missing positions are forward-filled from previous frames
    - Ball position is forward-filled if missing

    Returns:
      frames: List[Frame]
      valid_player_ids: ordered list of player IDs (from frame 0)
    """
    if not raw_frames:
        raise ValueError("No frames provided.")

    # --- determine valid players from frame 0
    frame0 = raw_frames[0]
    valid_player_ids = list(frame0.player_pos.keys())
    valid_set = set(valid_player_ids)

    frames: List[Frame] = []

    last_positions: Dict[str, Vec2] = {}
    last_ball_pos: Vec2 | None = None
    last_ball_owner: str | None = None

    for idx, rf in enumerate(raw_frames):
        players: Dict[str, Vec2] = {}

        # --- players
        for pid in valid_player_ids:
            if pid in rf.player_pos:
                pos = tuple(rf.player_pos[pid])
                players[pid] = pos
                last_positions[pid] = pos
            else:
                # forward-fill
                if pid not in last_positions:
                    raise ValueError(
                        f"Player {pid} missing at frame {idx} and no previous position to fill."
                    )
                players[pid] = last_positions[pid]

        # --- ball
        ball = rf.ball
        if ball.x is not None and ball.y is not None:
            last_ball_pos = (float(ball.x), float(ball.y))
            last_ball_owner = ball.owner_id
        elif last_ball_pos is None:
            raise ValueError(f"Ball position missing at frame {idx} and cannot be inferred.")

        frames.append(
            Frame(
                frame_idx=idx,
                players=players,
                ball_pos=last_ball_pos,
                ball_owner_id=last_ball_owner,
                note=rf.note,
            )
        )

    return frames, valid_player_ids
