# Frame Game AI Handoff

This document is for future language-model assistants working on this Unity project. It summarizes the current direction, the important scripts, and the fragile areas that should be understood before changing code.

## Project Goal

The game is a 2D turn-based stick-fighter inspired by `Your Only Move Is Hustle`.

The desired loop is:

1. Player edits a translucent frozen copy of the fighter.
2. The live fighter repeatedly previews what will happen from the current snapshot toward that frozen target.
3. Player commits the turn.
4. The live fighter simulates for a fixed duration.
5. The resulting live pose is captured as the next snapshot.
6. The full motion can be replayed as a final animation.

The current direction favors:

- marker-authored fighters that build their physics rig at runtime
- **angle-driven movement** via PoseDriver hinge motors as the primary control method
- minimal force -- only the root/hips body gets translational force to maintain structure
- planted feet using temporary FixedJoint2D for ground contact stability
- **Auto Limbs** used primarily for walking, standing, and hand-grounding during posing
- upper-body limbs manually poseable as Basic/Block or Strike (unless in Auto mode)
- **chain-isolated dragging** -- moving a hand only affects its arm chain; moving a foot only affects its leg chain

## Current Architecture

### `FreezeReplayV2.cs`

This is the main controller. Start here for most gameplay issues.

Responsibilities:

- instantiates the live fighter prefab
- optionally builds a runtime rig from marker-only authoring points
- clones the live fighter into the frozen copy
- manages turn phases: `Posing`, `Simulating`, `Capturing`
- records replay frames on `FixedUpdate`
- runs the posing preview loop
- wires `PoseDriver` components
- manage `LimbMoveIntent` state (Auto/Basic/Strike/Recovery)
- drive Auto-limb movement and hand/foot planting
- handle planted limb joints (FixedJoint2D) and replanting (TargetJoint2D)
- stabilizes and clamps the frozen copy
- draws the simple IMGUI move-planning UI
- **loads/saves presets** via `FRV2Preset` system
- **auto-loads a named preset on Start** via `autoLoadPresetName`

Important methods:

- `Start()`: instantiate/build/wire initial fighter and frozen copy; calls `TryAutoLoadPreset()`
- `CommitPose()`: stop preview, restore snapshot, begin committed sim
- `StartPosePreviewLoop()`: restore the live fighter from the captured snapshot and simulate preview against the current authored frozen copy without mutating it
- `FixedUpdate()`: applies assist systems and advances committed/preview fixed steps
- `BeginLimbPlansForSimulation()`: begins limb intents, caches planted feet, creates planted-foot joints
- `ApplyAdaptiveLegAssist()`: decides whether Auto-walk is active
- `ApplyAutoWalkLegAssist()`: current Auto-walk driver
- `GetAutoMidJointTarget()`: 2-bone IK for knee/elbow placement -- uses cross-product from live bodies for bend direction
- `GetAutoLimbEndTarget()`: calculates world/relative target for feet and hands based on stride and planting state
- `StabilizeFrozenCopyPose()`: clamps frozen copy root, joint limits, joint anchors, and floor penetration
- `ApplyCurrentLowForceDefaults()`: context-menu/reset button for the current intended tuning
- `SaveCurrentPreset()` / `LoadPreset()`: preset file I/O

The custom inspector in `Editor/FreezeReplayV2Editor.cs` hides many experimental fields in foldouts. The fields still exist and are serialized. The inspector also has a **Presets** section for loading/saving presets.

### `FRV2Preset.cs`

Serializable preset system for FRV2 tuning values.

- `FRV2Preset.CaptureFrom(frv2)`: snapshot all current settings into a preset
- `preset.ApplyTo(frv2)`: apply a preset to FRV2
- `preset.SaveToFile()` / `FRV2Preset.LoadFromFile(name)`: JSON files in `[ProjectRoot]/FRV2Presets/`
- `FRV2Preset.GetAvailablePresetNames()`: lists saved presets
- Keep `CaptureFrom()` and `ApplyTo()` symmetric. A missing field on either side makes presets look correct in JSON but behave differently after reload.

Presets cover: PoseDriver gains, pose assist springs/forces, planted foot joints, auto-walk, adaptive leg assist, combat multipliers, turn duration, and more.

### `FRV2Diagnostics.cs`

