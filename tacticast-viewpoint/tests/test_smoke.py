import os
import json

from tacticast_viewpoint import AlgoConfig, recommend_player_focus


def _load_tactic_from_env():
    path = os.environ.get("TACTIC_JSON")
    assert path is not None, "Set env var TACTIC_JSON to tactic JSON path"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def test_recommend_player_focus_smoke():
    tactic = _load_tactic_from_env()
    cfg = AlgoConfig(top_k=1)

    recs = recommend_player_focus(tactic, cfg)

    # basic sanity
    assert isinstance(recs, dict)
    assert len(recs) > 0

    # pick any player
    pid, lst = next(iter(recs.items()))
    assert len(lst) > 0

    r0 = lst[0]
    assert r0.player_id == pid
    assert r0.primary is not None
    assert isinstance(r0.primary.anchor, tuple)
