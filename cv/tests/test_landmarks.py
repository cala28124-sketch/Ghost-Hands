import numpy as np

from ghosthands.landmarks import Hand, resolve_sides


def hand(side, score):
    return Hand(side, score, np.zeros((21, 3)), np.zeros((21, 3)))


def test_two_different_sides_are_unchanged_and_sorted():
    result = resolve_sides([hand("right", 0.9), hand("left", 0.8)])
    assert [(h.side, h.score) for h in result] == [("left", 0.8), ("right", 0.9)]


def test_same_side_collision_flips_the_less_confident_hand():
    result = resolve_sides([hand("right", 0.55), hand("right", 0.91)])
    assert [(h.side, h.score) for h in result] == [("left", 0.45), ("right", 0.91)]


def test_a_single_unsure_hand_is_kept():
    # A low score means "unsure which hand", not "bad landmarks", so the hand stays.
    assert [h.side for h in resolve_sides([hand("left", 0.3)])] == ["left"]


def test_no_hands():
    assert resolve_sides([]) == []