Runtime telemetry and auto-tuning system. Attach to the same GameObject as FRV2.

- Samples tracking error (position + angular) every `FixedUpdate` during simulation
- Computes per-turn summaries with per-body metrics (avg/max error, oscillation count)
- Detects preview-vs-commit divergence
- Writes JSON reports to `[ProjectRoot]/FRV2Diagnostics/`
- Generates **optimized presets** based on heuristic analysis of telemetry data
- F8: write report; F9: generate optimized preset

Heuristic rules:
- High position error -> suggest increasing poseAssistSpring / maxPoseAssistForce
- Oscillation -> suggest increasing poseKd / poseAssistDamping
- Force saturation -> suggest increasing force caps
- Damping-to-spring ratio checks
- Walk-specific leg error analysis

### `FighterRuntimeRigBuilder.cs`

Builds a playable physics fighter from marker-only prefabs.

Expected marker names include:

- `Hips`
- `Shoulders`
- `Head`
- `Elbow L`, `Elbow R`
- `Hand L`, `Hand R`
- `Knee L`, `Knee R`
- `Foot L`, `Foot R`

The builder creates:

- `Rigidbody2D`
- colliders
- sprite visuals
- `HingeJoint2D`
- optional `ImpactDamage`
- optional `FighterHealth`
- optional `LimbMoveIntent`
- `FighterRuntimeRigInstance` for self-collision ignores

`FreezeReplayV2` auto-adds this builder when the assigned prefab looks like a marker-only fighter and has no child `Rigidbody2D` components. If a `FighterRuntimeRigBuilder` is also placed on the same GameObject as `FreezeReplayV2`, its settings are copied to the spawned fighter builder.

### `SmartPoser.cs`

Handles mouse interaction with the frozen copy.

Current behavior:

- root/hips drag is handled directly through `FreezeReplayV2.BeginFrozenRootPoseEdit`, `UpdateFrozenRootPoseEdit`, and `EndFrozenRootPoseEdit`
- non-root limb drag temporarily switches the frozen copy to `Dynamic`, creates a `TargetJoint2D`, then returns the frozen copy to `Kinematic`
- after release, it calls `FreezeReplayV2.StabilizeFrozenCopyPose()` and requests preview restart
- **chain-isolated dragging**: when dragging, `BuildLimbChain()` identifies all bodies in the same kinematic chain (e.g., Hand -> Lower Arm -> Arm), then anchors all bodies NOT in that chain with `TargetJoint2D` anchors, ensuring only the dragged limb chain moves

Chain isolation logic:
- Walks DOWN from dragged body to find HingeJoint2D children
- Walks UP via `connectedBody` links, stopping at root bodies or branch points (bodies with multiple jointed children like hips/spine)
- All non-chain bodies get anchored in place

**Component placement**: SmartPoser lives on the **same GameObject as FRV2**. It references FRV2 to access the frozen copy and call BeginFrozenPhysicsPoseEdit / EndFrozenPhysicsPoseEdit. Its settings (dragForce, frequency, etc.) are specific to the drag interaction and do NOT overlap with FRV2's simulation settings.

Important: frozen copy should not be left dynamic after a drag. If frozen limbs drift after release, check the `BeginFrozenPhysicsPoseEdit` / `EndFrozenPhysicsPoseEdit` path.

### `PoseDriver.cs`

A PD motor controller on live hinge-jointed limbs. It drives a live `HingeJoint2D` toward the matching frozen-copy `HingeJoint2D`.

**Component placement**: PoseDriver instances are added to **each limb body of the LIVE fighter** (not the frozen copy, not the FRV2 controller). They are automatically wired by `FreezeReplayV2.WirePoseDrivers()` during setup. There is also a `PoseDriverSetup.cs` utility script, but FRV2 handles wiring directly.

`FreezeReplayV2` wires these in `WirePoseDrivers()` and re-applies:

- `poseKp`
- `poseKd`
- `poseMaxSpeed`
- `ignoreJointLimits`

Current relevance:

- **Primary control method** in the angle-driven preset -- all jointed limbs are driven by PoseDriver hinge motors
- Auto limbs use PoseDriver when `usePoseDriversForAutoLimbs = true`
- Auto-walk also has its own joint-motor path controlled by `useJointMotorsForAutoWalk`
- PoseDriver settings on FRV2 (poseKp, poseKd, etc.) are global and override the per-PoseDriver values during `ApplyPoseDriverSettings()`

