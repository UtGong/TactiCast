import tempfile
from pathlib import Path

from tacticast_viewpoint.learning.prefs.derive import (
    load_session_meta,
    load_telemetry,
    load_events,
    load_candidates,
    derive_rewards_and_prefs,
)


def _write_json(path: Path, obj) -> None:
    import json
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)


def _write_jsonl(path: Path, rows) -> None:
    import json
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")


def test_derive_demo_logs_produces_rewards_and_prefs():
    """
    Minimal synthetic VR session:
      - candidates exist for each frame
      - telemetry produces dwell evidence
      - events include manual select / replay / hint
    Expect:
      - rewards non-empty
      - prefs non-empty
    """
    with tempfile.TemporaryDirectory() as tmp:
        session_dir = Path(tmp) / "demo_vr_session"
        session_dir.mkdir(parents=True, exist_ok=True)

        # --- session_meta.json ---
        meta = {
            "session_id": "demo_sess_001",
            "user_id": "user_demo_hash",
            "tactic_id": "tac_demo",
            "player_id": "A-7",
            "start_ms": 1000,
            "end_ms": 9000,
            "device": {"hmd": "DemoHMD", "fps": 90, "refresh_hz": 90},
            "locomotion_mode": "fixed",
            "comfort_settings": {"snap_turn": True},
            "algorithm": {
                "algorithm_version": "tacticast_viewpoint_v2_baseline",
                "config_hash": "demo_cfg_hash",
                "tactic_hash": "demo_tactic_hash",
                "seed": 7,
            },
        }
        _write_json(session_dir / "session_meta.json", meta)

        # --- candidates.jsonl (5 frames) ---
        cand_rows = []
        for fi in range(5):
            cand_rows.append(
                {
                    "frame_idx": fi,
                    "player_id": "A-7",
                    "candidates": [
                        {
                            "candidate_id": "c0",
                            "target_type": "ball",
                            "target_player_id": None,
                            "anchor_xy": [50 + fi, 34],
                            "baseline_score": 1.5,
                            "features": {"kind": "BALL"},
                        },
                        {
                            "candidate_id": "c1",
                            "target_type": "player",
                            "target_player_id": "B-6",
                            "anchor_xy": [75, 30 + fi],
                            "baseline_score": 1.2,
                            "features": {"kind": "OPP"},
                        },
                        {
                            "candidate_id": "c2",
                            "target_type": "player",
                            "target_player_id": "A-9",
                            "anchor_xy": [70, 36],
                            "baseline_score": 0.9,
                            "features": {"kind": "TEAM"},
                        },
                    ],
                    "chosen_candidate_id": "c0" if fi in {0, 2, 4} else "c1",
                }
            )
        _write_jsonl(session_dir / "candidates.jsonl", cand_rows)

        # --- events.jsonl ---
        events = [
            {
                "t_ms": 1200,
                "frame_idx": 0,
                "type": "manual_target_select",
                "payload": {"target_type": "ball", "target_id": "ball"},
            },
            {
                "t_ms": 4200,
                "frame_idx": 1,
                "type": "replay_segment",
                "payload": {"from_frame": 1, "to_frame": 2, "count": 1},
            },
            {
                "t_ms": 6800,
                "frame_idx": 2,
                "type": "focus_hint_request",
                "payload": {"reason": "confused"},
            },
        ]
        _write_jsonl(session_dir / "events.jsonl", events)

        # --- telemetry.jsonl ---
        # We emulate dwell by repeating hit_id in many samples. derive.py uses sample_dt_ms=50.
        telemetry = []
        t = 1000
        # frame 0: mostly ball
        for _ in range(20):
            telemetry.append(
                {
                    "t_ms": t,
                    "frame_idx": 0,
                    "playback_state": "playing",
                    "playback_speed": 1.0,
                    "frame_progress": None,
                    "head_pos_xyz": [0.0, 1.6, 0.0],
                    "head_rot_quat": [0.0, 0.0, 0.0, 1.0],
                    "gaze_origin_xyz": [0.0, 1.6, 0.0],
                    "gaze_dir_xyz": [0.0, 0.0, 1.0],
                    "hit_type": "ball",
                    "hit_id": "ball",
                    "hit_point_xyz": None,
                    "fov_player_ids": None,
                }
            )
            t += 50
        # frame 1: mostly B-6
        for _ in range(20):
            telemetry.append(
                {
                    "t_ms": t,
                    "frame_idx": 1,
                    "playback_state": "playing",
                    "playback_speed": 1.0,
                    "frame_progress": None,
                    "head_pos_xyz": [0.0, 1.6, 0.0],
                    "head_rot_quat": [0.0, 0.0, 0.0, 1.0],
                    "gaze_origin_xyz": [0.0, 1.6, 0.0],
                    "gaze_dir_xyz": [0.0, 0.0, 1.0],
                    "hit_type": "player",
                    "hit_id": "B-6",
                    "hit_point_xyz": None,
                    "fov_player_ids": None,
                }
            )
            t += 50
        # frame 2: mixed A-9 and none
        for k in range(20):
            telemetry.append(
                {
                    "t_ms": t,
                    "frame_idx": 2,
                    "playback_state": "playing",
                    "playback_speed": 1.0,
                    "frame_progress": None,
                    "head_pos_xyz": [0.0, 1.6, 0.0],
                    "head_rot_quat": [0.0, 0.0, 0.0, 1.0],
                    "gaze_origin_xyz": [0.0, 1.6, 0.0],
                    "gaze_dir_xyz": [0.0, 0.0, 1.0],
                    "hit_type": "player" if k < 8 else "none",
                    "hit_id": "A-9" if k < 8 else None,
                    "hit_point_xyz": None,
                    "fov_player_ids": None,
                }
            )
            t += 50

        _write_jsonl(session_dir / "telemetry.jsonl", telemetry)

        # --- run derive ---
        meta_obj = load_session_meta(session_dir / "session_meta.json")
        telemetry_obj = load_telemetry(session_dir / "telemetry.jsonl")
        events_obj = load_events(session_dir / "events.jsonl")
        candidates_obj = load_candidates(session_dir / "candidates.jsonl")

        rewards, prefs = derive_rewards_and_prefs(meta_obj, telemetry_obj, events_obj, candidates_obj)

        assert len(rewards) > 0
        assert len(prefs) > 0

        # sanity: should include at least one manual select preference or dwell preference
        assert any(p.evidence.get("type") in {"manual_target_select", "dwell", "dwell_margin"} for p in prefs)
