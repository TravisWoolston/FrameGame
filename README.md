# Frame Game

A physics-driven stick-figure fighter where the fight *is* the animation. Inspired by
*Your Only Move Is HUSTLE* and *Toribash*: time freezes, you pose your fighter joint by
joint, then the physics simulation plays the moment out. Every frozen pose and every
simulated step becomes part of a replayable animation you create and play at the same time.

A personal indie project by Travis Woolston (KratumGames).

## Core loop

1. **Freeze** – the simulation pauses.
2. **Pose** – set joint targets on your ragdoll fighter.
3. **Simulate** – physics resolves the move (hits, falls, momentum).
4. **Replay** – the whole sequence plays back as a continuous animation.

Key systems live in `Assets/Scripts/`: `FreezeReplayV2` (freeze/pose/sim/replay loop),
`PoseDriver` (drives joints toward posed targets), `FighterRuntimeRigBuilder` (builds the
2D ragdoll rig at runtime), plus `GameStateReplay`, `FighterHealth`, and `ImpactDamage`.

## Tech

- Unity **6000.3.10f1**, Universal Render Pipeline (2D)
- C#, Unity 2D physics (hinge joints, rigidbodies)

## Getting started

1. Install Unity 6000.3.10f1 via Unity Hub.
2. Clone this repo and open the folder with **Unity Hub → Add project from disk**.
3. Open the main scene: `Assets/frameGameTestZone.unity`.
4. Press Play.

## Roadmap

- Walking and crouching
- Jumping
- Clearer strike feedback and intent
- Dual-fighter (two posed fighters) matches
- Hit reactions
- Replay export (save/share the animation)

## License

MIT – see [LICENSE](LICENSE). Third-party Unity packages remain under their own licenses.
