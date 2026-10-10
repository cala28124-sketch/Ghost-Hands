# Ghost Hands U1 + U2 integration audit

Justin authorized combining Paul's hands/headset/ghost prompt with the existing U2 implementation. This supersedes the pasted prompt's ownership restrictions between U1 and U2. No commits were made during implementation before Justin requested a separate integration branch; `/cv`, `/web`, `/shared`, lesson format and saved scene were preserved. SDK/NDK/Java installation remains paused at Justin's request.

## Implemented contract

| Paul U1 requirement | Implementation and integration |
| --- | --- |
| Live 21-joint source | `U1/Tracking/XRHandsJointSource.cs`: XRHandSubsystem, exact shared joint order, TrackingSpace.TransformPoint once, CenterEyeAnchor head. Per-frame cache gives matcher, renderer and debug a consistent snapshot. |
| Interaction SDK fallback | `U1/Tracking/InteractionHandJointSource.cs`: IHand.GetJointPose, matching 21-joint map, connected/valid guards. These poses are already world-space. Used only when XR Hands has no valid data. Hardware/provider selection remains unverified. |
| Android hand configuration | Enabled OpenXR Hand Tracking Subsystem for Android through Unity's settings API. Meta already had ControllersAndHands; preserved that setting. Kept loader, packages, rig and other OpenXR features unchanged. |
| Tracking debug | `U1/Tracking/HandTrackingDebug.cs`: optional 21 live magenta spheres per hand; missing hands hidden; no colliders or shadows; disabled by default. |
| Shared core / artifact checks | Reused existing Core without redefining models or angle math. Tests enforce finger angles within 8°, frames within 5° of identity, equal mirrored step-01 angles. Artifact maximum raw finger error is about 6.87°; nominal frame offset is about 3.08°. |
| Ghost geometry | `U1/Rendering/GhostHandRenderer.cs`: two hands, 21 joints + 20 bones each; transparent URP Unlit, pale cyan at 35% alpha, no shadows/colliders. Resource material ensures shader/asset inclusion in builds. |
| Placement and scale | Exact anchor + headYaw × (lessonRotation × joints × scale); live wrist or specified 0.35 m forward / 0.15 m down fallback; palm-length ratio clamped 0.7–1.4; exponential anchor smoothing at 10/s. Untracked fingers gray. |
| Finger feedback | Agreed API maps Matched/Close/Incorrect/Unavailable to green/amber/red/gray using renderer property blocks. Existing U2 matcher supplies feedback. The API has no hand parameter, so U2 sends the worst status across required hands. |
| Pose transitions | ShowStep interpolates joints and wrist quaternion over next.durationMs. Hands absent from the next step hide immediately. U2 pass timing and recall visibility drive the real renderer. |
| Alignment demo | `U1/Demo/GhostDemo.cs`: bundled StreamingAssets via UnityWebRequest, A/B controller navigation, index pinch rising edge, N/P keyboard navigation, wrapping steps. Runs separately from lesson flow. |
| Combined startup | Existing U2 bootstrap now wires either live XR + fallback or editor replay into the real ghost renderer and lesson flow. Standalone demo and integrated flow never auto-start together. Runtime setup preserves SampleScene. |

## Automated validation — October 10, 2026

- Unity 6000.6.5f1, project `/Users/justinteape/Projects/Ghost-Hands/unity`, active scene `Assets/Scenes/SampleScene.unity`.
- **53 EditMode tests passed; 0 failed, 0 skipped.** Includes the previous 38 U2 tests and 15 new U1/integration tests.
- Renderer tests inspect actual geometry, material/shadow settings and finger property blocks; placement, scale clamps, smoothing, transitions, tracking loss, hidden recall, manual demo and debug visibility all pass.
- Existing local HTTP fixtures still verify successful GETs, failure fallbacks, exact results JSON and HTTP 201 handling. They do not use the real laptop server or Neon.
- Managed C# compilation completed with `scriptCompilationFailed == false`. No new GhostHands C# warnings/errors are emitted.
- `git diff --check` passes for the authored C# code and intentional OpenXR/Player Settings changes. Imported TextMesh Pro files and Unity-generated asset/meta files retain their original whitespace.

## Runtime smoke test

- Server URL temporarily set to closed localhost port; editor replay enabled. Natural game frames completed Practice → Recall → Results, `completed == true`, `recallPassed == true`, exactly five result records. Failed POST kept the summary on screen.
- Real renderer created all 82 meshes. A controlled runtime inspection showed both green ghost hands and the practice panel; direct CenterEyeAnchor image capture confirmed their appearance. Entering recall hid both hands immediately.
- Actual Retry button callback returned Loading, cleared progress/results and reset step index. A second natural run again reached Results with five fresh records.
- Standalone alignment mode loaded `example` from StreamingAssets, showed both fallback ghosts and started no LessonFlow. Runtime Next/Previous calls reached the right-only pass, hid the left hand and restored it on return.
- Standalone live source resolved `TrackingSpace` and `CenterEyeAnchor` correctly. Both providers reported Unavailable in this simulator session; real tracked joints/scale/alignment are not asserted to pass.
- Two earlier attempts were stopped through Unity's toolbar before completing. The final run, with Unity left playing, passed the checks above. These aborted attempts are not evidence of a GhostHands failure.
- Unity crashed in native Meta/OpenXR teardown when stopping the standalone demo. The project was reopened successfully; Pipeline is ready, SampleScene is clean and managed compilation still passes. The crash erased the in-memory preference backup, so temporary localhost/alignment/replay overrides were removed and Inspector defaults / integrated live mode restored. No exact restoration of prior custom PlayerPrefs values is claimed.

## Tooling limits

The installed Editor's Burst native linker logs Permission denied. `burst-lld-21-hostmac` is owned by root with mode `-rwxrw-r--`: the owner can execute it, but the user's group/other permissions cannot. This is separate from the managed C# compilation verdict and prevents claiming a completely clean native toolchain. It was not hidden by disabling Burst or altering the installed Editor.

Android SDK/NDK/OpenJDK remain missing and installation is deferred. Meta XR Simulator also logs that adb is unavailable. No Android build or headset alignment claim is made.

The native XR shutdown crash remains a simulator/Editor integration issue to investigate before calling the entire toolchain clean. Its teardown stack contains OpenXR/Meta loader destruction; the passing runtime behavior above does not establish shutdown stability.

## Run and device checklist

1. **Ghost Hands > Enable Editor Replay Demo**, then Play: observe two ghost hands, animated practice, hidden recall, results and Retry. Replay uses fake poses and is excluded from Android builds.
2. **Ghost Hands > Enable Standalone Alignment Demo**, then Play: use N/P or A/B/pinch to inspect each pose at your pace. This mode uses live tracking; without tracking it shows gray fallback ghosts.
3. **Ghost Hands > Enable Integrated Live Lesson** restores real tracking for normal flow. On Quest, verify debug joints coincide with real joints and inspect the selected source provider; test each hand's loss/recovery.
4. Verify wrist/head-yaw alignment, scale at different hand sizes, smooth anchoring, pass animation, missing left hand in step-03, feedback colors and hidden recall.
5. Test world-space UI readability and Quest ray/poke Retry input; the Canvas still needs the device's Interaction SDK UI input wiring verified.
6. After SDK tooling is installed, build ARM64 on Quest 3S. Test server-off fallback and real same-Wi-Fi GET `/lessons/example` / POST `/results` → 201. Tune tolerances with real hands.
