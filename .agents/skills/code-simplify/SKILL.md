---
name: code-simplify
description: >
  Review the current diff in this repo for reuse, simplification, efficiency and altitude cleanups, then apply the
  fixes and verify them. Tuned for both halves of the codebase: the `asset-studio/` Python pipeline (procedural
  meshes, recipes, shading, export) and the Godot 4.7 C#/GDScript game. Runs four review subagents in parallel.
  Quality only; it does not hunt for correctness bugs.
---

# Simplify for Rogue Gauntlet

A repo-tuned version of the generic simplify flow. Four independent reviewers each take one angle. Then a single pass
applies the findings that survive and proves the cleanup changed nothing it shouldn't have.

Optional argument: a branch, commit range, PR number or path to review instead of the current diff.

## Phase 0 — Gather the scope

1. Diff against the base: `git diff main...HEAD`, or the given target. If the working tree is dirty or the range is
   empty, include `git diff HEAD` as well. New files never show up in a diff: list them with
   `git ls-files --others --exclude-standard` and read them whole.
2. Leave out generated and binary output: `*.glb *.gltf *.png *.gif *.import *.uid *.blend *.npz`,
   `asset-studio/catalogs/**`, binaries under `assets/`, `.godot/` and `gdunit4_testadapter_v5/`. Keep the recipes
   (`character.json`, `costume.json`, `item.json`): they are code in data form.
3. Write the filtered diff to a scratch file outside the repo (the session scratchpad, or `mktemp -d`). Sort the
   changed files into domains:

| Domain | Paths | Read first |
| --- | --- | --- |
| Studio Python | `asset-studio/**/*.py` | `asset-studio/README.md`, `.agents/skills/assets/SKILL.md` |
| Studio recipes | `asset-studio/{characters,costumes,objects}/**/*.json` | same as Studio Python |
| Godot C# | `scenes/**/*.cs`, `scripts/**/*.cs`, `common/**/*.cs`, `tests/**/*.cs` | `AGENTS.md`, `ARCHITECTURE.md`, `.agents/skills/godot-csharp/SKILL.md` |
| Godot data | `*.tscn`, `*.tres`, `project.godot` | `.agents/skills/godot-mcp/SKILL.md` |
| GDScript tools | `asset-studio/godot/*.gd`, `.agents/skills/**/*.gd` | `.agents/skills/godot-mcp/SKILL.md` |

When only one half of the repo changed, leave the other half's checklists out of the briefs.

## Phase 1 — Review (four agents in parallel)

Launch four review subagents in a single message so they run concurrently. In Claude Code, that's four `Agent`
calls in one turn. A harness without subagents runs the four passes itself, one after another, starting each from a
fresh read of the scope.

Each brief contains:
- the scope file's path and the domain table;
- its angle section below, copied verbatim, with the checklists for the domains in scope;
- these rules:
  - Quality only. If you find a real bug, list it under "out of scope" in one line and don't chase it.
  - Read the changed files whole, not just the hunks, plus the "read first" docs for their domain.
  - Every finding names its evidence: the existing helper's `file:line`, the duplicated block, or the hot path.
  - Return a list of findings with these fields:
    - `file`, `line`, `angle`;
    - `summary`: one line;
    - `cost`: what is duplicated, wasted or harder to maintain;
    - `fix`: the concrete change;
    - `output_change`: `none`, `geometry/colors/export`, `runtime behavior`, or `API/recipe schema`.

### Reuse

Flag new code that re-implements something the repo already has. Grep the shared modules and the files next to the
change, and name the helper to call instead.

- **Studio:**
  - Core helpers live in `core/`:
    - `kit.py`: `Piece`, merging and mirroring, lofts, sweeps, hulls, `rounded_box`, `ring_points`, `host_band`,
      `paint_cut`, `carve`, `stud`.
    - `mesh.py`: palette atlas and color space.
    - `export.py`: palette resolution, gradient ramps, crease normals, material presets.
    - `rig.py`: weight transfer and smoothing.
    - `image3d.py`: voxel remesh and decimation.
    - `paths.work_root()` for temporary output, and `prompts.py` for the master style prompt.
  - Solids and fitting have their own helpers: `items/solid.py` (`Assembly`, `box`, `tube`, `lathe`, `inspect`),
    and `clothes.Fitted` and the costume hosts for fitting garments to a body.
  - Before a new primitive approximates a part, check `prefabs/details/*` and `studio.py schema` for an existing
    fastener, buckle, emblem, horn, plank or gash.
  - Builders take colors from named palette slots, not raw RGB.
  - New prefabs register in `prefabs/registry.py`, not in a parallel list.
