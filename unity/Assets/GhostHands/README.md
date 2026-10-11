# Ghost Hands — combined hands, ghost and lesson flow

All code is in namespace `GhostHands`. U1 hands/ghost code and U2 logic now work together, following Justin's request to combine both roles. The saved scene and CV/server/schema files are preserved. The bundled lesson is copied unchanged to `Assets/StreamingAssets/Lessons/example.json`.

`Core` is the shared `GhostHands.Core` assembly. Hands/rendering are under `U1` in `GhostHands.U1`; lesson logic is under `U2` in `GhostHands`. U2 references U1 for bootstrap wiring, and U1 depends on Core. Editor tools and tests live alongside them. Both roles use the same models and angle conventions.

## Run in the editor

1. Choose **Ghost Hands > Enable Editor Replay Demo**, then enter Play mode in SampleScene.
2. A runtime lesson controller, two translucent ghost hands and world-space panel appear. The fake source replays the current step's own joints. Practice runs through all five steps, then recall hides the renderer and repeats all five steps, then shows results.
3. With the configured server off, loading falls back to StreamingAssets after at most three seconds. Failed result submission leaves the summary displayed and logs the exact payload.
4. Choose **Ghost Hands > Enable Integrated Live Lesson** for real tracking. The fake source is excluded from Android builds.

For the hour-6 manual alignment check, choose **Ghost Hands > Enable Standalone Alignment Demo**, then enter Play mode. This loads the bundled lesson directly and runs `GhostDemo` without `LessonFlow`. **A / index pinch / N** advances; **B / P** goes back. Step index and instruction are exposed on the component and logged. For standalone alignment on Quest, add a `GhostDemo` component and assign the live source to it and its renderer; an existing demo prevents the automatic lesson bootstrap. No scene change is needed for the editor menus.

The runtime bootstrap creates `Ghost Hands Lesson` if the scene has no `LessonFlow`. An existing scene component takes precedence. To configure the lesson id, tester, tolerances or success audio in the Inspector before building, create your own U2 GameObject with `LessonFlow` and `LessonPanel`. `LessonLoader` is added automatically. This is optional; the default bootstrap loads `example`.

## Live hands and ghost integration

- `XRHandsJointSource` implements `IHandJointSource`: 21 finite world positions in `HandConventions.LessonJointOrder`, plus the rig's `CenterEyeAnchor`. It converts XR Hands origin-space joints using `TrackingSpace.TransformPoint` exactly once.
- If XR Hands provides no valid data, `InteractionHandJointSource` uses the Interaction SDK's `IHand.GetJointPose`. Those poses are already world-space and are not transformed again. The source exposes the provider for each hand; hardware verification is still required.
- The automatic bootstrap wires the live source (or editor replay) into both `LessonFlow` and `GhostHandRenderer`. An existing scene flow still takes precedence; assign its source and renderer in the Inspector or let binding discover them.
- `GhostHandRenderer` implements the agreed `Show`, `ShowStep(previous, next)`, `SetFingerStatus(finger, status)`, `SetVisible(bool)` and `Hide()` API. It draws 21 spheres and 20 bones per hand using a build-included transparent URP Unlit material, 35% alpha and no shadows. Live wrists anchor it, and every bone takes the learner's own length (`FitToHand`), so it fits any hand size. Between steps the ghost swings each bone from the old pose to the new one with ease in/out (`Core/GhostTransition`), over the expert's move time kept within `LessonFlow.minTransitionSeconds`–`maxTransitionSeconds` (0.6–1.5 s); practice tracking pauses until it lands. Without tracking it uses the specified head-relative fallback and gray feedback.
- Step transitions lerp joints and slerp wrist rotation over `next.durationMs`. Missing hands are hidden; recall visibility comes from U2. `HandTrackingDebug.drawJoints` shows 21 magenta live spheres per tracked hand and is off by default.
- Shared models use the JSON field names (`lesson.steps`, `step.hands`, etc.). Helpers convert joint arrays and wrist quaternions directly.
- The agreed renderer API has no left/right parameter. U2 therefore sends the worst status across the hands required by the step. Individual hand feedback remains available through `LessonFlow.HandMatches`.
- During a lesson, a small dock within arm's reach (low left) has MENU (`LessonFlow.Stop()`, back to the main menu) and RETRY (`LessonFlow.Retry()`). They are pressed on the Quest by pushing an index fingertip through them from the front (`PokeButton` in `U2/UI/Poke.cs`, using the hand joint source; no Meta UI wiring needed). The mouse clicks them in the editor; an existing EventSystem is reused.

## Main menu

The app opens at the main menu (`U2/Menu`), floating 0.5 m ahead: translucent glass over passthrough, content on solid cyan-edged boxes, Rajdhani font (OFL, `Resources/GhostHands/Fonts`). Start lists every lesson on the server (`GET /lessons`; the bundled example offline) as cards: swipe the row with a fingertip or use the arrows, tap a card to play it. Credits lists the team. The user ID at the top is a stand-in (`MainMenu.userId`). When no lesson is running, `LessonFlow.State` is `Idle` and the menu shows. Ghost Hands > Play Lesson's "Start at main menu" (on by default) opens here; turn it off, or pass `-lessonId`, to go straight into a lesson.

## Networking

