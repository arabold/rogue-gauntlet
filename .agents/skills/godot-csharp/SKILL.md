---
name: godot-csharp
description: >
  Use when writing, modifying, or refactoring C# gameplay, component, actor, UI, or Resource scripts
  in Rogue Gauntlet (`scenes/**/*.cs`, `scripts/**/*.cs`, `common/**/*.cs`, `tests/**/*.cs`). Covers
  Godot 4.7 C# architecture (Composition, Strategy, Factory), Editor-first `[Export]` authoring,
  the three-tier notification model (`[Signal]`, `ObservableResource`, `SignalBus`), runtime/physics
  lifecycle, and Mobile renderer (`rendering_method="mobile"`) transparency rules.
---

# Godot 4.7 C# Patterns for Rogue Gauntlet

Write C# so every actor, component, strategy, and resource works first-class in the Godot Editor Inspector, `AnimationPlayer`, `AnimationTree`, and the Mobile renderer.

## 1. Architecture: Composition, Strategy & Factory

- **Composition (`Node` capabilities):**
  - Give each child `Node` one capability (`HealthComponent`, `MovementComponent`, `HurtBoxComponent`, `ActorActionGateComponent`, `DodgeComponent`, `GuardComponent`, `PerceptionComponent`, `NavigationComponent`, `WorldEffectHost`, `SurfaceAppearanceController`). Split a class when it has multiple independent reasons to change; keep cohesive behavior in one script until variation or reuse is real.
  - Add `[GlobalClass]` to reusable component `Node` scripts (one `[GlobalClass]` per `.cs` file) so they appear directly in the Editor's **Create New Node** dialog and Inspector node pickers.
  - Keep actor roots (`Player.cs`, `EnemyBase.cs`) as thin facades that expose gameplay interfaces (`IDamageable`, `ICombatDamageable`, `IStaggerable`), initialize runtime copies in `_EnterTree()`, and forward calls to child components.
- **Strategy (`[GlobalClass] Resource`):**
  - Vary behavior across monsters, weapons, scrolls, locks, or rooms with a selectable `[GlobalClass] public abstract partial class ... : Resource` exported on the component or definition, giving designers an Inspector dropdown instead of `switch`/`if` chains.
  - **One `[GlobalClass]` per `.cs` file** (plain `enum`s may share the file). Run `--import` after adding a new script so Godot generates its `.cs.uid` sidecar.
  - **Stateless `.tres` strategies:** Shared strategy resources must never store per-actor mutable state (cooldowns, targets, RNG, tokens). Pass actor context (`in EnemyContext`, `ActorInteractionContext`) into methods or create a lightweight per-actor state object in `_Ready()`.
- **Factory & Prototype (`_EnterTree` vs. `_Ready`):**
  - Treat authored `.tres` assets (`PlayerStats`, `Inventory`, `MonsterDefinition`, `Item` definitions) as immutable prototypes.
  - Godot runs `_EnterTree()` top-down (parent first) and `_Ready()` bottom-up (children first). Clone runtime state (`CreateRuntimeCopy()` / `Duplicate()`) or apply `MonsterDefinition` values in the actor root's `_EnterTree()` so child components see initialized state in `_Ready()`. Do not rely on `ResourceLocalToScene` alone to isolate dynamically created items.
  - Encapsulate procedural spawning and loot rolling in Factory classes or `[GlobalClass]` factory resources using explicit `GenerationRandom` streams (`DeriveSeed(levelSeed, domain, sourceId)`), never global `GD.Randf()` / `Random.Shared` or hardcoded `res://` paths in gameplay code.

## 2. Inspector & Editor-First Authoring

- **`[Export]` properties (`{ get; set; }`), never C#-only `GetX()`/`SetX()` or `{ get; private set; }`:**
  - Expose every tunable attribute (`Speed`, `MaxTurnRate`, `Duration`, `Xp`), initial/configurable actor state (`InitialAction`, `AIEnabled`), and Node/Resource dependency as `[Export] public T Prop { get; set; }` so the Inspector, `.tscn`/`.tres` serializer, Remote Scene Tree, and `AnimationPlayer` property tracks can read and write it.
- **Deserialization-safe reactive `[Export]` setters:**
  - When changing a property must trigger side effects (clamping, updating a child node, refreshing visuals, transitioning state), put that logic in the `[Export]` property's `set` accessor rather than a separate `SetX()` method.
  - **Lifecycle invariant:** Godot assigns `[Export]` values during instantiation *before* `_EnterTree()` and `_Ready()`. Always assign the backing field first, guard scene/child side effects with `if (!IsNodeReady()) return;`, and apply initial state in `_Ready()`:
  ```csharp
  private float _speed = 3.0f;

  [Export(PropertyHint.Range, "0,50,0.1")]
  public float Speed
  {
  	get => _speed;
  	set
  	{
  		_speed = Mathf.Max(0f, value);
  		if (IsNodeReady()) SyncNavigationSpeed();
  	}
  }
  ```
