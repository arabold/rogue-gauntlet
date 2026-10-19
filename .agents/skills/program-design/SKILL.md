---
name: program-design
description: "Settle code shape before a substantial implementation or refactor in Rogue Gauntlet: types, signatures, Resource and scene wiring, call order, file locations, failure contracts, and independently verifiable slices. Use when creating or revising an implementation plan; ordinary small edits do not need this workflow."
---

# Program Design

Bridge architecture and implementation: settle the decisions an implementer would otherwise have to invent. Read the current code before naming its replacements. Adapt detail to the change; write signatures without bodies, affected paths without file contents, and call order without implementation logic.

## Scope and altitude

- Reference `AGENTS.md`, `README.md`, `ARCHITECTURE.md`, the GDD, roadmap, and relevant feature documents rather than repeating their requirements or system overview.
- Describe what changes. Record alternatives and rationale only where they settle a real implementation choice.
- If a wrong guess could materially affect ownership, correctness, authoring, or migration, specify the contract. Omit obvious accessors and plumbing.
- Preserve the user's requested scope. A planning request authorizes planning and documentation; it does not by itself authorize implementing, committing, or publishing the feature.
- Use an existing plan's useful decisions. Put feature implementation plans and proposed technical specifications in `docs/specs/`; update the documentation index and roadmap links when adding or moving a plan. Keep architecture summaries in `ARCHITECTURE.md` and intended gameplay in the GDD; proposed behavior is not implemented behavior.

## Establish the current seams

Trace the smallest complete path affected by the change, including callers and consumers. Check adjacent `.tres`/`.tscn` references, exported properties, signals, save DTOs, and tests when applicable. Use existing type and method names for unchanged code; clearly label proposed names.

Identify the uncertainty most likely to invalidate later work. Resolve it from code, authoritative Godot documentation, or a small explicitly scoped experiment; otherwise mark it open with the slice that must resolve it. Do not silently turn an unknown into an API promise.

## Godot design contract

- Prefer composition: focused child Nodes for active scene behavior, typed Resources for authored data and selectable strategies. Retain useful existing subclasses; avoid new inheritance permutations for independently varying capabilities.
- Specify which types are `[GlobalClass]` Resources, which are Nodes, and which are ordinary C# helpers/DTOs. A factory pattern does not itself require a Node or Resource; its designer-controlled policy does.
- For new authoring surfaces, name exported Resource/Node references, defaults, Inspector hints or validation that matter, and the scene responsible for assigning them. Do not hide tunable rules in a static service or hardcoded path.
- For runtime state, name its owner and whether referenced Resources are shared immutable definitions or instance-specific copies. Specify creation, restoration, copying, disposal, and mutation boundaries where relevant. Do not assume scene instantiation or `ResourceLocalToScene` isolates independently created items.
- For generated content, specify seed derivation, RNG ownership/draw order, and stable source identities. Regenerate the untouched baseline and persist differences; do not default to full-floor snapshots. Separate generation from gameplay/cosmetic streams and distinguish deterministic initial state from changes caused by play. Tests must compare generated outcomes, not just equal seeds.
- Scope RNG repeatability to the same build/content and relevant inputs. Changes to draw order between builds may change scenarios; do not add historical-seed compatibility machinery. Separate level, monster, and item generators with derived seeds, and distinguish accidental shared-stream coupling from intentional dependencies on generated data.
- Resource strategies may contain behavior; they must not retain per-actor mutable state when shared. Nodes own ticking, rendering, physics, and scene lifetime.
- Prefer local signals and Resource observation within a feature. Use `SignalBus` for intentionally decoupled notifications, not fallible operation requests. Define success/failure before notifications.
- Avoid new autoload responsibilities or global gameplay registries. Inject local dependencies through parents and exported references.
- Keep editor construction/setters safe: no random gameplay rolls, world queries, or scene side effects during ordinary Resource construction or inspection.
- Separate Inspector reachability from safe live editing. Specify exported state, change propagation, and commands needed to maintain invariants; do not promise arbitrary edits are safe merely because a property is visible.
- Account for Godot Variant-compatible signatures at editor/GDScript boundaries. Ordinary C# result records can remain internal; a debug Node can expose simple command arguments and a readable result.

