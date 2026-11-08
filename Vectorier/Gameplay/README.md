# Gameplay

The integrated game implementation used by the editor's **Play**, **Hunter Mode**, and **Play Here** features.

The goal is to build on the original Vector gameplay, not to reproduce it exactly.

## Running inside the editor

The game runs directly inside the editor's Game View rather than as a separate executable.

The integration works as follows:

- **GameHost:** `Services/Game/GameHost` loads the active project into `GameFiles`, an in-memory file store. The game reads from this store instead of accessing files on disk.
- **Rendering and audio:** `Rl/` provides a replacement for Raylib-cs. Drawing calls are handled by Avalonia and rendered into the Game View's `DrawingContext`. Audio is handled by the lightweight mixer in `Rl/Mixer`, which sends output through the active platform's audio system.
- **Game loop:** `VectorGame.Frame` is called once per Game View frame. Game logic uses a fixed timestep of 60 ticks per second, tracked through `VectorGame.TicksThisFrame`, independently of the display's refresh rate.

## Source structure

| Folder / Class | Responsibility |
|---|---|
| `VectorGame`, `LevelMainController` | Main game loop, debug controls, overlays, and level start, restart, and end handling. |
| `Level/` | Level data. `Location` contains the models in the level, while `Sets` contains the runners loaded from level XML. |
| `Runners/` | Implements the behavior of individual level elements. Generally, each XML element type has its own runner, including platforms, trapezoids, areas, triggers, images, items, and spawns. |
| `Models/`, `Node/` | Model and node structures. `ModelHuman` represents the models controlled by runners, while `ModelObject` represents their nodes. |
| `Controllers/` | Handles model behavior, including animations (moves), physics, collisions, platforms, catching, and input. |
| `Animation/` | Loads and handles `moves.xml` and binary `.bin` animation data, including moves, intervals, events, and reactions. |
| `Trigger/` | Trigger scripting system. Contains events (`TE_`), conditions (`TC_`), actions (`TA_`), and functions (`TF_`). |
| `Transformation/` | Handles dynamic object transformations such as movement, scaling, rotation, and color changes. |
| `Rl/` | Raylib compatibility layer for rendering and audio inside the editor. |

## Testing gameplay changes

The game uses a fixed timestep and random seed, allowing gameplay behavior to be tested deterministically.

For regression testing, run a level without rendering (headless) for a fixed number of ticks, then compare the results before and after the change.

Useful values to compare include:

- Each model's animation and current frame.
- The position of every node.
- Any other relevant gameplay state affected by the change.

This makes it easier to identify unintended behavior changes without relying entirely on visual testing.