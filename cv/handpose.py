"""Hand geometry: MediaPipe world landmarks -> lesson-schema hand (joints, wristRotation, targetAngles).

Conventions (shared/lessons/README.md):
  - Unity axes: x right, y up, z forward (away from the viewer).
  - joints are in the hand's own frame: wrist at origin, palm down (-y), fingers along +z.
    The right hand's thumb is on -x; the left hand is the right hand mirrored across x.
  - wristRotation maps the hand frame into the viewer (camera) frame, as a Unity [x, y, z, w] quaternion.

MediaPipe world landmarks are meters, origin near the hand center, x right, y down, z away from the camera.
"""

import numpy as np

WRIST = 0
INDEX_MCP, MIDDLE_MCP, PINKY_MCP = 5, 9, 17

# Joint chains used for flexion angles, from the wrist out to the fingertip.
# Each finger's angles are measured at every interior joint of its chain.
FINGER_CHAINS = {
    "thumb": [1, 2, 3, 4],          # angles at MCP (2) and IP (3)
    "index": [0, 5, 6, 7, 8],       # angles at MCP, PIP, DIP
    "middle": [0, 9, 10, 11, 12],
    "ring": [0, 13, 14, 15, 16],
    "pinky": [0, 17, 18, 19, 20],
}


def mediapipe_to_unity(world):
    """(21, 3) MediaPipe world landmarks -> Unity viewer axes (flip y)."""
    p = np.asarray(world, dtype=float).copy()
    p[:, 1] *= -1.0
    return p


def hand_basis(p, hand):
    """Rotation matrix whose columns are the hand's right, up, forward axes in viewer space."""
    forward = p[MIDDLE_MCP] - p[WRIST]
    forward /= np.linalg.norm(forward)
    # Palm down, fingers forward: the index knuckle is on -x for a right hand, +x for a left hand.
    across = p[PINKY_MCP] - p[INDEX_MCP]
    right = across if hand == "right" else -across
    right = right - right.dot(forward) * forward
    right /= np.linalg.norm(right)
    up = np.cross(forward, right)  # back of the hand
    return np.column_stack([right, up, forward])


def quat_from_matrix(m):
    """Proper rotation matrix -> [x, y, z, w], with w >= 0."""
    t = np.trace(m)
    if t > 0:
        s = np.sqrt(t + 1.0) * 2
        w, x, y, z = 0.25 * s, (m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        w, x, y, z = (m[2, 1] - m[1, 2]) / s, 0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = np.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        w, x, y, z = (m[0, 2] - m[2, 0]) / s, (m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s
    else:
        s = np.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        w, x, y, z = (m[1, 0] - m[0, 1]) / s, (m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s
    q = np.array([x, y, z, w])
    q /= np.linalg.norm(q)
    return q if q[3] >= 0 else -q


def flexion_angles(local, chain, signed):
    """Bend at each interior joint of a chain, in degrees. 0 = straight.

    signed=True: positive bends toward the palm (-y), negative is hyperextension. The hinge axis of
    the four fingers is roughly the hand's x axis. The thumb's hinge is not, so its angles are unsigned.
    """
    out = []
    for a, b, c in zip(chain, chain[1:], chain[2:]):
        b1 = local[b] - local[a]
        b2 = local[c] - local[b]
        cross = np.cross(b1, b2)
        ang = np.degrees(np.arctan2(np.linalg.norm(cross), b1.dot(b2)))
        if signed and cross[0] < 0:
            ang = -ang
        out.append(float(np.clip(ang, -90.0, 180.0)))
    return out


def lesson_hand(world, hand):
    """MediaPipe world landmarks for one hand -> the schema's hand object."""
    p = mediapipe_to_unity(world)
    p -= p[WRIST]
    basis = hand_basis(p, hand)
    local = p @ basis  # each row expressed in the hand frame
    angles = {name: flexion_angles(local, chain, signed=(name != "thumb")) for name, chain in FINGER_CHAINS.items()}
    return {
        "targetAngles": {k: [round(a, 1) for a in v] for k, v in angles.items()},
        "joints": [[round(float(c), 4) for c in pt] for pt in local],
        "wristRotation": [round(float(c), 4) for c in quat_from_matrix(basis)],
    }