### `LimbMoveIntent.cs`

Per-limb action state.

Modes:

- `Auto`
- `BasicBlock`
- `Strike`
- `Recovery`

Strike behavior:

- strike limbs get a velocity multiplier during simulation
- strike damage and impulse are applied through `ImpactDamage`
- after a strike, the limb enters recovery for `strikeRecoverySnapshots`
- **Auto state**: Supported for both legs and arms. 
  - Legs default to `Auto` when `defaultLegsToAuto` is enabled.
  - Arms support `Auto` for hand-grounding interaction. 
  - In Auto, limbs are not manually poseable through `SmartPoser`; they are driven by the IK system toward ground or walk targets.

### `ImpactDamage.cs`

Collision damage and strike impulse.

Important preview detail:

- preview mode suppresses HP/UI effects
- strike impulses still run during preview so preview physics better matches committed simulation

This is controlled through `SetPreviewSimulationMode()`, called by `FreezeReplayV2.SetPreviewDamageEnabled()`.

Note: damage cooldown uses `Time.time` which can cause minor divergence between preview and commit because the same collision might register at a different wall-clock moment.

## Editor Setup

Typical scene setup:

1. Create a controller GameObject.
2. Add `FreezeReplayV2`.
3. Add `SmartPoser` to the same GameObject.
4. Optionally add `FRV2Diagnostics` to the same GameObject for telemetry.
5. Assign `FreezeReplayV2.playerPrefab`.
6. Set `FreezeReplayV2.groundMask` to the real ground layer.
7. Optional: add `FighterRuntimeRigBuilder` to the same controller GameObject if you want its inspector values copied into spawned marker rigs.

For marker-only fighters:

- assign a prefab containing the authoring marker points listed above
- the prefab does not need child physics components
- `FreezeReplayV2` will build the generated runtime rig under `_GeneratedRig`

For older prebuilt fighters:

- ensure child bodies have `Rigidbody2D`, `Collider2D`, and `HingeJoint2D`
- ensure hierarchy/order matches between live clone and frozen clone
- avoid old limb motor scripts fighting `FreezeReplayV2`

## Turn And Preview Flow

`FreezeReplayV2` uses `FixedUpdate` for committed capture and preview stepping.

Preview:

1. `StartPosePreviewLoop()` restores the live fighter from `currentSnapshot` and reads the frozen copy as the target pose without mutating it.
2. It begins limb plans and creates planted foot joints.
3. It unfreezes live bodies.
4. Fixed steps run for the same count as a committed turn.
5. It restarts from the snapshot unless the player commits or changes the frozen copy.

Commit:

1. `CommitPose()` stops preview, stabilizes frozen copy, and restores snapshot.
2. It hides the frozen copy and then begins limb plans from the same frozen target.
3. It simulates exactly `turnDuration` using `FixedUpdate`.
4. `UpdateCapturing()` copies live to frozen, captures snapshot, and returns to posing.

Preview-vs-commit alignment:
- `CommitPose()` stabilizes the authored frozen copy before the real turn begins
- Preview restarts do **not** call `StabilizeFrozenCopyPose()` and must not rewrite frozen-copy transforms
- Frozen copy is hidden (colliders disabled) before `BeginLimbPlansForSimulation()` in commit
- The preview loop restores the live fighter from the last captured snapshot, then simulates toward the current authored copy

**Important source-of-truth rule:** the frozen copy is the authored target pose, and the preview loop must be read-only with respect to it. Preview simulation must not copy live transforms back into the frozen copy, must not stabilize/reconcile/lift the copy on loop restart, and must not recapture `currentSnapshot`. `currentSnapshot` is the authoritative simulation start state and should remain unchanged from the last real capture until the next real capture. It is only replaced during initial setup and `UpdateCapturing()` after a committed turn.

If preview and capture still do not match, check:

- whether `ImpactDamage` cooldown timing via `Time.time` differs between paths
- whether some script behaves differently when `disableDamageDuringPreview` is active
- whether runtime collision ignores were reapplied
- whether `turnDuration` and `Time.fixedDeltaTime` produce the expected number of fixed steps

## Frozen Copy Rules

The frozen copy is a planning ghost, not a fully simulated body.