- **Godot:**
  - `SignalBus` emit helpers for decoupled events, `[Signal]` delegates for local component notifications, and `ObservableResource` (`SetValue`) for observed data resources.
  - The existing components (`HealthComponent`, `MovementComponent`, `HurtBoxComponent`, `FadeOutComponent`, `SurfaceAppearanceController`, `ActionManager`, and so on).
  - The interfaces in `common/interfaces/` (`IDamageable`, `IInteractive`, `IPlayerAction`).
  - The existing `Resource` definitions, `[GlobalClass]` strategies, and factories under `scenes/`.
  - The bundled validators and renderers instead of new one-off probe scripts:
    `.agents/skills/godot-mcp/scripts/inspect_resource.gd`, `render_level_topdown.gd` and `asset-studio/godot/*`.

### Simplification

Flag complexity the change adds: redundant or derivable state, copy-paste with small variations, deep nesting, dead
code. Name the simpler form that does the same job.

- **Studio:**
  - Builders or variants that differ only in constants. Make them a parameter table or a recipe entry.
  - Recipe keys that restate defaults, and parameters nobody passes.
  - Compatibility shims, retired flags or old entrypoints left behind after a refactor.
  - Stored state that can be recomputed from the mesh.
  - Long positional argument lists. The rest of the kit passes one spec dict.
  - Comments or README paragraphs that restate the code or each other. The studio README and the `assets` skill
    must not drift into two copies.
- **Godot:**
  - Classes with several reasons to change. Split them into components.
  - Extra layers, factories or interfaces with a single implementation and no pressure to vary. Don't split just to
    add patterns.
  - Programmatic-only `GetX()`/`SetX()` methods or `{ get; private set; }` on tunable attributes, configurable actor
    states, or component/resource references that should be `[Export]` properties (`{ get; set; }`) bound in the
    Godot Inspector (with `IsNodeReady()`-guarded reactive setters when side effects are needed).
  - `GetNode<T>("...")` for authored sibling/child dependencies where an `[Export]` reference wired in the `.tscn`
    belongs.
  - `GameManager`-style registries, autoload growth or service-locator lookups where an exported dependency or
    parent initialization works.
  - Hardcoded `res://` paths in gameplay logic instead of exported `PackedScene`/`Resource` fields.
  - `[Tool]` setters that do more than regenerate editor preview data.

### Efficiency

Flag wasted work: redundant computation, repeated I/O, independent work run sequentially, blocking work on hot paths,
and long-lived closures that keep large data alive. Name the cheaper alternative.

- **Studio:**
  - Python loops over vertices or faces where numpy vectorizes.
  - All-pairs distance or ray tests where `scipy.spatial.cKDTree` or a bounding pre-filter works. Covered-skin
    culling once took 13 s until rays were limited by reach.
  - `Piece.merge` or array concatenation inside loops.
  - Meshes, rigs, GLBs or palettes reloaded on every call instead of once.
  - One Godot launch per item where a batch command renders everything in one session (`lineup --all`).
  - Closures kept in long-lived structures that capture whole piece dicts or mesh arrays, such as host lambdas,
    where a small object holding only the needed fields would do.
- **Godot:**
  - Allocations in `_Process`/`_PhysicsProcess`: LINQ, new collections, string formatting, or uncached `StringName` literals (`Input.IsActionPressed`, `SetShaderParameter`, `IsInGroup`, `AnimationTree`).
  - `GetNode`, group queries or `ResourceLoader.Load` on every frame instead of once in `_Ready` or via `[Export]`.
  - Short-lived combat/VFX scenes (`PackedScene.Instantiate` + `QueueFree`) that should use `ScenePool.Spawn<T>` / `ScenePool.Despawn` and `IPooledNode`, or idle components leaving `_PhysicsProcess`/`_Process` enabled when inactive.
  - Signal subscriptions or lambdas capturing `this` on long-lived emitters (`SignalBus.Instance`, runtime `Resource`s,
    `SceneTree`) that do not use `this.SubscribeUntilExit(...)`.
  - Navmesh rebakes or full regeneration for a local change.
  - Tests marked `[RequireGodotRuntime]` that don't need the engine; they boot Godot for nothing.

