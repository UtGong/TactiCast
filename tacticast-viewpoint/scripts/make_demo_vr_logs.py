from __future__ import annotations

import argparse
import json
import math
import random
import time
import uuid
from pathlib import Path
from typing import Dict, List, Tuple


def _write_json(path: Path, obj) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)


def _write_jsonl(path: Path, rows: List[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")


def _unit(v: Tuple[float, float, float]) -> Tuple[float, float, float]:
    x, y, z = v
    n = math.sqrt(x * x + y * y + z * z)
    if n < 1e-9:
        return (0.0, 0.0, 1.0)
    return (x / n, y / n, z / n)


def _quat_from_yaw_pitch(yaw: float, pitch: float) -> Tuple[float, float, float, float]:
    # Minimal yaw/pitch quat (roll=0), sufficient for demo logs
    cy = math.cos(yaw * 0.5)
    sy = math.sin(yaw * 0.5)
    cp = math.cos(pitch * 0.5)
    sp = math.sin(pitch * 0.5)

    # yaw around y, pitch around x (approx)
    w = cy * cp
    x = sp * cy
    y = sy * cp
    z = -sy * sp
    return (x, y, z, w)


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Generate synthetic VR logs for derive.py testing.")
    p.add_argument("--out_dir", required=True, help="Directory to write the session logs into.")
    p.add_argument("--tactic_id", default="tac_1768583550841")
    p.add_argument("--player_id", default="A-7")
    p.add_argument("--n_frames", type=int, default=5)
    p.add_argument("--sample_hz", type=int, default=20)
    p.add_argument("--seed", type=int, default=7)
    return p.parse_args()


def main() -> None:
    args = _parse_args()
    random.seed(args.seed)

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    session_id = f"demo_{uuid.uuid4().hex[:8]}"
    user_id = "user_demo_hash"
    now_ms = int(time.time() * 1000)

    # -------------------------
    # session_meta.json
    # -------------------------
    meta = {
        "session_id": session_id,
        "user_id": user_id,
        "tactic_id": args.tactic_id,
        "player_id": args.player_id,
        "start_ms": now_ms,
        "end_ms": now_ms + 15_000,
        "device": {"hmd": "DemoHMD", "fps": 90, "refresh_hz": 90},
        "locomotion_mode": "fixed",
        "comfort_settings": {"snap_turn": True},
        "algorithm": {
            "algorithm_version": "tacticast_viewpoint_v2_baseline",
            "config_hash": "demo_cfg_hash",
            "tactic_hash": "demo_tactic_hash",
            "seed": args.seed,
        },
    }
    _write_json(out_dir / "session_meta.json", meta)

    # -------------------------
    # candidates.jsonl
    # One record per frame for this player
    # We intentionally craft candidates so dwell/manual selection can form prefs.
    # -------------------------
    # Candidate IDs stable per frame record, not across frames (OK for training).
    candidates_rows: List[dict] = []
    for fi in range(args.n_frames):
        # We fabricate 4 candidates:
        # c0: BALL
        # c1: PLAYER(B-6)
        # c2: PLAYER(A-9)
        # c3: GOAL
        cands = [
            {"candidate_id": "c0", "target_type": "ball", "target_player_id": None, "anchor_xy": [50 + fi * 2, 34], "baseline_score": 1.5, "features": {"kind": "BALL_NEARBY"}},
            {"candidate_id": "c1", "target_type": "player", "target_player_id": "B-6", "anchor_xy": [75, 30 + fi], "baseline_score": 1.2, "features": {"kind": "OPP_PRESSURE"}},
            {"candidate_id": "c2", "target_type": "player", "target_player_id": "A-9", "anchor_xy": [70, 36], "baseline_score": 0.9, "features": {"kind": "TEAM_SUPPORT"}},
            {"candidate_id": "c3", "target_type": "goal", "target_player_id": None, "anchor_xy": [105, 34], "baseline_score": 0.6, "features": {"kind": "GOAL"}},
        ]

        # Chosen by baseline: vary per frame to test penalties/prefs
        chosen = "c0" if fi in {0, 2} else ("c1" if fi == 1 else ("c2" if fi == 3 else "c0"))

        candidates_rows.append(
            {"frame_idx": fi, "player_id": args.player_id, "candidates": cands, "chosen_candidate_id": chosen}
        )

    _write_jsonl(out_dir / "candidates.jsonl", candidates_rows)

    # -------------------------
    # events.jsonl
    # Include manual select (frame 0), replay (frame 1), hint (frame 2)
    # -------------------------
    events_rows: List[dict] = [
        {
            "t_ms": now_ms + 1200,
            "frame_idx": 0,
            "type": "manual_target_select",
            "payload": {"target_type": "ball", "target_id": "ball", "anchor_xy": [50, 34]},
        },
        {
            "t_ms": now_ms + 4200,
            "frame_idx": 1,
            "type": "replay_segment",
            "payload": {"from_frame": 1, "to_frame": 2, "count": 1},
        },
        {
            "t_ms": now_ms + 6800,
            "frame_idx": 2,
            "type": "focus_hint_request",
            "payload": {"reason": "confused"},
        },
    ]
    _write_jsonl(out_dir / "events.jsonl", events_rows)

    # -------------------------
    # telemetry.jsonl
    # Simulate gaze hits per frame:
    # frame 0: mostly ball (supports chosen c0)
    # frame 1: mostly B-6 (supports candidate c1 but replay penalty exists)
    # frame 2: mostly none + some A-9 (hint penalty, creates preference vs chosen)
    # frame 3: mostly A-9 (supports c2)
    # frame 4: mostly ball
    # -------------------------
    sample_hz = max(1, int(args.sample_hz))
    dt_ms = int(1000 / sample_hz)

    telemetry_rows: List[dict] = []
    t = now_ms

    # Map from frame -> hit_id distribution (hit_type will be inferred)
    frame_hit_plan: Dict[int, List[str]] = {
        0: ["ball"] * 20 + ["B-6"] * 2 + ["none"] * 2,
        1: ["B-6"] * 18 + ["ball"] * 3 + ["none"] * 3,
        2: ["A-9"] * 10 + ["none"] * 14,
        3: ["A-9"] * 20 + ["ball"] * 2 + ["none"] * 2,
        4: ["ball"] * 18 + ["none"] * 6,
    }

    for fi in range(args.n_frames):
        plan = frame_hit_plan.get(fi, ["none"] * 20)
        # Use 1 second per frame for demo
        n_samples = sample_hz * 1

        for si in range(n_samples):
            hit_id = random.choice(plan)
            if hit_id == "ball":
                hit_type = "ball"
            elif hit_id == "none":
                hit_type = "none"
                hit_id = None
            else:
                hit_type = "player"

            yaw = 0.2 * math.sin(0.1 * si)
            pitch = 0.05 * math.cos(0.08 * si)
            q = _quat_from_yaw_pitch(yaw, pitch)

            # Gaze dir roughly forward + some noise
            gaze_dir = _unit((0.0 + random.uniform(-0.05, 0.05), 0.0 + random.uniform(-0.05, 0.05), 1.0))

            telemetry_rows.append(
                {
                    "t_ms": t,
                    "frame_idx": fi,
                    "playback_state": "playing",
                    "playback_speed": 1.0,
                    "frame_progress": None,
                    "head_pos_xyz": [0.0, 1.6, 0.0],
                    "head_rot_quat": list(q),
                    "gaze_origin_xyz": [0.0, 1.6, 0.0],
                    "gaze_dir_xyz": list(gaze_dir),
                    "hit_type": hit_type,
                    "hit_id": hit_id,
                    "hit_point_xyz": None,
                    "fov_player_ids": None,
                }
            )
            t += dt_ms

    _write_jsonl(out_dir / "telemetry.jsonl", telemetry_rows)

    print(f"Demo VR logs written to: {out_dir.resolve()}")
    print("Files:")
    print("  - session_meta.json")
    print("  - telemetry.jsonl")
    print("  - events.jsonl")
    print("  - candidates.jsonl")


if __name__ == "__main__":
    main()