Current intended behavior:

- visible/idle frozen copy is `Kinematic`
- non-root limb drag temporarily makes it `Dynamic`
- root/hip drag is handled kinematically
- preview simulation reads the frozen copy but does not edit it
- after edits, `StabilizeFrozenCopyPose()`:
  - clamps root movement
  - clamps joint limits unless `ignoreJointLimits` is on
  - reconciles hinge anchors so connected limbs stay attached
  - lifts the ghost above the detected floor if `preventFrozenCopyGroundPenetration` is on
  - zeros velocities
- replay completion restores the planning ghost from `currentSnapshot`, not from replay-driven live transforms

If the frozen copy visibly changes during preview playback without direct user posing, treat that as a bug. The only normal writers to frozen-copy transforms are explicit user edits, `ResetFrozenCopy()` from the existing snapshot, initial setup, and `UpdateCapturing()` after a real committed turn.

If the frozen copy goes through the floor:

- check `groundMask`
- check `preventFrozenCopyGroundPenetration`
- check `frozenCopyGroundClearance`
- check `TryGetGroundPoint()` is not hitting fighter colliders instead of ground

## Ideal Simulation Behavior And Current Limitations

This section captures the main unresolved design/engineering issues from recent tuning. Treat these as product goals, not settled implementation facts.

### Frozen Copy Manipulation

Ideal behavior:

- The frozen copy is the sole authored target pose between committed captures.
- Preview simulation is a read-only consumer of that pose. It must never push preview/live results back into the frozen copy.
- `currentSnapshot` contains all information needed to restart preview/commit. It should not change until a real committed capture finishes.
- Dragging hips/root should move the upper body predictably while Auto limbs solve to physically plausible support/contact positions.
- Dragging a limb should affect its chain and nearby logical dependencies, but should not pull the whole fighter off the floor.
- Root/hip lowering should clamp to the closest valid pose instead of fighting the user or lifting planted feet.

Known limitations:

- `StabilizeFrozenCopyPose()` is still a fragile reconciliation pass. It can move bodies after IK has authored them, so Auto leg targets may need to be re-applied after stabilization.
- Ground clamping can overcorrect if the ground probe hits the wrong surface or if non-Auto carried bodies collide with the floor before the leg solver has settled.
- Preview-loop restarts previously influenced the frozen copy. This must remain isolated; if the copy moves when preview starts, treat it as a regression.
- Frozen-copy manipulation is currently a blend of kinematic writes, joint-anchor reconciliation, ground clamping, and IK. These systems can disagree about which body is authoritative.

### Leg Manipulation, Crouching, And Walking

Ideal behavior:

- If at least one foot is planted at snapshot start, lowering or moving the hips should preserve at least one planted support foot on the floor.
- The planted support foot should be authoritative during frozen-copy editing. It should not drift upward or sideways just because the hip is lowered.
- Only one leg should become the active stride/swing leg at a time.
- The stride foot should keep a natural neutral-side bias for small hip edits, then reach confidently in the travel direction for larger committed moves.
- Low hip/crouch edits should prioritize valid vertical hip placement and grounded support over perfect horizontal stride distance.
- Legs should generally bend away from the floor. However, support legs should not be aggressively flipped by knee-clearance logic if doing so destabilizes their planted contact.

Known limitations:

- Auto-walk/crouch is still the most fragile system. Small changes to swing-foot target math can quickly trade confident strides for crossed legs, or natural neutral stance for timid movement.
- The current knee bend direction uses live-body cross products plus additional floor-aware correction. This can be stale or overaggressive, especially for planted support legs.
- The floor-aware bend correction should be conservative: best suited for swing or unsupported legs, less suitable for planted legs.
- `ProjectFrozenRootIntoPlantedLegReach()` keeps hips within reach of planted support, but it is not a full IK constraint solver. It may need better projection/scoring if hips are low and horizontally offset.
- Foot placement scoring was explored and removed/restored several times. Avoid reintroducing a complex scorer unless it has a clear, inspectable rule for preserving planted support and natural bends.

### Jumping And Airborne Behavior

Ideal behavior:

