# Demo, risks, and done

## Judge demo script

1. Pitch: "Ghost Hands turns expert demonstrations into repeatable mixed-reality practice."
2. Show a short expert clip and explain how the pipeline picks key poses automatically and stores the lesson in Neon.
3. Put the judge in the Quest 3S and launch a preloaded lesson.
4. Match a pose; show the finger colors and the stable-hold success.
5. Go through 2–3 steps, then hide the ghost for recall.
6. If reliable, upload a short video on the webpage and show the new lesson appear; then show the judge's practice results in Neon.
7. Close on equipment-anchored guidance as the future, while being clear universal recognition isn't in the prototype.

Keep the live demo short. Have a prerecorded backup and a preloaded lesson. Never make a judge wait on network transfer or processing.

> "Ghost Hands transforms expert demonstrations into reusable mixed-reality lessons, guiding people through physical tasks with a live ghost hand and testing whether they can repeat the steps without help."

## Risks

| Risk | Mitigation |
|---|---|
| Quest SDK/API incompatibility | Prove tracking and deployment first; freeze versions |
| Ghost drift / coordinate mismatch | Headset-native live joints and wrist-relative target geometry |
| Noisy or occluded video landmarks | Simple clip, annotated review, manual keyframes, reject low-confidence frames |
| Over-ambitious automation | Manual step labels allowed; prove one lesson first |
| Matcher too strict or loose | Per-finger tolerances, real-user tuning, pause on tracking loss |
| Network transfer fails | Bundled lesson and recorded fallback |
| Neon outage or free-tier limits | Short clips (10–30 s); server serves local lesson files; bundled lesson in Unity |
| Automatic keyframes pick wrong poses | Record with 1 s freezes; tune `--still-speed`; fix the keyframes file by hand |
| Unity merge conflicts | One scene owner, prefabs/components, frequent integration |
| Overclaiming AI or learning | State what's actually built; show real test results |

## Definition of done

- [ ] A new user understands the first instruction without verbal coaching
- [ ] One 3–5-step lesson works on Quest 3S
- [ ] The ghost is stable and target fingers are visually distinct
- [ ] Feedback and tracking-loss behavior are understandable
- [ ] A stable hold advances exactly once
- [ ] Recall hides the target and gives clear results
- [ ] At least one lesson made automatically from an uploaded video imports into XR
- [ ] Lessons and practice results are stored in Neon
- [ ] A backup recording exists, and what's built vs. roadmap is clear
- [ ] All four teammates are registered on the submission page

## Roadmap (after the hackathon)

| Horizon | Capability | Requires |
|---|---|---|
| Next | User-recorded key poses and shareable JSON | Capture/calibration UI, schema validation |
| Next | Object-relative placement | Manual anchor or known object pose |
| Later | Equipment-specific task variants | Approved procedures per machine/model |
| Later | Automatic equipment recognition | Object detection, uncertainty handling, user confirmation |
| Long term | Adaptive task training | Task graph, prerequisites, anchors, safety constraints |
