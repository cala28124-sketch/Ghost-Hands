# Test checklist

## Functional

- [ ] App launches on Quest 3S and receives hand-tracking data
- [ ] Target ghost doesn't depend on the learner's current pose
- [ ] Ghost wrist alignment stays stable during practice
- [ ] Correct fingers turn green; others stay amber or red
- [ ] Tracking loss shows gray and can't accidentally pass a step
- [ ] Hold timer resets outside tolerance; a successful hold advances exactly once
- [ ] All steps complete; retry is clear
- [ ] Recall hides the ghost and shows results
- [ ] Missing or malformed JSON gives a useful error or falls back to the bundled lesson
- [ ] Quest Pro tested, or device differences documented
- [ ] At least one video-assisted lesson (from `cv/`) imports into Unity
- [ ] Headset loads a lesson from the `web/` server; bundled fallback works when the server is off
- [ ] Upload page: a video uploads from a phone and a PC and becomes `pending`
- [ ] A practice run posted to `POST /results` appears in Neon (`sessions`, `step_results`)
- [ ] With Neon unreachable, `/health` says `offline` and lessons still load from local files

## Usability and reliability

- [ ] At least five testers complete the lesson, if possible
- [ ] Record per tester: time to first success, retries, tracking-loss events, completion, clarity
- [ ] Test different hand sizes, orientations, lighting, and some occlusion
- [ ] Feedback tells learners *what to change*, not just whether they're right
- [ ] Report results honestly; no invented accuracy or learning claims

| Tester | Time to first success | Retries | Tracking losses | Completed | Notes |
|---|---|---|---|---|---|
| | | | | | |

## Demo readiness

- [ ] Preloaded lesson on the judging headset
- [ ] Backup demo video recorded
- [ ] 60–90 second demo rehearsed
- [ ] Known bugs written down
- [ ] All four teammates registered on the submission page
