# PaceScape — repository instructions

## Project and stack

PaceScape is a Unity/C# project for AR running training, currently at the starter-scene stage. See [README.md](README.md) for the planned experience and setup.

Use the Editor version recorded in `PaceScape/ProjectSettings/ProjectVersion.txt` (currently `2022.3.62f3`). Check `PaceScape/Packages/manifest.json` before relying on a package. AR, treadmill, sensor, and dashboard integrations are not implemented yet; do not assume tools from MarkerML2 exist here.

## Repository map

Paths in this file are relative to the repository root. **Open `PaceScape/` as the Unity project.**

| Path | Responsibility |
| --- | --- |
| `PaceScape/Assets/Scenes/` | Unity scenes; currently `SampleScene.unity` |
| `PaceScape/Assets/Scripts/` | New runtime C# code, organized by feature; create when needed |
| `PaceScape/Assets/Editor/` | New Editor-only tools; create when needed |
| `PaceScape/Packages/` | Package manifest and resolved dependency lockfile |
| `PaceScape/ProjectSettings/` | Shared Editor, serialization, and build configuration |
| `.gitattributes`, `.gitignore` | LFS tracking and generated-file exclusions |
| `README.md`, `DISCLAIMER.md` | Human-facing setup and testing disclaimer |

## Setup and commands

Run Git commands from the repository root.

| Purpose | Command or action |
| --- | --- |
| Initialize LFS after cloning | `git lfs install` |
| Retrieve binary assets | `git lfs pull` |
| Resolve Unity dependencies | Open `PaceScape/` in the specified Editor and let Package Manager finish importing |
| Run the starter scene | Open `Assets/Scenes/SampleScene.unity` in Unity and enter Play Mode |
| Check tracked changes for whitespace errors | `git diff --check` |
| Review pending binary asset changes | `git lfs status` |
| Inspect an asset's LFS rule | `git check-attr filter -- <asset-path>` (replace the placeholder with its repository-relative path) |

No project-specific automated test command or device build workflow is configured. The Build Settings scene list is empty. Do not invent test results or reuse build commands from another repository.

## Implementation rules

- Inspect nearby code and settings before editing. Reuse existing implementations before introducing another abstraction.
- Put training calculations in plain C# where practical; use Unity components to connect them to scenes and hardware. Keep Editor-only APIs out of runtime code.
- Match component class names to script filenames. Follow naming and formatting conventions in the files being changed.
- Keep changes within the requested scope. Explain any necessary new dependency; avoid unrelated package upgrades or restructuring. Let Unity resolve the lockfile when dependencies change.
- The user owns conceptual, scientific, and methodological decisions; identify consequential assumptions instead of silently choosing a training method.

## Preservation and Git boundaries

- Preserve existing user edits. Do not reset unrelated work or rewrite history without an explicit request.
- Move assets through Unity or move their `.meta` files with them. Retain existing GUIDs and serialized references.
- Preserve `Force Text` serialization and `Visible Meta Files`. Keep text-serialized scenes, prefabs, assets, and `.meta` files in regular Git. Apply the existing LFS rules to binary assets.
- Do not manually edit or commit generated `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, local IDE state, or build output. These folders are inside the Unity project; consult the root `.gitignore`.
- Preserve third-party license and attribution files. `DISCLAIMER.md` addresses testing liability and does not grant a software license.

## Verification by change type

- **Every change:** inspect the final diff, run `git diff --check`, and review new files separately because that command does not cover untracked files.
- **Binary assets:** check `git lfs status` and the asset's filter attribute. A matching binary asset should report `filter: lfs`; check its import and references in Unity when relevant.
- **C# or dependencies:** let Unity finish importing and compiling, then inspect the Console. If relevant tests exist, run them and report the actual results. Add behavior-focused checks for nontrivial logic where a suitable test setup exists.
- **Scenes, UI, animations, or interactions:** verify the affected behavior in Play Mode. Hardware-dependent behavior also requires target-device validation once that integration exists.
- **Installed device apps:** rebuild after changing C#, scenes, or bundled assets. Editor validation alone does not validate a device build.
- **Documentation only:** check links, paths, and formatting; launching Unity is unnecessary.

## Security and data

- Keep credentials, access tokens, private keys, and machine-specific secrets out of versioned files and logs.
- Keep identifiable participant, session, and sensor recordings out of Git. Use synthetic or anonymized fixtures; do not transmit private recordings to external services without explicit authorization.
- Validate file, network, and device input at integration boundaries. Do not add telemetry, external data transmission, or device permissions without an agreed requirement.

## Completion and maintenance

Report the outcome, affected files, verification results, and any checks that could not run with their reasons. Distinguish implemented features from plans, Editor checks from device checks, and local edits from commits or pushes.

Update the README when setup or usage changes. Update this file when commands or repository boundaries change. Keep temporary task requirements out of it; add nested `AGENTS.md` files only where a subproject needs different instructions.

## Our Roles

- The user leads the conceptual work and makes scientific and methodological decisions.
- The agent implements individual code sections within the agreed scope.
- The agent researches and explains relevant knowledge and options to support the user's thinking.
- The agent critically examines the user's concepts and decisions for scientific soundness and explains concerns and uncertainties.
- The agent points out overlooked assumptions, missing information, and relevant considerations the user may have forgotten.
