"""Which way the camera faced the expert, worked out from the video, so a lesson's hands are never turned around.

A phone propped up in front of the expert sees their hands from the other side: their right hand appears on the
left of the picture. Filmed from the expert's own point of view (a head or chest camera), it appears on the right.
MediaPipe tells right from left by the hand's shape, which doesn't depend on the view, so where each hand sits
in the picture gives the camera away:
  - both hands in view: is the right hand to the right of the left hand, or to its left? (Count, PaulCube: left
    in every frame, so facing.)
  - one hand: which half of the picture does it stay in? (ASL's right hand: the left half, so facing.)
Hands crossed over, or one hand in the middle, can't tell: then the expert's own view is assumed, as before.

What it can't see: how far the camera looked down (pass --camera-pitch), and footage mirrored like a selfie
(mirroring swaps the hands; turn "Mirror Front Camera" off, or film with the back camera).
Measured on the 14 recordings so far (October 11, 2026): the 9 with both hands in most frames vote 0.65 or more one
way (8 of them 0.9 or more), and agree with the two whose view we know (Count, PaulCube: facing).
"""

from dataclasses import dataclass

import numpy as np

from ghosthands.handpose import WRIST, Camera
from ghosthands.tracking import Tracking

MIN_TWO_HAND_FRAMES = 15  # fewer frames with both hands than this: judge each hand alone
SIDE_AGREEMENT = 0.6      # how one-sided the vote must be (1: every frame agrees)
OFF_CENTRE = 0.1          # one hand: how far from the middle it must stay, as a fraction of the picture's width


@dataclass(frozen=True)
class Viewpoint:
    camera: Camera
    detected: bool  # False: the video didn't say, so the default was used
    reason: str


def detect_viewpoint(tracking: Tracking) -> Viewpoint:
    votes: list[float] = []    # +1: right hand right of the left hand (expert's view), -1: left of it (facing)
    offsets: list[float] = []  # each hand's distance toward its own side of the picture (+) or the other side (-)
    seen: set[str] = set()
    for frame in tracking.frames:
        left, right = frame.hand("left"), frame.hand("right")
        if left is not None and right is not None:
            votes.append(float(np.sign(right.image[WRIST][0] - left.image[WRIST][0])))
        for hand, own_side in ((left, -1), (right, 1)):
            if hand is not None:
                offsets.append(own_side * (hand.image[WRIST][0] - 0.5))
                seen.add(hand.side)

    if len(votes) >= MIN_TWO_HAND_FRAMES:
        vote = float(np.mean(votes))
        if abs(vote) >= SIDE_AGREEMENT:
            facing = vote < 0
            share = round(100 * np.mean(np.array(votes) == (-1 if facing else 1)))
            return Viewpoint(Camera(facing=facing), True,
                             f"the right hand was {'left' if facing else 'right'} of the left hand "
                             f"in {share}% of {len(votes)} frames")
    if offsets:
        offset = float(np.median(offsets))
        if abs(offset) >= OFF_CENTRE:
            facing = offset < 0
            if len(seen) == 1:
                side = seen.pop()
                half = "left" if (side == "right") == facing else "right"
                reason = f"the {side} hand stayed on the {half} of the picture"
            else:
                reason = f"each hand stayed on its {'opposite' if facing else 'own'} side of the picture"
            return Viewpoint(Camera(facing=facing), True, reason)
    return Viewpoint(Camera(), False, "the video doesn't show it (hands crossed or central): assumed the expert's view")


def resolve_camera(camera: Camera | None, tracking: Tracking) -> tuple[Camera, str]:
    """The camera a lesson is built with: one set by hand (command line or keyframes file), else detected."""
    if camera is not None:
        return camera, "set by hand"
    viewpoint = detect_viewpoint(tracking)
    return viewpoint.camera, ("detected: " if viewpoint.detected else "") + viewpoint.reason


def describe(camera: Camera) -> str:
    view = "facing the expert" if camera.facing else "the expert's own view"
    return view + (f", looking down {camera.pitch_degrees:g} degrees" if camera.pitch_degrees else "")
