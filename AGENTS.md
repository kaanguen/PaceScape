# PaceScape

Unity/C# project for AR running training, currently in its initial setup stage. Read [README.md](README.md) for the product overview. All paths below are relative to the repository root.

## Project context and layout

- `PaceScape/` is the Unity project; the repository root contains documentation and Git configuration. Open the Unity project subfolder, not the repository root.
- `PaceScape/ProjectSettings/ProjectVersion.txt` is the source of truth for the Editor version (currently `2022.3.62f3`). Keep code and assets compatible with it.
- `PaceScape/Packages/manifest.json` declares dependencies; `packages-lock.json` records resolved ones. Change dependencies through Unity Package Manager and include the resulting lockfile changes.
- `PaceScape/Assets/Scenes/SampleScene.unity` is the existing scene. There are currently no custom C# scripts or project-specific tests. URP, OpenXR, the Magic Leap SDK, BLE, and RunningServer integration are not configured yet.
- MarkerML2 is a separate, earlier project of the user. Do not assume its tools, packages, or features exist here; treat it as a reference only if the user points to it. <!-- TODO: add one sentence on what MarkerML2 is. -->
- Place runtime scripts in the matching feature folder under `PaceScape/Assets/Scripts/`, editor tools in `PaceScape/Assets/Editor/`. Create these folders when needed.

## Language

- Talk to the user in German. Write code, identifiers, code comments, commit messages, and this file in English.

## Working scope and collaboration

- The user leads the conceptual work and makes scientific and methodological decisions. Implement individual code sections within the agreed scope.
- Research and explain relevant knowledge and options. Distinguish evidence from assumptions and state uncertainties.
- Examine concepts critically for scientific soundness. Point out overlooked assumptions and missing information. Do not silently choose scientific methods, training parameters, or evaluation criteria.
- Avoid unrelated package upgrades, restructuring, and speculative infrastructure. Keep this file aligned with the repository and update it when structure or workflows change.

## Git

- Before editing, run `git status --short` and inspect relevant changes with `git diff -- <path>`. Preserve the user's uncommitted changes.
- Do not commit, push, create branches, or rewrite history unless the user asks. When asked to commit, use short imperative English messages (e.g. `Add pace display prototype`).
- Before reporting completion, run `git diff --check` and review `git diff -- <changed-paths>` for unintended edits.

## Unity assets and generated files

- Move or rename assets through Unity, or move them together with their `.meta` files. Include `.meta` files for all new assets and folders. Preserve existing GUIDs and references.
- Do not hand-edit `.unity`, `.prefab`, or other serialized YAML assets unless the user agrees for that specific change. Prefer a small Editor script or step-by-step instructions for the user to perform in the Editor.
- Follow `.gitattributes` and `.gitignore`. Track binary assets with Git LFS according to the existing rules; keep scripts, text-serialized Unity assets, and `.meta` files in regular Git.
- Do not edit or commit `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, generated solution/project files, local IDE files, or builds. Do not edit `Library/PackageCache/`.

## C# coding guidelines

Style follows the [Unity C# style guide (Unity 6 edition)](https://unity.com/resources/c-sharp-style-guide-unity-6), summarized here so it does not need to be fetched each time. `.editorconfig` in the repository root enforces formatting and naming where possible; if it conflicts with this section, `.editorconfig` wins.

- Formatting: 4 spaces, Allman braces (opening brace on its own line), always use braces for `if`/`for`/`foreach`, one statement per line.
- Naming: `PascalCase` for types, methods, properties, events, enums, and public members; `camelCase` for locals and parameters; `_camelCase` for private fields; `I` prefix for interfaces. Use descriptive names; booleans read as questions (`isRunning`, `hasTarget`).
- Inspector fields: `[SerializeField] private` instead of public fields. Renaming a serialized field loses its scene/prefab value; use `[FormerlySerializedAs]` or ask first.
- Comments explain why, not what. Add XML summaries for public APIs.
- Keep `Find*`, `GetComponent`, and allocations out of `Update`; cache references in `Awake`/`Start`.
- Keep code compatible with the Editor version in `ProjectVersion.txt`. Do not introduce unsupported APIs or language features to follow an example.
- Where the guide offers alternatives, use the convention already established in the project code. Do not reformat unrelated code or rename serialized fields, assets, or public APIs solely for style.
- Mention any necessary style deviation in the completion report.

## Verification

- The agent usually cannot run the Unity Editor. After C# changes, ask the user to let Unity compile and to paste Console errors or warnings. Do not claim the code compiles before the user confirms it.
- Run relevant existing tests through Unity's Test Runner when available; an empty test suite is not behavior verification.
- Scenes, UI, placement, and animations must be verified in the Editor. Hardware-related behavior must be verified on the target device.
- Command-line test workflows and device build tools are not configured yet. Do not invent commands or claim checks that were not performed. If Unity or hardware is unavailable, report the pending checks.
- Changes to C# code, scenes, or bundled assets require a new device build to take effect on an installed app. Editor verification does not verify installed-device behavior.
- Documentation-only changes need a content and diff review; no compile or device build is required.

## Completion report

- Small changes: 2 to 3 sentences covering what changed, which files, and what was not verified.
- Larger changes: state what changed and which files; checks actually performed and their results; checks not performed (Unity compilation, tests, device verification); material assumptions, style deviations, and limitations; and whether a new device build is needed.