### Altitude

Check that each change fixes the root cause at the right depth instead of patching a symptom. Special cases layered
on shared code mean the fix isn't deep enough; name the more general change to the underlying mechanism.

- **Studio:**
  - Per-character or per-item name checks in shared prefabs. Move the variation into the recipe or a parameter.
  - Magic offsets that hide a gap or a floating part, instead of fitting to the host surface.
  - Color, insets or grooves faked with overlay shells laid over a mesh, instead of `paint_cut`/`carve` on the
    existing faces.
  - Inflated geometry, hidden bridges, or relaxed topology and validation policies added to pass a gate.
  - Render, lighting or import tweaks that hide a geometry or weighting problem.
  - Bone or skin-weight special cases instead of a fix to the weight transfer.
  - Candidates, reports or images written into the repo or `catalogs/` instead of `work_root()` and an explicit
    publish step.
  - Gameplay data (stats, damage, `.tres`) creeping into the art-only studio.
- **Godot:**
  - Hand-placed scene fixes for something a generator or factory produces.
  - Null checks or deferred calls that mask an initialization-order problem dependency injection or `_EnterTree`/`_Ready`
    ordering should solve.
  - Broad `try`/`catch` blocks that swallow failures.
  - Enemy- or item-specific branches (`switch`/`if` chains) in shared AI, components or `SignalBus` that belong in
    a selectable `[GlobalClass] Resource` strategy.
  - Forward+-only rendering APIs such as `GeometryInstance3D.Transparency`, or mutating shared material alpha in place
    instead of duplicating materials (`FadeOutComponent`) or using a two-pass depth pre-pass shader on the Mobile renderer.
  - Feature logic in autoloads.

## Phase 2 — Apply the fixes

Wait for all four reports. Merge findings on the same line or mechanism. When reuse and altitude disagree, prefer
the deeper fix. Then fix each remaining finding directly.

Skip a finding, and say so, when it:
- changes intended behavior. In the studio, that means any change to generated geometry, colors, weights, normals or
  exported files, so any `output_change` other than `none`. Report those for a deliberate follow-up instead;
- needs work well outside the reviewed change;
- is a false positive.

Don't touch generated artifacts, catalogs or game assets, and don't publish, export or commit. Match the local
style: C# per `.editorconfig` (tabs, CRLF, no final newline), Python's surrounding naming and comment density, and
brand-neutral identifiers.

Before editing studio code, snapshot the outputs it feeds, so Phase 3 can prove the cleanup changed nothing. Pick one
to three recipes that exercise the changed code (grep the recipes for the touched prefab kinds), then run:

```bash
./.venv/bin/python .agents/skills/code-simplify/scripts/fingerprint.py snapshot before \
  --character orc/orc_king --character heroes/knight
```

For items, add `--glb <staged path>` with the path that `studio.py item build <key>` reports.

## Phase 3 — Verify

Run the checks for the domains you touched, and report each result, with its output when it fails:

- **Studio:** run both commands:
  ```bash
  ./.venv/bin/python .agents/skills/code-simplify/scripts/fingerprint.py compare before
  ```
  ```bash
  ./.venv/bin/python -m unittest discover -s asset-studio/tests
  ```
  `compare` rebuilds the stored characters itself. Rebuild items with `item build` before comparing.
- **Godot C#:** run `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"`. When testable logic changed, also run
  `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot /usr/local/share/dotnet/dotnet test "Rogue Gauntlet.csproj" --settings .runsettings`.
- **Godot data or new scripts:** run `.agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import`,
  then `inspect_resource.gd` on each touched `.tres`.

A fingerprint mismatch means the cleanup changed the output. Revert that fix, or report it as intentional.

Finish with a short summary:
- fixes applied, one line each with its file;
- findings skipped, and why;
- verification results.