- **Explicit `[Export]` node wiring over `GetNode` / `NodePath`:**
  - Wire sibling/child component dependencies via typed `[Export] public FooComponent Foo { get; set; }` in the `.tscn` (renaming/reparenting nodes in the Editor updates typed exports automatically, unlike `NodePath` strings or `GetNode("...")`).
  - Leave optional capabilities (`DodgeComponent`, `GuardComponent`, `AttackSlotsComponent`) nullable so omitting them in a scene cleanly disables the capability. Default-initialize exported arrays (`= [];`) so untouched Inspector arrays are never `null`.
- **Hints, Variant compatibility & `[Tool]` polish:**
  - Use `[ExportGroup]`, `[ExportSubgroup]`, `[Export(PropertyHint.Range, "min,max,step")]`, and `[Export(PropertyHint.Layers3DPhysics)]`.
  - Keep properties read by `AnimationTree` advance expressions (`IsMoving`, `IsDead`, `IsHit`), `AnimationPlayer` tracks, or GDScript harnesses on Godot Variant-compatible types (`bool`, `int`, `float`, `string`, `Vector3`, `StringName`, `enum`, `GodotObject`).
  - On `[Tool]` scripts:
    - Guard runtime-only logic with `if (Engine.IsEditorHint()) return;`.
    - Use `[ExportToolButton("Rebuild")] public Callable RebuildButton => Callable.From(Rebuild);` for clickable Inspector action buttons instead of fake boolean checkboxes.
    - Override `_GetConfigurationWarnings()` (calling `UpdateConfigurationWarnings()` in setters when in the editor) to flag missing required `[Export]` references in the Scene Tree dock.
    - Override `_ValidateProperty(Godot.Collections.Dictionary property)` + `NotifyPropertyListChanged()` when an exported enum/mode should hide irrelevant Inspector fields (`PropertyUsageFlags.NoEditor`).

## 3. Notifications & Subscription Lifetime

| Scope | Mechanism | Rule |
| --- | --- | --- |
| **Local component / Node** | `[Signal] public delegate void ...EventHandler(...)` | Use for parent-child or sibling events (`HealthComponent.Died`, `FadeOutComponent.Finished`). Exposes the signal to the Editor's Node Signals dock, `AnimationPlayer`, `ToSignal`, and C# events. Parameters must be Variant-compatible; use a plain C# `event` **only** for non-Variant structs (`in DamageContext`). |
| **Observed `Resource`** | `ObservableResource` (`SetValue(ref _field, value)`) | Call `SetValue` inside `[Export]` setters on runtime data resources (`PlayerStats`, `Inventory`, `ActionSlot`) so code and Inspector edits both emit `EmitChanged()`. |
| **Cross-system broadcast** | `SignalBus` static emit helpers | Use `SignalBus.Emit...` only when sender and receiver are intentionally decoupled (e.g., gameplay to HUD, floor transitions). Never use `SignalBus` for fallible request/response calls. |

- **Subscription lifetime (`SubscribeUntilExit`):**
  - Whenever a `Node` subscribes to an emitter that can outlive it (`SignalBus.Instance`, a runtime `Resource`, `SceneTree`, or an ancestor node), wrap it in `this.SubscribeUntilExit(emitter, e => e.Signal += OnEvent, e => e.Signal -= OnEvent)` so `TreeExiting` unhooks it automatically.
  - Never use `SubscribeUntilExit` inside `_Ready()` on an `IPooledNode` for its own child nodes (`ScenePool.Despawn` removes the pooled node from the tree, unhooking the handler, and `ScenePool.Spawn` re-adds it without running `_Ready()` again).
  - In GdUnit4 tests, attach nodes with `TestFactories.AddToSceneTree(node)` rather than calling `_Ready()` on an unparented node, so `TreeExiting` fires on cleanup.

## 4. Runtime, Physics & Pooling

- **Deterministic physics tick order (`CombatPhysics`):**
  - Any component whose `_PhysicsProcess` order matters must set `ProcessPhysicsPriority` in `_Ready()` from `CombatPhysics` (`Input = -60`, `InputModifier = -55`, `Intent = -50`, `Defense = -40`, `Steering = -30`, `Clearance = -25`, `Movement = -20`, `Damage = 0`, `Commit = 10`, `Trace = 20`) instead of relying on Scene Tree sibling order.
  - Call `SetPhysicsProcess(false)` / `SetProcess(false)` when a component is idle and enable ticking only while active.
