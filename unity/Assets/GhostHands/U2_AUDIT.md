# Justin U2 audit — October 10, 2026

Audited against Justin's supplied task prompt, the U2 section and angle code in `Ghost Hands CV Handoff (Copy).md`, and the local shared lesson/server contracts. The user's later instructions override the document's commit schedule: this audit created no commits and made no changes outside the Unity project. Android tool installation remains deferred.

## Requirement coverage

| Requirement | Implementation | Evidence / limits |
| --- | --- | --- |
| Shared models and conventions | `Core/LessonModels.cs`, `HandConventions.cs`, `IHandJointSource.cs`, `FingerStatus.cs` | Exact JSON names and 21-joint mapping; hand frame, signed finger bends and unsigned thumb bends match the handoff formula. Every example hand/step matches its own targets. |
| Lesson loading and listing | `U2/Loading/LessonLoader.cs`, bundled `StreamingAssets/Lessons/example.json` | Three-second requests, persistent URL override, schema/pose validation, Android-compatible UnityWebRequest fallback. Local HTTP tests cover successful GET, server-off, 404, malformed JSON, invalid schema and malformed URL. Runtime server-off fallback passed. |
| Android HTTP access | `Editor/AndroidNetworkSettings.cs`, Player Settings | Always Allowed HTTP downloads and forced INTERNET permission confirmed in the Editor. Android SDK/NDK/JDK are missing; no Android build verified. |
| Plain C# matcher | `U2/Matching/FingerMatcher.cs` | EMA 0.3; configurable 25° thumb / 20° fingers; Close through 1.75×; tracking Unavailable; default 35° labeled wrist check relative to head yaw; unambiguous worst-finger hint. Tests cover 40° isolated bend on both hands, smoothing, transforms, missing data, wrist mismatch and inclusive boundaries. Mathf.Approximately handles arithmetic roundoff at a boundary. |
| Practice and pass flow | `U2/Flow/LessonFlow.cs`, `StepProgress.cs` | Every required hand must match continuously. Broken tolerance and lost tracking reset hold; losses count once per episode. Pass ignores matching and advances on duration. Green pulse and optional audio. Tests cover exact thresholds, mismatch, two-hand requirement and reentrant completion. |
| Recall and metrics | Same flow, `U2/Results/SessionResults.cs` | Renderer hidden; hold deadline 20 seconds; per-step recall time, retries and tracking losses. Timeout fails recall and advances. Tests cover exact deadline and crossing it. Runtime replay passed all five recall steps. |
| Results POST | `U2/Results/ResultsClient.cs` | Exact six top-level / four per-step fields, Quest 3S device. Local HTTP fixture returns real HTTP 201 and verifies JSON; failure and invalid URL retain/log results. Runtime closed-server POST preserves summary. Actual laptop server remains unverified. |
| UI and restart | `U2/UI/LessonPanel.cs`, runtime bootstrap | World-space instruction, label, step count, hold feedback and results; recenters when head moves out of view. Actual button callback resets state/metrics and a second runtime walkthrough succeeds. Quest ray/poke input still requires U1 integration. |
| U1 boundary | `GhostRendererAdapter.cs`, separate Core/U2 assemblies | Uses the agreed renderer API and IHandJointSource; implements no live tracker or ghost renderer. Late renderer binding receives current step and visibility. Real U1 tracking/rendering, ghost animation/alignment and colors require integration verification. |

## Final verification

- Connected Editor: Unity 6000.6.5f1, project `/Users/justinteape/Projects/Ghost-Hands/unity`, scene `Assets/Scenes/SampleScene.unity`.
- Final EditMode suite: **38 total, 38 passed, 0 failed, 0 skipped**.
- Compilation complete; `scriptCompilationFailed == false`; latest U2 C# compilation has no warnings or errors.
- `git diff --check` passes for `Assets/GhostHands` and intentional Player Settings changes.
- Runtime replay with server URL temporarily `http://127.0.0.1:1`: Practice → Recall → Results, five result records, completed true, recallPassed true, zero retries/losses for perfect replay, failed POST kept on screen.
- Retry button callback returned Loading, cleared Results/Progress, reset step index to zero; second run again completed with exactly five records.
- Results panel center in camera viewport approximately `(0.50, 0.54, 1.20)`; direct CenterEyeAnchor rendering confirms visible text and button. Pipeline's Game-view/XR mirror capture omitted the panel, so mirror presentation is not asserted to pass.
- Tests used fake joints and a disposable local HTTP fixture; they did not run or modify `/web`, `/cv`, `/shared`, or contact Neon.
- Play mode stopped after testing; prior server/replay preferences restored. Active scene remained `isDirty == false`; no scene save or rig edit was performed during this audit. Existing unrelated working-tree changes were preserved.

## Remaining device/server checks

1. When authorized and disk space is available, install SDK/NDK/OpenJDK and build ARM64 for Quest 3S.
2. Connect U1's live source and renderer; verify mapping, ghost alignment/pass animation, per-finger colors and hidden recall with actual tracking.
3. Verify the world-space panel and Retry using Quest ray/poke input. Check the simulator's visible XR mirror separately.
4. Stop the real laptop server to confirm fallback, then start it on the same Wi-Fi and verify GET `/lessons/example` and real POST `/results` → 201.
5. Break a hold, lose/reacquire each hand, let recall exceed 20 seconds, retry, and tune the serialized finger/wrist tolerances on device.
