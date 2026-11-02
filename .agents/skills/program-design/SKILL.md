---
name: program-design
description: "Settle code shape before a substantial implementation or refactor in Rogue Gauntlet: types, signatures, Resource and scene wiring, call order, file locations, failure contracts, and independently verifiable slices. Use when creating or revising an implementation plan; ordinary small edits do not need this workflow."
---

# Program Design

Bridge architecture and implementation: settle the decisions an implementer would otherwise have to invent. Read the current code before naming its replacements. Adapt detail to the change; write signatures without bodies, affected paths without file contents, and call order without implementation logic.

## Scope and altitude

- Reference `AGENTS.md`, `README.md`, `ARCHITECTURE.md`, the GDD, roadmap, and feature documents rather than repeating their contents. Proposed behavior is not implemented behavior; store plans in `docs/specs/` and update index links.
- Describe what changes and why. If a wrong guess could materially affect ownership, correctness, authoring, or migration, specify the contract. Omit obvious accessors and plumbing.
- Preserve the user's requested scope: planning authorizes design and documentation, not immediate implementation or publishing.

## Establish the current seams

Trace the smallest complete path affected by the change (callers, consumers, adjacent `.tres`/`.tscn` references, exports, signals, save DTOs, and tests). Use existing names for unchanged code; clearly label proposed names. Resolve critical unknowns through code inspection or small scoped experiments before turning assumptions into API promises.

## Naming and taxonomy rules

- **Node and Class Types:**
  - **`*Profile` for configuration resources:** Authored datasets and tunable presets use the `*Profile` suffix (`FlickerProfile`, `StatProfile`, `SurfaceAppearanceProfile`, `AttackAnimationProfile`, `HitVulnerabilityProfile`, `LootDropProfile`). Store presets in a matching pluralized directory (`scenes/effects/flicker_profiles/`).
  - **`*Component` for actor capabilities:** Single-capability child nodes attached to actors end in `Component` (`HealthComponent`, `MovementComponent`, `HurtBoxComponent`, `DodgeComponent`).
  - **`*Controller` for coordinators:** Nodes coordinating multiple child components or visual systems end in `Controller` (`SurfaceAppearanceController`).
  - **Domain nouns for actors, fixtures & visual nodes:** Standalone entities, interactive props, and effect nodes use direct conceptual nouns (`Player`, `EnemyBase`, `LightFixture`, `FlickeringLight`, `EffectLight`, `FlyingOrb`, `CharacterModel`, `Chest`, `Door`).
  - **Interfaces:** Prefix with `I` (`IDamageable`, `ICombatDamageable`, `IInteractive`, `IPooledNode`).
  - **C# filenames:** Exactly match the class name in PascalCase (`LightFixture.cs` defines `public partial class LightFixture`). One `[GlobalClass]` per `.cs` file.

- **Scene and Asset Files (Subject-First):**
  - **Subject-first ordering:** File and scene names start with the primary subject followed by descriptor or state: `<subject>_<descriptor>.tscn` / `<subject>_<state>.tscn` (e.g., `candle_flame.tscn`, `torch_flame.tscn`, `brazier_fire.tscn`, `hearth_fire.tscn`, `torch_mounted.tscn`, `wall_torch_unlit.tscn`, `candelabra_lit.tscn`).
  - **Strict lowercase `snake_case`:** All scene (`.tscn`), resource (`.tres`), and asset paths use lowercase `snake_case` (`flickering_light.tscn`, `torch_mounted.tscn`, `charged.tres`).

- **Properties, Exports & Signals:**
  - **Exported properties:** PascalCase reflecting domain concepts directly (`IsLit`, `Flame`, `Light`).
  - **Profile bindings:** When a component configures a single profile type unambiguously, name the property `Profile` (e.g., `FlickeringLight.Profile`). Use the full type name (`FlickerProfile`) when ambiguous or alongside multiple profiles (`VisualEffectTimeline.FlickerProfile`).
  - **Signals:** Past-tense event names (`Died`, `Finished`, `Interacted`), with delegate `[Signal] public delegate void <Name>EventHandler(...)`.

- **No Backward-Compatibility Shims:**
  - We own all code in this repository. Never retain transitional subclasses, dummy inheritance shims (`class Candle : LightFixture`), or legacy aliases. When refactoring or standardizing names, migrate all callers, scenes, resources, and tests directly and completely.
  - Prefer configuring an instance property (e.g., `IsLit = false`) over creating duplicate variant scenes with brittle child-node index overrides.

