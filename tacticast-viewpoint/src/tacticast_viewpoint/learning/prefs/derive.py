from __future__ import annotations

import argparse
import json
from dataclasses import asdict
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple

from tacticast_viewpoint.learning.prefs.schema import (
    Candidate,
    CandidateSetRecord,
    DerivedReward,
    EventRecord,
    PreferencePair,
    SessionMeta,
    TelemetrySample,
)


# -------------------------
# IO helpers
# -------------------------

def _load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _iter_jsonl(path: Path) -> Iterable[Dict[str, Any]]:
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            yield json.loads(line)


def _write_jsonl(path: Path, rows: Iterable[Dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")


# -------------------------
# Parsing into schema types
# -------------------------

def load_session_meta(meta_path: Path) -> SessionMeta:
    obj = _load_json(meta_path)
    # minimal parsing; trust schema.py types downstream
    return SessionMeta(**obj)


def load_telemetry(telemetry_path: Path) -> List[TelemetrySample]:
    out: List[TelemetrySample] = []
    for d in _iter_jsonl(telemetry_path):
        out.append(TelemetrySample(**d))
    out.sort(key=lambda s: (s.frame_idx, s.t_ms))
    return out


def load_events(events_path: Path) -> List[EventRecord]:
    out: List[EventRecord] = []
    for d in _iter_jsonl(events_path):
        out.append(EventRecord(**d))
    out.sort(key=lambda e: e.t_ms)
    return out


def load_candidates(candidates_path: Path) -> Dict[Tuple[str, int], CandidateSetRecord]:
    """
    Keyed by (player_id, frame_idx)
    """
    out: Dict[Tuple[str, int], CandidateSetRecord] = {}
    for d in _iter_jsonl(candidates_path):
        cands = [Candidate(**c) for c in d["candidates"]]
        rec = CandidateSetRecord(
            frame_idx=int(d["frame_idx"]),
            player_id=str(d["player_id"]),
            candidates=cands,
            chosen_candidate_id=str(d["chosen_candidate_id"]),
        )
        out[(rec.player_id, rec.frame_idx)] = rec
    return out


# -------------------------
# Fixation approximation
# -------------------------

def compute_hit_dwell_by_frame(
    telemetry: List[TelemetrySample],
    *,
    sample_dt_ms: int = 50,
) -> Dict[int, Dict[str, float]]:
    """
    Approximate dwell time (ms) on each hit_id per frame_idx.
    Works even without eye tracking if gaze_dir/hit_id is head-forward based.

    We assume telemetry is frequent; we convert samples to dwell by counting samples * sample_dt_ms.
    If dt is irregular, this is an approximation (good enough to bootstrap).
    """
    dwell: Dict[int, Dict[str, float]] = {}
    for s in telemetry:
        fid = int(s.frame_idx)
        hit = s.hit_id if s.hit_id is not None else "none"
        dwell.setdefault(fid, {})
        dwell[fid][hit] = dwell[fid].get(hit, 0.0) + float(sample_dt_ms)
    return dwell


def best_attention_id_for_frame(dwell_by_frame: Dict[int, Dict[str, float]], frame_idx: int) -> Optional[str]:
    m = dwell_by_frame.get(frame_idx, {})
    # ignore "none" and "ui" if present as ids
    filtered = {k: v for k, v in m.items() if k not in {"none", None}}
    if not filtered:
        return None
    return max(filtered.items(), key=lambda kv: kv[1])[0]


# -------------------------
# Reward and preference derivation
# -------------------------

def derive_rewards_and_prefs(
    meta: SessionMeta,
    telemetry: List[TelemetrySample],
    events: List[EventRecord],
    candidates: Optional[Dict[Tuple[str, int], CandidateSetRecord]],
    *,
    dwell_window_ms: int = 1500,
    dwell_reward_scale: float = 1.0,
    manual_select_reward: float = 2.0,
    hint_penalty: float = 1.0,
    replay_penalty: float = 0.5,
) -> Tuple[List[DerivedReward], List[PreferencePair]]:
    """
    Produces:
      - one reward per (player_id, frame_idx) where a chosen candidate exists
      - preference pairs where evidence supports preferred candidate

    Requirements:
      - candidates must be provided for meaningful supervised / RL-ready outputs.
        If candidates is None, we will still emit weak rewards based on attention, but
        cannot form candidate-level prefs.
    """
    player_id = meta.player_id

    # Build quick event index by frame
    events_by_frame: Dict[int, List[EventRecord]] = {}
    for e in events:
        events_by_frame.setdefault(int(e.frame_idx), []).append(e)

    # Attention dwell by frame (proxy)
    dwell_by_frame = compute_hit_dwell_by_frame(telemetry)

    rewards: List[DerivedReward] = []
    prefs: List[PreferencePair] = []

    if candidates is None:
        # Weak-only mode: no action space, so we can only export aggregate attention metrics.
        # Still produce "rewards" as frame-level engagement score.
        for frame_idx, m in sorted(dwell_by_frame.items()):
            best = best_attention_id_for_frame(dwell_by_frame, frame_idx)
            engaged_ms = sum(v for k, v in m.items() if k not in {"none"})
            r = float(engaged_ms / max(dwell_window_ms, 1))
            rewards.append(
                DerivedReward(
                    session_id=meta.session_id,
                    tactic_id=meta.tactic_id,
                    player_id=player_id,
                    frame_idx=int(frame_idx),
                    chosen_candidate_id="__none__",
                    reward=r,
                    components={"engaged_ms": float(engaged_ms)},
                )
            )
        return rewards, prefs

    # Candidate-based mode
    # Iterate candidate records for this player only
    player_records = [(k, v) for k, v in candidates.items() if k[0] == player_id]
    player_records.sort(key=lambda kv: kv[0][1])  # by frame_idx

    for (pid, frame_idx), rec in player_records:
        # chosen action
        chosen_id = rec.chosen_candidate_id

        # evidence
        frame_events = events_by_frame.get(frame_idx, [])
        dwell_map = dwell_by_frame.get(frame_idx, {})
        best_hit = best_attention_id_for_frame(dwell_by_frame, frame_idx)

        # component rewards
        comp: Dict[str, float] = {}
        total = 0.0

        # 1) Manual target select: if user explicitly selects something, treat it as strong positive
        manual_selected_candidate = _match_manual_select_to_candidate(rec, frame_events)
        if manual_selected_candidate is not None:
            # reward if chosen matches manual; else negative (optional)
            if manual_selected_candidate == chosen_id:
                total += manual_select_reward
                comp["manual_select_match"] = manual_select_reward
            else:
                total -= (manual_select_reward * 0.5)
                comp["manual_select_mismatch"] = -(manual_select_reward * 0.5)

                # also create preference: manual-selected ≻ chosen
                prefs.append(
                    PreferencePair(
                        session_id=meta.session_id,
                        tactic_id=meta.tactic_id,
                        player_id=pid,
                        frame_idx=frame_idx,
                        preferred_candidate_id=manual_selected_candidate,
                        other_candidate_id=chosen_id,
                        weight=2.0,
                        evidence={"type": "manual_target_select"},
                    )
                )

        # 2) Dwell/fixation proxy: if attention spent on a candidate target, reward it
        # We map dwell to candidate targets by hit_id:
        # - hit "ball" aligns with candidates whose target_type=ball
        # - hit player_id aligns with candidates target_player_id
        dwell_bonus, pref_pairs = _derive_dwell_bonus_and_prefs(
            meta=meta,
            rec=rec,
            frame_idx=frame_idx,
            dwell_map=dwell_map,
            dwell_window_ms=dwell_window_ms,
            scale=dwell_reward_scale,
        )
        if dwell_bonus != 0.0:
            total += dwell_bonus
            comp["dwell_bonus"] = dwell_bonus
        prefs.extend(pref_pairs)

        # 3) Hint request penalty (confusion)
        if any(e.type == "focus_hint_request" for e in frame_events):
            total -= hint_penalty
            comp["hint_penalty"] = -hint_penalty

        # 4) Replay penalty (confusion / unmet understanding)
        if any(e.type == "replay_segment" for e in frame_events):
            total -= replay_penalty
            comp["replay_penalty"] = -replay_penalty

        # Save reward
        rewards.append(
            DerivedReward(
                session_id=meta.session_id,
                tactic_id=meta.tactic_id,
                player_id=pid,
                frame_idx=frame_idx,
                chosen_candidate_id=chosen_id,
                reward=float(total),
                components=comp,
            )
        )

    return rewards, prefs


def _match_manual_select_to_candidate(rec: CandidateSetRecord, frame_events: List[EventRecord]) -> Optional[str]:
    """
    If a manual_target_select happened at this frame, match it to a candidate_id.
    Matching logic:
      - if payload.target_type == 'ball' => match first ball candidate
      - if payload.target_type == 'player' with target_id => match candidate with target_player_id
      - else no match
    """
    selects = [e for e in frame_events if e.type == "manual_target_select"]
    if not selects:
        return None

    # pick the last selection in this frame
    e = selects[-1]
    payload = e.payload or {}
    ttype = str(payload.get("target_type", "")).lower()
    tid = payload.get("target_id")

    if ttype == "ball":
        for c in rec.candidates:
            if c.target_type == "ball":
                return c.candidate_id
        return None

    if ttype == "player" and tid is not None:
        tid = str(tid)
        for c in rec.candidates:
            if c.target_player_id == tid:
                return c.candidate_id
        return None

    return None


def _derive_dwell_bonus_and_prefs(
    meta: SessionMeta,
    rec: CandidateSetRecord,
    frame_idx: int,
    dwell_map: Dict[str, float],
    dwell_window_ms: int,
    scale: float,
) -> Tuple[float, List[PreferencePair]]:
    """
    Convert dwell into:
      - a scalar bonus for the chosen candidate if attention aligns
      - preference pairs: candidate aligned with attention ≻ others

    We compute a per-candidate "attention score" from dwell_map:
      - if candidate is ball: use dwell_map.get("ball",0)
      - if candidate is player: use dwell_map.get(target_player_id,0)
      - else 0 (zone/goal not directly hit-tested unless VR emits hit ids for zones)
    """
    # candidate attention scores
    att: Dict[str, float] = {}
    for c in rec.candidates:
        a = 0.0
        if c.target_type == "ball":
            a = float(dwell_map.get("ball", 0.0))
        elif c.target_type == "player" and c.target_player_id is not None:
            a = float(dwell_map.get(c.target_player_id, 0.0))
        att[c.candidate_id] = a

    if not att:
        return 0.0, []

    # Normalize into [0,1] by dwell_window_ms
    norm_att = {cid: min(1.0, a / max(1.0, float(dwell_window_ms))) for cid, a in att.items()}

    # reward bonus: if user attended to chosen target, reward proportionally
    chosen_att = norm_att.get(rec.chosen_candidate_id, 0.0)
    dwell_bonus = scale * chosen_att

    # preferences: best attended candidate ≻ chosen/others, if evidence strong enough
    best_cid, best_val = max(norm_att.items(), key=lambda kv: kv[1])
    prefs: List[PreferencePair] = []

    # threshold to avoid noise
    if best_val >= 0.35:
        # if best != chosen, create preference best ≻ chosen
        if best_cid != rec.chosen_candidate_id:
            prefs.append(
                PreferencePair(
                    session_id=meta.session_id,
                    tactic_id=meta.tactic_id,
                    player_id=meta.player_id,
                    frame_idx=frame_idx,
                    preferred_candidate_id=best_cid,
                    other_candidate_id=rec.chosen_candidate_id,
                    weight=1.0 + best_val,
                    evidence={"type": "dwell", "best_val": best_val},
                )
            )

        # also create best ≻ any low-attended candidate
        for cid, v in norm_att.items():
            if cid == best_cid:
                continue
            if best_val - v >= 0.35:
                prefs.append(
                    PreferencePair(
                        session_id=meta.session_id,
                        tactic_id=meta.tactic_id,
                        player_id=meta.player_id,
                        frame_idx=frame_idx,
                        preferred_candidate_id=best_cid,
                        other_candidate_id=cid,
                        weight=0.7 + (best_val - v),
                        evidence={"type": "dwell_margin", "margin": best_val - v},
                    )
                )

    return float(dwell_bonus), prefs


# -------------------------
# CLI
# -------------------------

def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Derive rewards and preferences from VR logs.")
    p.add_argument("--session_dir", required=True, help="Directory containing session_meta.json, telemetry.jsonl, events.jsonl")
    p.add_argument("--out_dir", required=True, help="Output directory")
    p.add_argument("--no_candidates", action="store_true", help="Ignore candidates.jsonl even if present")
    return p.parse_args()


def main() -> None:
    args = _parse_args()
    session_dir = Path(args.session_dir)
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    meta = load_session_meta(session_dir / "session_meta.json")
    telemetry = load_telemetry(session_dir / "telemetry.jsonl")
    events = load_events(session_dir / "events.jsonl")

    candidates: Optional[Dict[Tuple[str, int], CandidateSetRecord]] = None
    cand_path = session_dir / "candidates.jsonl"
    if (not args.no_candidates) and cand_path.exists():
        candidates = load_candidates(cand_path)

    rewards, prefs = derive_rewards_and_prefs(meta, telemetry, events, candidates)

    _write_jsonl(out_dir / "rewards.jsonl", [asdict(r) for r in rewards])
    _write_jsonl(out_dir / "prefs.jsonl", [asdict(p) for p in prefs])

    print(f"Wrote {len(rewards)} rewards to {out_dir / 'rewards.jsonl'}")
    print(f"Wrote {len(prefs)} prefs to {out_dir / 'prefs.jsonl'}")


if __name__ == "__main__":
    main()
