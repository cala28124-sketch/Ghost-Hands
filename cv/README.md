# cv

The laptop-side Python pipeline (OpenCV + MediaPipe Hand Landmarker). It turns an expert's demo video into lesson JSON.

1. Read a short, well-lit clip and find the 21 hand landmarks in each frame.
2. Save timestamps, handedness, landmarks, and tracking confidence. Skip low-confidence frames.
3. Make an annotated review video or contact sheet.
4. Pick keyframes (by hand is fine) and label each step with an instruction.
5. Convert to wrist-relative points and compute finger angles.
6. Export lesson JSON that matches the schema in `shared/`, and validate it.

MediaPipe only finds landmarks. It doesn't understand tasks, so step naming is manual or assisted.
