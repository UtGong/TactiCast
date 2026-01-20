from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import matplotlib.pyplot as plt

from tacticast_viewpoint import AlgoConfig, recommend_player_focus
from tacticast_viewpoint.core import select_tactic, parse_meta
from tacticast_viewpoint.io import load_json, ensure_tactic_schema
from tacticast_viewpoint.types import FocusTarget, PlayerFocusRecommendation


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Visualize player-centric focus recommendations.")
    p.add_argument("--tactic", required=True, help="Path to tactic JSON (single tactic or tactic DB list).")
    p.add_argument("--tactic_id", default=None, help="Select tactic by meta.tactic_id if JSON is a list.")
    p.add_argument("--tactic_index", type=int, default=0, help="Select tactic by index if JSON is a list.")
    p.add_argument("--out_dir", required=True, help="Output directory for per-frame PNGs.")
    p.add_argument("--top_k", type=int, default=1, help="How many recommendations to compute (default 1).")
    p.add_argument("--mode", choices=["primary", "topk"], default="primary",
                  help="primary = draw exactly one arrow per player; topk = draw all top_k arrows.")
    p.add_argument("--show_ids", action="store_true", help="Draw player IDs near player dots.")
    return p.parse_args()


def _draw_pitch(ax, pitch_len: float, pitch_w: float) -> None:
    # Pitch outline
    ax.plot([0, pitch_len, pitch_len, 0, 0], [0, 0, pitch_w, pitch_w, 0], linewidth=1)

    # Halfway line
    ax.plot([pitch_len / 2, pitch_len / 2], [0, pitch_w], linewidth=1)

    # Center circle (approx)
    # keep it simple: small circle
    cx, cy = pitch_len / 2, pitch_w / 2
    r = 9.15
    theta = [i * 0.1 for i in range(0, 63)]
    xs = [cx + r * __import__("math").cos(t) for t in theta]
    ys = [cy + r * __import__("math").sin(t) for t in theta]
    ax.plot(xs, ys, linewidth=1)

    ax.set_aspect("equal", adjustable="box")
    ax.set_xlim(-2, pitch_len + 2)
    ax.set_ylim(-2, pitch_w + 2)
    ax.invert_yaxis()  # optional: match many soccer viz conventions
    ax.set_xlabel("x")
    ax.set_ylabel("y")


def _focus_to_arrow(player_xy: Tuple[float, float], focus: FocusTarget) -> Tuple[List[float], List[float]]:
    px, py = player_xy
    tx, ty = focus.anchor
    return [px, tx], [py, ty]


def main() -> None:
    args = _parse_args()
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    root = load_json(args.tactic)
    tactic = select_tactic(root, tactic_id=args.tactic_id, tactic_index=args.tactic_index)
    ensure_tactic_schema(tactic)

    meta = parse_meta(tactic["meta"])
    frames = tactic["frames"]
    if not frames:
        raise ValueError("No frames in tactic.")

    # Valid players are defined by frame 0 (contract)
    frame0_player_ids = list((frames[0].get("player_pos") or {}).keys())
    frame0_set = set(frame0_player_ids)

    cfg = AlgoConfig(top_k=int(args.top_k))

    # Run model
    recs = recommend_player_focus(
        root,
        cfg,
        tactic_id=args.tactic_id,
        tactic_index=args.tactic_index,
    )

    # Determine frame count from any player in output
    any_pid = frame0_player_ids[0] if frame0_player_ids else None
    if any_pid is None or any_pid not in recs:
        raise ValueError("No valid frame0 players found in recommendations output.")
    n_frames = len(recs[any_pid])

    # Render each frame
    for i in range(n_frames):
        fig, ax = plt.subplots(figsize=(10, 6))
        _draw_pitch(ax, meta.pitch.length, meta.pitch.width)

        # Plot all frame0 players positions for THIS frame i
        f = frames[i]
        player_pos = f.get("player_pos") or {}

        # Draw player points (frame0 only)
        for pid in frame0_player_ids:
            xy = player_pos.get(pid)
            if xy is None:
                continue
            x, y = float(xy[0]), float(xy[1])

            team = meta.players.get(pid).team if pid in meta.players else "A"
            marker = "o" if team == "A" else "s"
            ax.scatter([x], [y], marker=marker, s=60)

            if args.show_ids:
                ax.text(x + 0.5, y + 0.5, pid, fontsize=8)

        # Ball
        ball = f.get("ball") or {}
        bx, by = ball.get("x"), ball.get("y")
        if bx is not None and by is not None:
            ax.scatter([float(bx)], [float(by)], marker="*", s=120)
            ax.text(float(bx) + 0.5, float(by) + 0.5, "ball", fontsize=8)

        # Draw arrows: exactly one per player in primary mode
        for pid in frame0_player_ids:
            if pid not in recs:
                continue

            # Use position from the tactic frame to anchor arrows (consistent with visualization)
            xy = player_pos.get(pid)
            if xy is None:
                continue
            px, py = float(xy[0]), float(xy[1])

            r: PlayerFocusRecommendation = recs[pid][i]

            if args.mode == "primary":
                focus = r.primary
                xs, ys = _focus_to_arrow((px, py), focus)
                ax.plot(xs, ys, linewidth=1.25)
                ax.scatter([focus.anchor[0]], [focus.anchor[1]], marker="x", s=55)
            else:
                # topk mode: draw each candidate in r.top_k (may be >1)
                for j, ev in enumerate(r.top_k):
                    focus = ev.focus
                    xs, ys = _focus_to_arrow((px, py), focus)
                    ax.plot(xs, ys, linewidth=1.0)
                    ax.scatter([focus.anchor[0]], [focus.anchor[1]], marker="x", s=45)

        title = f"{meta.title} | frame {i} | mode={args.mode} | top_k={cfg.top_k}"
        ax.set_title(title)

        out_path = out_dir / f"frame_{i:03d}.png"
        fig.tight_layout()
        fig.savefig(out_path, dpi=160)
        plt.close(fig)

    print(f"Saved {n_frames} frames to: {out_dir.resolve()}")


if __name__ == "__main__":
    main()
