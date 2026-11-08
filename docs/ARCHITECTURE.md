# Vectorier Architecture

This document describes how Vectorier's source code is organized and where to find the code responsible for different parts of the editor.

For an introduction to Vectorier and installation instructions, see the [README](../README.md).

## Projects

| Project | Description |
|---|---|
| `Vectorier/` | The main project. Contains the editor, shared code, UI, services, models, and integrated game (`Vectorier.Core.dll`). |
| `Vectorier.Desktop/` | Desktop entry point for Windows, Linux, and macOS. Handles packaging and copying the `Data` folder alongside the application. |
| `Vectorier.Browser/` | WebAssembly entry point. Handles browser-specific file access and audio output. |
| `Vectorier.External/` | Standalone tools that are not part of the editor itself. |

## Main project structure

Most of the editor's functionality lives in `Vectorier/`. The main folders are organized by responsibility.

| Folder | Description |
|---|---|
| `Models/Scene/` | Scene and element data. `SceneDocument` represents the level, while `SceneElement` and the classes in `Elements/` represent individual elements. These models are the source of truth while editing. XML is only used when loading and saving. |
| `Services/Xml/` | Handles conversion between scene data and game XML. `SceneXmlReader` and `SceneXmlWriter` handle loading and saving, while `ObjectLibrary` resolves `<Sets>` using the game's rules. |
| `Services/Assets/` | Loads and manages textures, thumbnails, sprite sheets, models, platform surfaces, and paint canvases. |
| `Services/Projects/` | Handles project folders, project data such as locations, shops, and localization, as well as the `.vectorier` editor cache. |
| `Services/IO/` | Provides the `IFileSystem` abstraction for file access. Desktop builds use native folders, while the browser uses picked folders. Use `VPath` instead of platform-specific paths. |
| `Services/Platform/` | Contains platform-specific functionality such as settings, audio output, and sound previews. Access these through `IPlatformServices`. |
| `Services/Game/` | Contains `GameHost`, which runs the integrated game inside the editor for Play, Hunter Mode, and Play Here. |
| `Services/Collab/` | Live collaboration functionality. |
| `Triggers/` | Trigger-related functionality. |
| `Tutorial/` | Tutorial system. |
| `MoveVisualizer/` | Move visualization tools. |
| `Localization/` | Localization and language support. |
| `ViewModels/` and `Views/` | The editor's MVVM layer, built with CommunityToolkit.Mvvm. `ViewLocator` connects each `FooViewModel` to its corresponding `FooView`. |
| `Controls/Scene/` | Scene View implementation. `SceneView.*.cs` handles input and editing tools; `SceneRenderer.*.cs` handles rendering, including the spatial grid, culling, labels, ghosts, and selection. |
| `Gameplay/` | The integrated game implementation (Vector Raylib). See [Gameplay/README.md](../Vectorier/Gameplay/README.md). |

Large classes are split into partial files to keep related functionality together. Files follow the `Class.Concern.cs` naming convention, such as `EditorViewModel.Selection.cs` and `SceneRenderer.HitTest.cs`.

## Working with scene elements

Scene elements generally follow the same structure. Using **Spawn** as an example, these are the main places to look when modifying or adding functionality:

| Responsibility | Location |
|---|---|
| Element data, properties, copying, and resize rules | `Models/Scene/Elements/SpawnElement.cs` |
| Element type definition | `ElementType` in `Models/Scene/SceneElement.cs` |
| XML loading | `SceneXmlReader.ReadSpawn` |
| XML saving | `SceneXmlWriter.WriteSpawn` |
| Scene View rendering | `SceneRenderer.DrawOverlay` for overlays; `SceneRenderer.DrawVisual` for images, models, and animations |
| Inspector UI | `DataTemplate DataType="models:SpawnElement"` in `Views/Editor/InspectorView.axaml`; related logic is in `InspectorViewModel` |
| Element Bar entry, icon, and ordering | `ElementTypeInfo.MenuOrder` and `GetIcon`, along with the Element menu in `EditorView.axaml` |
| Element creation | `EditorViewModel.Edit.cs`, under `case ElementType.Spawn` |
| In-game behavior | `Gameplay/Runners/SpawnRunner.cs`, registered through `Gameplay/Runners/Elements.cs` |

### Adding a new element type

When introducing a new scene element, update each part of the editor that needs to recognize and handle it.

1. **Create the element model.** Add the new value to `ElementType` and create a class under `Models/Scene/Elements/`. Implement `Type`, `CreateEmpty`, `CopyTo`, and any element-specific transform rules.
2. **Add XML support.** Implement reading and writing in `SceneXmlReader` and `SceneXmlWriter`. Only write attributes supported by the game.
3. **Add rendering and Inspector support.** Handle the element in `SceneRenderer` and provide its Inspector `DataTemplate`.
4. **Add it to the UI.** Register it in `ElementTypeInfo.MenuOrder`, provide an icon, and add its creation logic in `EditorViewModel.Edit.cs`.
5. **Add gameplay support if needed.** If the element should have an effect in-game, implement its runner under `Gameplay/Runners/`.

## Coding conventions

- Follow the rules in `.editorconfig`. Run `dotnet format` before committing.
- Use file-scoped namespaces and `_camelCase` for private fields.
- Keep one public type per file, except for small helper types closely related to the main type.
- Comments should explain why something is done or clarify behavior that is not obvious from the code. Avoid comments that simply repeat the implementation.
- Do not leave commented-out code in the source. Git history should be used for preserving old implementations.
- Shared code must remain compatible with the browser build. Avoid `System.IO` paths and process launching outside `Services/Platform` and `Services/IO`.
- Keep the Scene View rendering path efficient. It runs every frame, so avoid unnecessary allocations such as LINQ queries, newly created brushes or pens, and uncached text layouts.