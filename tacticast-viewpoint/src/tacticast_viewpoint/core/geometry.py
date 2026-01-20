from __future__ import annotations

from typing import Tuple

from tacticast_viewpoint.types import Vec2, Pitch


# -----------------------
# Basic geometry
# -----------------------

def dist(a: Vec2, b: Vec2) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    return (dx * dx + dy * dy) ** 0.5


def norm(v: Vec2) -> float:
    return (v[0] * v[0] + v[1] * v[1]) ** 0.5


def sub(a: Vec2, b: Vec2) -> Vec2:
    return (a[0] - b[0], a[1] - b[1])


def dot(a: Vec2, b: Vec2) -> float:
    return a[0] * b[0] + a[1] * b[1]


def unit(v: Vec2) -> Vec2:
    n = norm(v)
    if n < 1e-6:
        return (0.0, 0.0)
    return (v[0] / n, v[1] / n)


# -----------------------
# Soccer-specific helpers
# -----------------------

def attacking_goal_center(pitch: Pitch, attack_direction: int) -> Vec2:
    """
    Return opponent goal center assuming:
      attack_direction = +1 → attacking +x (right)
      attack_direction = -1 → attacking -x (left)
    """
    x = pitch.length if attack_direction > 0 else 0.0
    y = pitch.width * 0.5
    return (x, y)


def is_ahead(a: Vec2, b: Vec2, attack_direction: int) -> bool:
    """
    True if point a is ahead of b along attack direction.
    """
    return (a[0] - b[0]) * attack_direction > 0.0


def in_forward_cone(
    origin: Vec2,
    target: Vec2,
    attack_direction: int,
    cos_threshold: float = 0.5,
) -> bool:
    """
    Check if target lies roughly in front of origin
    with respect to attack direction.

    cos_threshold:
      1.0 = exactly forward
      0.0 = 90 degrees
    """
    forward = (float(attack_direction), 0.0)
    v = sub(target, origin)
    v_hat = unit(v)
    return dot(v_hat, forward) >= cos_threshold