- Grounded posing and airborne/jump posing should feel like different modes.
- In grounded mode, planted feet provide stability and constrain hip/root movement.
- In `forceAirborneMode` or `isJumpNextSimulation`, root movement should skip grounded stride and ground constraints so launch poses can be exaggerated.
- Toggling airborne/launch state must not copy live transforms to the frozen copy, must not overwrite `currentSnapshot`, and must not silently reset the authored pose.
- A committed jump should use the authored frozen copy as intent, then the live body should simulate ballistically with gravity and angular control.

Known limitations:

- Jump trigger behavior currently depends on leg extension and planted-leg detection. If planted feet are unstable, jump detection will also be unstable.
- Airborne tethers and grounded tethers have different goals. Airborne should generally tether limb ends relative to the core; grounded behavior is more editor/preset dependent.
- Launch/airborne mode is intentionally less physically constrained while posing, but the resulting simulation still needs enough joint/motor authority to look intentional.

## Auto-Walk and Auto-Leg IK

Auto-walk is experimental and is one of the more fragile current systems.

### Activation conditions

- `enableAutoWalkFromHipTarget` is true
- at least one planted foot was detected at snapshot start
- frozen hip/root moved horizontally by at least `walkMinHipTravel`
- hip target is within leg range of the floor

### Current Auto-walk behavior

- one foot is selected as the swing foot
- planted foot bodies can get temporary `FixedJoint2D` anchors
- the swing foot's planted joint is released
- the active swing foot starts near its natural side of the body, then blends toward a longer travel-direction landing as hip travel becomes more committed and leg reach allows it
- `ApplyAutoWalkLegAssist()` drives walking
- `useJointMotorsForAutoWalk` rotates Auto thigh/calf/foot joints toward generated targets
- `useJointMotorsForAutoWalk` rotates Auto thigh/calf/foot joints toward generated targets
- `reduceAutoWalkLimbForcesWhenUsingMotors` reduces direct forces on limb segments so joints do more of the work

### Limb IK (GetAutoMidJointTarget / GetAutoLimbEndTarget)

Generalized 2-bone IK for both legs and arms:
- **Mid-joint placement**: IK height = `âˆš(halfLimbÂ² - halfDistÂ²)` applied perpendicular to the root->end line.
- **Bend Direction**: Dynamically determined by `GetLimbBendDirection()` using the cross-product of the live limb's current hip-knee-foot or shoulder-elbow-hand triangle. This preserves anatomical bend (e.g., knees-out, elbows-out).
- **Stride Control**: Foot targets track relative to `rootTarget.x` with a `walkMaxStrideDistance` limit. The single swing foot keeps a neutral-side bias for smaller edits, then reaches farther in the travel direction as the hip move becomes more committed; available reach still shrinks naturally when the hips are low.
- **Floor-aware knee bend**: swing or otherwise unsupported leg IK compares the preferred knee bend with its mirrored alternative and keeps the candidate with better ground clearance when the original bend would fold toward the floor. Planted support legs keep their existing bend direction so contact posing stays stable.
- **Hand Planting**: Hands in Auto mode will "plant" if they are near the ground when posing begins, identical to foot planting.
- Small lateral offsets and lift are applied to swing limbs during walking.

If limbs snap or bend backward, check `GetLimbBendDirection()` and `GetAutoMidJointTarget()`.

### Troubleshooting

If calves rotate around knees while thighs do not move:

- verify thigh bodies are named `Thigh` / `Thigh 2`
- verify thigh `HingeJoint2D.connectedBody` is hips
- verify `useJointMotorsForAutoWalk` is enabled
- verify `poseKp`, `poseKd`, and `poseMaxSpeed` are not too weak
- verify the thigh joint motor torque from `FighterRuntimeRigBuilder.defaultMotorTorque` is not too low
- check `ignoreJointLimits`: limits may prevent desired thigh rotation

## Planted Foot System

### Current behavior

- Detected at the start of a preview/turn from `currentSnapshot` by checking which feet are within `plantedFootGroundTolerance` of the ground
- Get temporary `FixedJoint2D` world anchors when planted
- The selected swing foot releases its planted joint during walking
- Joints break when force/torque exceed `plantedFootJointBreakForce` / `plantedFootJointBreakTorque`

### Intent-Based Foot Detachment