`LessonLoader.serverUrl` defaults to `http://10.32.217.238:3000`. For a persistent change without rebuilding, call `loader.SetServerUrl("http://<laptop-ip>:3000")`. It saves `GhostHands.ServerUrl` in PlayerPrefs. Clear that key to restore the Inspector default.

GET `/lessons/:id` and GET `/lessons` have three-second timeouts. A failed lesson request or invalid lesson falls back to the bundled example. A failed list request lists the bundled example. Android's StreamingAssets URL is read with UnityWebRequest, including its `jar:file://` path.

Player Settings are configured with **Allow downloads over HTTP = Always Allowed** and **Force Internet Permission = true**. `AndroidNetworkSettings` also enforces these before Android builds. Android's OpenXR **Hand Tracking Subsystem** is now enabled. Meta's project setting already supported **Controllers and Hands** and was preserved. Package versions and the rig were preserved.

POST `/results` uses the exact six top-level contract fields and four fields per step, with `device: "Quest 3S"`. A successful reply must be HTTP 201 with `ok:true` and a positive `sessionId`. Metrics cover the recall round: total time through each step (including the hold), tolerance-break retries, and tracking-loss episodes. Pass steps record their animation time. A hold that has not completed by 20 seconds fails recall and advances, so results remain reachable.

## Matcher and timing

One matcher per hand computes the specified hand frame and signed bone bends, with unsigned thumb bends. It starts smoothing at the first valid sample and uses alpha 0.3 thereafter. A new step or tracking loss resets smoothing. Default tolerances: thumb 25°, other fingers 20°, wrist 35°; Close extends to 1.75 times the finger tolerance. Labeled steps check wrist rotation relative to head yaw by default. Missing or degenerate joints are Unavailable.

Every required hand must match continuously for `holdMs`. A tolerance break resets the hold and counts a retry only if a hold was in progress. Tracking loss resets the hold and counts once per unavailable episode. Pass steps advance once after `durationMs`, regardless of tracking. The UI pulses green on successful advancement; assign an AudioSource for a sound as well.

## Tests

Open **Window > General > Test Runner > EditMode** and run `GhostHands.Tests.Editor`.

The suite tests all example poses, a 40° index-only bend, smoothing, rotation/scale invariance, wrist yaw, invalid joints, hold/pass thresholds, continuous two-hand matching, tracking-loss episodes, five-step practice/recall, ghost visibility, recall deadlines, validation and payload fields. U1 tests check the artifact's 8° angle bound, near-identity hand frames and mirrored angles; actual renderer geometry, transparency, shadows, wrist/yaw math, clamped scaling, fallback/smoothing, finger properties, pass animation, missing hands, manual demo, debug joints and combined tracking loss/recall. Local HTTP fixtures test server-off/404/bad JSON fallbacks, lesson listing and loading, a 201 results reply, and failed result retention. They do not modify `/web` or access Neon.

## Quest checklist

- Install the matching editor's Android SDK/NDK and OpenJDK; verify their paths in Preferences > External Tools.
- Build ARM64 for Quest 3S with U1's OpenXR/hand-tracking setup. Confirm the runtime panel is readable and the retry button responds to Quest interaction.
- Stop the laptop server: verify bundled lesson loading and retained results.
- Start the real server on the same Wi-Fi, update the PlayerPrefs URL, and verify GET `/lessons/example` and a real 201 POST `/results`.
- Check both hands, labeled wrist poses, gray tracking loss, timer reset, pass animation, hidden recall, and results. Tune the serialized tolerances against real hands.

Headset tracking quality, ghost alignment/animation, Quest UI input, Android builds and the real laptop/Neon server require device/server verification. The local HTTP fixture only verifies the client contract.

## Verified locally (October 10, 2026)

- Unity reports `scriptCompilationFailed: false` after compiling the final Core/U2 assembly layout.
- All 38 EditMode tests pass, including the fake-source five-step practice/recall walkthrough, inclusive tolerance boundaries, restart and reentrant advancement, late renderer binding, panel recentering, and local HTTP client fixtures.
- Play mode completed all five practice steps and all five recall steps using editor replay and a closed local server port. Bundled fallback succeeded, recall passed, and failed submission retained five result records. Invoking the actual Retry button callback reset state/metrics; the second run also completed with five fresh records.
- Direct `CenterEyeAnchor` camera rendering shows the results panel in front of the learner. The Pipeline Game-view/XR mirror capture omitted it, so that mirror's visual behavior remains unverified. No rig or simulator setting was changed. Play mode was stopped, original server/replay preferences restored, and SampleScene remained clean.
- The latest U2 compilation emits no C# warnings or errors. Deprecated object-search overloads were replaced with Unity 6000.6 APIs. `git diff --check` passes for the U2 code and intentional Player Settings changes.
- Android Build Support is installed, but its SDK/NDK and OpenJDK children are missing. Installation is deferred at Justin's request. The official installer reports approximately 1.5 GB of downloads and 3.6 GB installed; free at least 6 GB before continuing.
- Actual installed packages include XR Hands 1.9.0 and Newtonsoft JSON 3.2.2. No package version was changed for U2.

See `U2_AUDIT.md` for the requirement-by-requirement verification and remaining integration limits.

The U2 audit above records the earlier isolated validation. See `COMBINED_AUDIT.md` for the subsequent U1/U2 integration results and current remaining limits.