## Godot design contract

Align planned C# contracts with `.agents/skills/godot-csharp/SKILL.md`:

- **Composition, Strategy & Factory:** Build actors from focused single-capability child Nodes (`[GlobalClass]`) and selectable `[GlobalClass] Resource` strategies (one per `.cs` file). Strategy `.tres` resources must remain stateless; pass actor context into methods. Clone runtime copies (`CreateRuntimeCopy()` / `Duplicate()`) in the actor root's `_EnterTree()` before child `_Ready()` runs.
- **Editor-First Authoring:** Expose tunable attributes and Node/Resource dependencies as `[Export] public T Prop { get; set; }` (never C#-only `GetX()`/`SetX()` or `{ get; private set; }`). In reactive setters, assign the backing field first, guard child/scene side effects with `if (!IsNodeReady()) return;`, and apply state in `_Ready()`. Keep properties exposed to `AnimationPlayer` or GDScript Variant-compatible.
- **Three-Tier Notification Model:** Local component events use `[Signal]` delegates (Variant-compatible); observed data resources use `ObservableResource` (`SetValue` -> `EmitChanged()`); decoupled cross-system broadcasts use `SignalBus` static emit helpers. Use `SubscribeUntilExit` whenever observing long-lived emitters.
- **Dependency Injection & Contracts:** Avoid new autoloads or global registries. Prefer C# interfaces (`IDamageable`, `IInteractive`) and local Godot group queries (`player`, `enemy`). Inject dependencies via parents or typed exports.
- **Seeded Determinism & Persistence:** Separate RNG streams (`GenerationRandom.DeriveSeed`). Reconstruct untouched content from the run seed; persist only deltas. No historical-seed compatibility machinery. Bump `SaveGame.CurrentVersion` when schemas change.
- **Mobile Renderer Discipline:** Never plan around Forward+-only features (`GeometryInstance3D.Transparency`). Use instance-local material duplication (`FadeOutComponent`) or two-pass depth pre-pass `ShaderMaterial`s (`SurfaceAppearanceController`).

## Choose useful notations

### Types and signatures
Write key properties, methods, signals, and DTO fields without implementation bodies. State error contracts (return value vs. exception, mutation on failure, notification timing). Distinguish authored `.tres` fields from runtime-only fields.

### Affected file tree
Mark `+` added, `~` modified, `-` removed. Include only affected paths and short rationale when non-obvious. Include scenes, resources, and tests alongside C# files.

### Call-stack trees
Required when orchestration or control flow changes across Node/Resource boundaries, signal callbacks, or save/restore. Put correctness invariants next to the step that enforces them. No implementation bodies or pseudocode.

### Diagrams
Use Mermaid only when a state machine or interleaved relationship is clearer in two dimensions. Use linear lists or call-stack trees otherwise.

## Slice the work

- Choose a small number of coherent slices (vertical, horizontal, or hybrid) with rationale.
- Resolve risky uncertainties in early slices; establish shared contracts before dependent work.
- Each slice defines what can be exercised when it lands, affected files/contracts, verification steps, and remaining limitations.
- Keep each slice coherent: update callers, authored resources, and persistence adapters together.
- Deferred mechanics get clean extension seams, not unused state fields or speculative framework layers.

## Verification and cadence

Plan verification steps matched to the change type; run implementation checks as code lands:
- C# compile: `dotnet build "Rogue Gauntlet.sln"`. Exported clean check: `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease`.
- Behavior: targeted GdUnit4 tests under `tests/` with `GODOT_BIN` set and isolated `TMPDIR`.
- Scenes/resources: use `godot-mcp` skill for build, import, UID validation, and headless smoke checks.
- Designer workflows: acceptance exercise creating/assigning Resources and verifying Inspector behavior.

## Completion check

- Proposed names follow repository taxonomy (subject-first assets, `*Profile` resources, `*Component` capabilities, domain nouns) with zero backward-compatibility shims.
- Key contracts, errors, and ownership boundaries are settled; new files and modified callers have clear justification.
- Resource sharing, runtime cloning, composition, Inspector authoring, and change notifications are explicit.
- Changed orchestration has a call-stack tree with ordering invariants.
- Slicing direction, independently verifiable checks, and interim coherence are recorded.
- Open decisions and unverified engine boundaries are visible; no implementation is implied by a plan.

Use these as a review checklist, not a requirement to pad every plan with every notation or every internal signature.