- **Disposed wrappers (`GodotObject.IsInstanceValid`):**
  - `QueueFree()` leaves existing C# references non-null (`ObjectDisposedException` on access). Always check `GodotObject.IsInstanceValid(node)` (not just `!= null`) on dynamic runtime `Node` references held across frames, tweens, `await ToSignal(...)`, or `CallDeferred`.
- **Pooling (`ScenePool` & `IPooledNode`):**
  - Spawn frequent short-lived combat/VFX scenes (projectiles, sparks, `DamageNumber`) via `ScenePool.Spawn<T>(scene, parent)` and return them with `ScenePool.Despawn(this)`.
  - Because `_Ready()` runs only on first instantiation, implement `IPooledNode` (`OnSpawnedFromPool()` / `OnDespawnedToPool()`) to reset timers, velocities, collision state, and particles on reuse.
  - Every despawn removes the node from the tree, so `TreeExiting` fires, and reuse does not run `_Ready()` again. Inside a pooled scene, connect to the scene's own children with a plain `+=` in `_Ready()`, never `SubscribeUntilExit`: the first despawn would unhook the handler for good. That is how pooled arrows stopped registering hits.
- **Physics interpolation & callback safety:**
  - `project.godot` enables `common/physics_interpolation=true`. Call `node3D.ResetPhysicsInterpolation()` immediately after snapping `GlobalPosition` / `GlobalTransform` discontinuously (spawn, teleport, pool reuse) to avoid a 1-frame visual streak.
  - Inside `BodyEntered` / `AreaEntered` callbacks, defer physics mutations (`SetDeferred(CollisionShape3D.PropertyName.Disabled, true)` or `Callable.From(...).CallDeferred()`) to avoid `Function blocked during in/out signal`. Run `GetWorld3D().DirectSpaceState` queries inside `_PhysicsProcess`, not `_Process`.
- **Tweens & hot-path discipline:**
  - Store `private Tween _activeTween;`, call `_activeTween?.Kill()` before starting a replacement tween on the same property, and create it via `targetNode.CreateTween()` so its lifetime binds to the node.
  - Cache `StringName` constants (`private static readonly StringName ... = "..."` or `[Export] public StringName`) for inputs, shader uniforms, groups, and animations, and use generated `PropertyName`/`MethodName`/`SignalName` constants. Avoid LINQ, collection allocations, `GetNode`, group queries, or `ResourceLoader.Load` inside `_Process` / `_PhysicsProcess`.

## 5. Mobile Renderer (`rendering_method="mobile"`) & Transparency

- **Never use `GeometryInstance3D.Transparency`:** Per-instance transparency is Forward+-only and silently does nothing on the Mobile renderer.
- **Simple mesh/actor fades (`FadeOutComponent`):** Use `FadeOutComponent` (`FadeOutAndFree`), which duplicates each surface's `BaseMaterial3D` into an instance-local override, sets `Transparency = BaseMaterial3D.TransparencyEnum.Alpha`, and tweens `albedo_color:a` (plus `modulate:a` on `SpriteBase3D`/`Label3D`). Always duplicate before mutating alpha or shader parameters so shared `.tres`/`.glb` materials stay untouched.
- **Multi-mesh actor shader fades (`SurfaceAppearanceController`):** Alpha-blending articulated multi-part characters causes internal triangle sorting artifacts on Mobile. Use a two-pass `ShaderMaterial` chain (`NextPass`, as in `teleport_depth.gdshader` + `teleport_surface.gdshader`):
  1. **Depth pre-pass:** `render_mode unshaded, blend_add, depth_draw_always, cull_disabled, shadows_disabled;` writing `ALBEDO = vec3(0); ALPHA = 1.0;` (discarding cutouts) so the depth buffer records only the nearest front surface.
  2. **Color fade pass:** `render_mode blend_mix, depth_draw_never, cull_disabled;` sampling source PBR properties and fading `ALPHA`.
- **Solid skin overrides:** Keep solid surface replacements (`iron_skin`, `stone_skin`) on opaque depth rendering (`depth_draw_opaque` with `AlphaScissor` cutout support) routed through `SurfaceAppearanceController`.

## 6. Formatting & Verification

- **Style (`.editorconfig`):** Tabs (`\t`) for `*.cs`, `CRLF` line endings, no final newline, global namespace matching existing partial classes.
- **Verify:**
  1. Compile: `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"`
  2. New `[GlobalClass]` scripts or `.tres`/`.tscn` edits: `.agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import` and validate with `inspect_resource.gd`.
  3. Tests: `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot /usr/local/share/dotnet/dotnet test "Rogue Gauntlet.csproj" --settings .runsettings`