## Choose useful notations

### Types and signatures

Write the key new/changed properties, methods, signals, and DTO fields that carry behavior or ownership. Use C# declarations without bodies. Distinguish an abbreviated contract from a compilable source file.

State the error contract alongside it: return value versus exception, whether failure mutates state, and when notifications fire. For Resources, identify authored versus runtime-only fields. One designer-created `[GlobalClass]` Resource belongs in one script file; show authored `.tres` and scene wiring separately.

### Affected file tree

Mark `+` added, `~` modified, `-` removed. Include only affected paths and a short reason when the role is not obvious. Include scene/resource changes, tests, and supporting docs alongside C# files. A bounded content migration may use an explicit glob with a selection rule rather than enumerating hundreds of assets. Do not list the whole repository.

### Call-stack trees

Required when orchestration/control flow changes. Show the meaningful boundaries: Node to Resource/helper, inventory ownership transfer, scene lifecycle, signal callbacks, RNG consumption, and save/restore. Use existing entry points and label additions. Put correctness invariants next to the step that enforces them.

For example, a pickup tree should settle when capacity is checked, when ownership moves, when the source is removed, and when observers see success. A tree confined to one method adds little value. No implementation bodies or pseudocode algorithms.

### Diagrams

Use Mermaid only when a state machine or interleaved relationship is clearer in two dimensions. Use a list or call-stack tree for a linear flow. Do not repeat a system architecture diagram in an implementation plan.

## Slice the work

- Choose a small number of coherent slices and record whether delivery is vertical, horizontal, or hybrid, with a short reason.
- Start with a risky uncertainty if it can overturn the design; establish shared contracts before dependent work.
- Each slice names what a developer can exercise when it lands, affected files/contracts, its verification, and explicit remaining limitations.
- Include callers, authored resources, and persistence adapters needed to keep that slice coherent. Do not leave the game unable to load intermediate content or depend on an unimplemented later slice.
- For code and authored-content transitions, prefer changes that leave each slice coherent. Save compatibility is explicitly excluded before public release: bump `SaveGame.CurrentVersion` when necessary and reject mismatched or missing versions; do not plan migrations, legacy fields, or old-save adapters. Same-version round trips still require verification.
- Deferred mechanics get extension seams, not unused state fields, speculative type systems, or mandatory framework work.

## Verification and cadence

This skill plans verification; run implementation checks as the corresponding code lands. For documentation-only work, validate the skill/plan and links instead of claiming engine validation.

- C# compile: `dotnet build "Rogue Gauntlet.sln"`; use `/usr/local/share/dotnet/dotnet` if absent from PATH.
- Behavior: targeted GdUnit4 tests in `tests/`, compiled into the existing game project. Use `.runsettings` and set `GODOT_BIN` as described in `AGENTS.md`; no separate test project. Keep engine-independent tests plain C# where possible.
- Scenes/resources: use the repository's `godot-mcp` skill for build, import, UID/resource checks, and focused runtime smoke tests. Navmesh-dependent behavior needs the documented limitations and editor verification.
- Designer-facing changes: name an editor acceptance exercise that creates/assigns Resources, inspects runtime copies, and invokes supported operations through the same implementation as gameplay.
- Asset Studio-only work: use the relevant pipeline commands and tests discovered in its current docs; do not run game checks by rote.

Keep implementation scoped to the slice. Record useful follow-ups rather than mixing them in. Introduce abstractions for a stable responsibility or actual independent variation, not solely to shorten code.

## Completion check

- Key contracts and errors are settled; proposed names are distinguishable from existing names.
- New files have a home; changed callers, scenes, resources, and save fields have a reason.
- Resource sharing, runtime ownership, composition, Inspector authoring, and change notification are explicit where affected.
- Changed orchestration has a call-stack tree with important ordering invariants.
- Slicing direction, independently executable checks, and interim compatibility are recorded.
- Open decisions and unverified engine boundaries are visible; no implementation is implied by a plan.

Use these as a review checklist, not a requirement to pad every plan with every notation or every internal signature.