At the start of each simulation/preview, `PreReleasePlantedFeetByFrozenTarget()` compares each planted foot's anchor position (from `CachePlantedFootTargets`) to the frozen copy's current foot position. If the frozen foot has moved beyond `plantedFootDetachThreshold`, the foot is removed from `plantedFootTargets` before `CreatePlantedFootJoints()` runs -- so no FixedJoint2D is created for it, and the foot is free to chase its new target.

- **`plantedFootDetachThreshold`** (default: 0.4): distance threshold for intent-based release. Set to 0 to disable.
- Called from `BeginLimbPlansForSimulation()` between `CachePlantedFootTargets()` and `CreatePlantedFootJoints()`
- Works for both preview and committed simulation paths
- Logged only when `logFootReplantingDetails` is enabled
- Included in the preset system (`FRV2Preset`)

### Airborne Physics & Posing

Airborne behavior differs significantly from grounded posing:
- **LAUNCH READY Posing**: When `isJumpNextSimulation` is active, the frozen copy stays grounded with at least one foot planted. The user authors the launch direction by moving the hips -- the vertical height limit is relaxed to `walkMaxHipGroundDistance * 1.15` to allow full leg extension. The live fighter preview actually fires the jump trigger to show the launch trajectory.
- **Already Airborne**: When `forceAirborneMode` is active (after a committed launch), the frozen copy root skips all stride and ground constraints.
- **Local-Space Tethers**: Limbs are leashed relative to the core (`connectedBody = rootRb`) using `DistanceJoint2D`. This allows the character to fall naturally under gravity while limbs rigidly track their relative pose offsets.
- **Core Rotation**: Root rotation is driven by a boosted `airborneAngularSpringMultiplier` (default: 10x) for "rigid" angular control without position-tethering the hips to world-space.
- **Root Rotation Compensation**: `PoseDriver.airborneRootRotationOffset` compensates for the rotation delta between live and frozen roots. FRV2 computes `DeltaAngle(frozenRoot.rotation, liveRoot.rotation)` and injects it into PoseDrivers whose joint is connected to the root body. This makes thighs and spine maintain world-space orientation instead of drifting with rotating hips.
- **Coherent Launch Impulse**: `CheckJumpTrigger()` applies impulse to ALL bodies (root at 100%, limbs at 70%) for body coherence during launch. Impulse scales by leg extension ratio for predictable jump height.
- **Mode Toggle Isolation**: Toggling Airborne/Launch Ready restarts preview from the existing authored copy and captured snapshot. It does not call `CopyLiveToFrozen()` and does not replace `currentSnapshot`.

Feet can still break free via FixedJoint2D break force/torque during simulation if needed.

### Spine-to-Hips Weakness

The HingeJoint2D connecting the spine to the hips can be weak, causing the torso to slouch. This is a known issue with the angle-driven preset.

Potential fixes:
- Increase PoseDriver `poseKp` specifically for the spine joint (requires per-joint tuning, not currently supported by global FRV2 settings)
- Increase the spine joint's `maxMotorTorque` in `FighterRuntimeRigBuilder`
- Apply a small amount of angular pose assist specifically to spine/torso bodies

## Preset System

### File locations

- Presets: `[ProjectRoot]/FRV2Presets/*.json`
- Diagnostics reports: `[ProjectRoot]/FRV2Diagnostics/*.json`

### Current active preset: `angle_driven_minimal_force`

Philosophy: **angle-primary with minimal root support**.

Key settings:
- `ignoreJointLimits: true` -- joint limits off for now
- `poseKp: 55, poseKd: 14` -- moderate PD gains
- `enablePhysicsPoseAssist: true` but `poseAssistOnlyRootAndUnjointedBodies: true` -- **only root/hips get translational force**; all jointed limbs are purely motor-driven
- `usePoseDriversForAutoLegs: true` -- legs use PoseDriver motors
- `useJointMotorsForAutoWalk: true` -- walking via joint motors
- All walk/leg force values zeroed -- no force-driving of limbs
- `enablePlantedFootStability: true, usePlantedFootJoints: true` -- FixedJoint2D for planted feet
- Planted foot spring fallback disabled

### How to use presets

1. Play a few turns with FRV2Diagnostics attached
2. Press F8 for report + auto-generated optimized preset, or F9 for preset only
3. Open Presets section in FRV2 inspector -> select from dropdown -> Load
4. Set `autoLoadPresetName` to auto-apply on Start

