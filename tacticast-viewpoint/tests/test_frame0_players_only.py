import os
import json

from tacticast_viewpoint import AlgoConfig, recommend_player_focus
from tacticast_viewpoint.core import select_tactic


def _load_root_from_env():
    path = os.environ.get("TACTIC_JSON")
    assert path is not None, "Set env var TACTIC_JSON to tactic JSON path"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def test_only_frame0_players_are_used():
    root = _load_root_from_env()

    # Match your viz command / common usage
    tactic_id = os.environ.get("TACTIC_ID")  # optional
    tactic = select_tactic(root, tactic_id=tactic_id, tactic_index=0)

    frames = tactic["frames"]
    frame0_players = set(frames[0]["player_pos"].keys())

    cfg = AlgoConfig(top_k=1)
    recs = recommend_player_focus(root, cfg, tactic_id=tactic_id, tactic_index=0)

    assert set(recs.keys()) == frame0_players

    n_frames = len(frames)
    for pid, lst in recs.items():
        assert len(lst) == n_frames
        for r in lst:
            assert r.player_id == pid