## Component Relationships (Same GameObject)

All three of these live on the **same controller GameObject**:

| Component | Purpose | Settings overlap? |
|---|---|---|
| `FreezeReplayV2` | Turn loop, simulation, assist systems, preset loading | Master -- its poseKp/Kd/maxSpeed override all PoseDrivers |
| `SmartPoser` | Frozen copy drag-to-pose UI | **No overlap** -- dragForce/frequency are for the TargetJoint2D drag spring, not simulation |
| `PoseDriverSetup` | Utility for wiring PoseDrivers | **Mostly unused** -- FRV2.WirePoseDrivers() handles this directly |

PoseDriver instances live on **each limb body of the live fighter**, not on the controller. FRV2 creates/wires them during setup.

## Low-Force / Stability Notes

The project has moved toward angle-driven control with minimal force.

Current defaults (angle-driven preset):

- `poseAssistOnlyRootAndUnjointedBodies = true` -- force only on root
- `useForceLegAssistWhenNotWalking = false`
- `usePlantedFootJoints = true`
- `usePlantedFootSpringFallback = false`
- `driveAutoLegTargetsOnlyDuringWalk = true`
- `usePoseDriversForAutoLegs = true`
- `useJointMotorsForAutoWalk = true`

If feet stick to the floor:

- check `autoWalkActive`
- check `autoWalkSwingFoot`
- check `ReleasePlantedFootJoint()`
- check `plantedFootGroundTolerance`
- check whether the actual foot body is named `Foot` / `Foot 2`; if no foot exists, calves are treated as contact legs
- **check if frozen foot has moved** -- may need smarter detachment logic (see Planted Foot section)

## Combat / Limb UI

The move-planning UI is immediate-mode GUI drawn by `FreezeReplayV2.OnGUI()`.

It supports:

- minimization
- Auto toggle for legs
- Basic/Block
- Strike
- recovery display
- angle delta bar between live joint and frozen target joint

Combat intent is stored on `LimbMoveIntent`.

Strike effects:

- velocity multiplier for PoseDrivers
- outgoing damage multiplier
- outgoing impulse
- recovery for `strikeRecoverySnapshots`

## Legacy Scripts

This project contains many older experimental scripts, including:

- `LeftThighScript.cs`
- `RightThighScript.cs`
- `RotateHingeJoint.cs`
- `PoseDriverSetup.cs`
- older `FreezeReplay.cs`
- balance/boot/agent scripts

`FreezeReplayV2.disableLegacyMotorControllers` tries to disable older scripts that may write hinge motors. Before re-enabling legacy scripts, inspect whether they conflict with `FreezeReplayV2`, `PoseDriver`, or the runtime rig builder.

In general, prefer improving the current `FreezeReplayV2` + marker rig path over reviving older motor scripts.

## Design Priorities (User Direction -- May 2026)

### Vision

The game should feel like a mix of **Your Only Move Is Hustle** and **Toribash**, but unique. An anime-like fighting game where the fighter looks as alive as possible while having fun with the physics. The end result: a punch lands, the victim's body tumbles along the floor -- that Dragon Ball Z appeal.

### User Answers To Architecture Questions

1. **Single or dual fighter?** Single fighter is the primary focus right now. Getting the frozen copy -> simulated fighter control to feel intuitive is the top priority. Would like an option to test two fighters eventually (for hit response testing), and controlling two fighters within a single FRV2 instance would be ideal. But not until single-fighter control is smooth.

2. **Strikes aren't visually faster.** Strike logic applies a velocity multiplier and forces, but the visual result doesn't show a faster strike. The PoseDriver joint-motor system (which provides the most concise, jitter-free motion) likely overrides or absorbs the strike speed boost before it becomes visible. This is a critical issue -- strikes MUST look distinct from regular movement.

3. **Preset philosophy.** `tether_jumping` is the current favorite. The auto-leg logic works decently for upright horizontal movement. Problems appear when hips are lowered: legs get unnaturally positioned, or hips refuse to update to positions that should be reachable (even if the system can only get close while honoring 1-planted-foot). New presets are welcome when new mechanics are added -- there's no proven approach since this game design is novel. An "alive_fighter" preset is welcome.

4. **Walking and jumping are the current focus.** Single-player walking and jumping need to work well. The fighter should maintain angle relations and body form while airborne (local-space coherence). Jump behavior needs work: physics should handle airborne trajectory, but the player still needs enough limb control for karate-kicks and similar poses. **Airborne thigh update failure is a known critical bug**: thighs fail to update because they're connected to a non-stationary base (hips falling under gravity), and the IK/motor system doesn't adapt.

5. **Replay export is far away.** Not a current concern.

### Current Development Focus (Ordered Priority)

1. **Smooth single-fighter control** -- the pose -> preview -> commit loop must feel intuitive
2. **Functional walking** -- upright is okay, crouching and lowered hips need work
3. **Functional jumping and airborne control** -- physics trajectory with limb authority
4. **Strike visual distinctness** -- strikes must look faster and more forceful than basic movement
5. **Dual-fighter option** -- for testing hit response (later)
6. **Hit reactions and ragdoll** -- dependent on #5
7. **Replay cinematics** -- later

### Critical Active Issues

10. **Strikes are not visually faster than basic movement**: `PoseDriver` motors control joint speed via PD gains and `maxMotorSpeed`. Strike `runtimeSpeedMultiplier` increases the PD output, but since the frozen target pose is the same as a basic move, the motors are chasing the same angle. The multiplier makes the motor *stiffer*, not *faster through a larger arc*. For strikes to look fast, the strike limb needs to either (a) temporarily disable/weaken its PoseDriver so external forces dominate, or (b) be driven toward an intermediate "wind-up then snap" pose sequence, or (c) have its PoseDriver chase a dynamically interpolated target that leads the frozen pose.

11. **Airborne thigh update failure**: When the fighter is airborne, the hips (root body) fall under gravity. Thigh `HingeJoint2D` components are connected to the hips. `PoseDriver` drives `jointAngle`, which is the angle relative to the `connectedBody` (hips). Since the hips are rotating during freefall, `jointAngle` is measured in a rotating frame. If the hips rotate 30 degrees, a thigh at `jointAngle = 0 degrees` also rotates 30 degrees in world space -- but the frozen copy's hips didn't rotate, so the frozen `jointAngle` is stale. Additionally, Auto-leg IK generates world-space targets and converts them to rotations, but the world-to-joint-angle conversion doesn't account for the live root's current rotation during freefall. The airborne angular spring multiplier on the root tries to keep hips upright, but if it loses that battle, all downstream joints drift.

## Quick Debug Checklist

When another model receives a bug report, start with these checks:

1. Is the bug about planning ghost editing, preview simulation, committed simulation, or replay playback?
2. Is the fighter marker-built by `FighterRuntimeRigBuilder` or an older prebuilt hierarchy?
3. Is `groundMask` correct?
4. Is `ignoreJointLimits` enabled?
5. Are legs in `Auto`, `BasicBlock`, `Strike`, or `Recovery`?
6. Are old motor scripts enabled and fighting FRV2?
7. Are planted foot joints being created/released as expected?
8. Does the preview path differ from commit through damage, strike impulse, or fixed-step count?
9. Are PoseDriver settings relevant to this limb, or is Auto-walk bypassing normal PoseDrivers?
10. **Which preset is loaded?** Check `autoLoadPresetName` and compare current inspector values to the preset file.
11. **Is FRV2Diagnostics attached?** Check its reports for tracking error data.
12. **Is the fighter airborne?** Check if thigh `jointAngle` targets are stale relative to live root rotation.
13. **Is a strike visually distinct?** Check if `runtimeSpeedMultiplier` is actually creating visible speed difference.

## Most Relevant Files

- `FreezeReplayV2.cs`: primary controller and active gameplay path
- `Editor/FreezeReplayV2Editor.cs`: compact inspector for FRV2 with preset UI
- `FRV2Preset.cs`: preset save/load system
- `FRV2Diagnostics.cs`: runtime telemetry and auto-tuning
- `FighterRuntimeRigBuilder.cs`: marker-only fighter builder
- `SmartPoser.cs`: frozen copy drag/edit interaction (chain-isolated)
- `PoseDriver.cs`: hinge motor PD driver (on live limb bodies)
- `LimbMoveIntent.cs`: per-limb Auto/Basic/Strike/Recovery state
- `ImpactDamage.cs`: collision damage and strike impulse
- `FighterHealth.cs`: health snapshots and health bar
