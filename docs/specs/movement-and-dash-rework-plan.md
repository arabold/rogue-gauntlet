# Movement and Dash Rework Plan

## 1. Goal and Design Direction

Transition player and monster locomotion from the current rigid, instantaneous step-function movement to a **top-down action-RPG baseline**:
- **Pacing**: A full screen cross-transit time of **$4.5 - 5.0\text{ seconds}$** (base speed **$7.5\text{ m/s}$** on the game's $35.6\text{ m}$ orthogonal camera viewport).
- **Responsiveness**: Snappy arcade acceleration ($0.12 - 0.16\text{ s}$) and rapid braking deceleration ($0.06 - 0.10\text{ s}$) that eliminates the harsh digital snap while preserving hair-trigger dodging and positioning.
- **Dash (Evade)**: A committed tactical evade covering **$6.0\text{ meters}$** (~$1/6$ screen width, ~3 dashes from center to edge) over $0.15\text{ s}$ travel + $0.05\text{ s}$ recovery, with 2 charges on a $1.2\text{ s}$ recharge and $0.25\text{ s}$ minimum interval.
- **Animation Synchronization**: Eliminate the $13.7\times$ foot-sliding ("ice skating") discrepancy by scaling locomotion animation playback dynamically to actual horizontal ground speed, and adopting an energetic run/jog animation clip for high-speed travel.
- **Monsters**: Monsters share the same acceleration/deceleration component and scale proportionally, preserving kiteability for standard melee enemies while keeping fast rushers threatening.

---

## 2. Geometric & Metric Seams

From [`scenes/main/main.tscn`](../scenes/main/main.tscn) and `project.godot`:
- **Orthogonal Camera**: `size = 20.0`, aspect ratio $16:9$.
- **Screen Width on Ground**: $20.0 \times (16/9) = \mathbf{35.56\text{ m}}$.
- **Screen Depth on Ground**: $20.0 / \sin(50^\circ) \approx \mathbf{26.1\text{ m}}$.
- **Center to Left/Right Edge**: $\mathbf{17.78\text{ m}}$.
- **Center to Top/Bottom Edge**: $\mathbf{13.05\text{ m}}$.

---

## 3. Specifications by System

### 3.1. `MovementComponent.cs` (Locomotion Engine)

#### New Exported Properties
```csharp
/// <summary>Horizontal acceleration rate in meters/second^2 when starting or changing direction.</summary>
[Export(PropertyHint.Range, "10,200,1")] public float Acceleration { get; set; } = 50.0f;

/// <summary>Horizontal deceleration rate in meters/second^2 when input ceases (braking friction).</summary>
[Export(PropertyHint.Range, "10,300,1")] public float Deceleration { get; set; } = 90.0f;

/// <summary>When false, velocity jumps directly to target speed (legacy/instant behavior).</summary>
[Export] public bool UseAcceleration { get; set; } = true;
```

#### Acceleration / Deceleration Math in `UpdateVelocity(double delta)`
Instead of instant assignment:
```csharp
Vector3 desiredHorizontal = Vector3.Zero;
if (TargetDirection != Vector3.Zero && ActionGate?.AllowsLocomotion != false)
{
    desiredHorizontal = TargetDirection * (Speed * _speedMultiplier * _statusSpeedFactor);
}

if (!UseAcceleration)
{
    velocity.X = desiredHorizontal.X;
    velocity.Z = desiredHorizontal.Z;
}
else
{
    float rate = desiredHorizontal != Vector3.Zero ? Acceleration : Deceleration;
    velocity.X = Mathf.MoveToward(Velocity.X, desiredHorizontal.X, rate * (float)delta);
    velocity.Z = Mathf.MoveToward(Velocity.Z, desiredHorizontal.Z, rate * (float)delta);
}
```

#### Invariants & Edge Cases:
- **`Stop()` & `ResetTransientMotion()`**: Must zero physical velocity immediately (`Velocity = Vector3.Zero; Actor.Velocity = Vector3.Zero;`) to ensure death, staggers, and teleports halt instantly without deceleration sliding.
- **External Pushes / Knockback**: Knockback velocity (`_pushStrength`) continues to bypass steering acceleration, maintaining snappy combat hit reactions.
- **Locomotion Overrides (`_hasOverrideVelocity`)**: Dashes, lunges, and forced combat steps directly dictate velocity and bypass steering acceleration.

---

### 3.2. Speed Baseline Calibration

| Actor | Current Speed | Proposed Speed | % of Player Speed | Screen Transit Time (Edge-to-Edge) |
| :--- | :--- | :--- | :--- | :--- |
| **Player (Base)** | $10.0\text{ m/s}$ | **$7.5\text{ m/s}$** | $100\%$ | $4.74\text{ s}$ |
| **Player (+25% Gear/Buff)** | $12.5\text{ m/s}$ | **$9.37\text{ m/s}$** | $125\%$ | $3.79\text{ s}$ |
| **Melee Skeleton** | $3.0\text{ m/s}$ | **$3.2\text{ m/s}$** | $43\%$ | $11.1\text{ s}$ (standard kitable trash mob) |
| **Skeleton Warrior** | $3.0\text{ m/s}$ | **$3.5\text{ m/s}$** | $47\%$ | $10.2\text{ s}$ |
| **Forsaken Warden (Boss)** | $2.8\text{ m/s}$ | **$3.0\text{ m/s}$** | $40\%$ | $11.8\text{ s}$ (heavy, relentless presence) |
| **Rat (Rusher)** | $4.6\text{ m/s}$ | **$5.0\text{ m/s}$** | $67\%$ | $7.1\text{ s}$ (fast flanker/chaser) |

- **Player Default**: Set `PlayerStats.BaseSpeed = 7.5f;` in [`scenes/player/resources/PlayerStats.cs`](../scenes/player/resources/PlayerStats.cs) and update `player.tscn`'s authored `MovementComponent.Speed`.
- **Monsters**: Inherit the shared `MovementComponent` acceleration so monsters also accelerate naturally out of turns and path nodes.

---

### 3.3. Dash Rebalance (`player_dodge.tres`)

Update [`scenes/combat/definitions/player_dodge.tres`](../scenes/combat/definitions/player_dodge.tres) with the tactical evade parameters:

```ini
[resource]
script = ExtResource("1_dodge")
AnimationProfile = ExtResource("animation")
Distance = 6.0
TravelDuration = 0.15
ImmunityEnd = 0.15
RecoveryDuration = 0.05
CooldownDuration = 0.25
MaxCharges = 2
ChargeRechargeDuration = 1.2
PerfectWindow = 0.1
PassThroughLayers = 8
```

- **Distance**: Increased from $2.4\text{ m}$ to **$6.0\text{ m}$** ($\approx 1/6$ screen width; 3 dashes reach the screen edge from center).
- **Travel Velocity**: $\approx 40.0\text{ m/s}$ during the crisp $0.15\text{ s}$ burst.
- **Recovery Duration**: $0.05\text{ s}$ (provides crisp end-lag before normal movement resumes).
- **Recharge Duration**: $1.2\text{ s}$ per charge (restoring the responsive action-RPG combat baseline).
- **Chain Interval**: $0.25\text{ s}$ (allows a fluid double-dash when both charges are ready).

---

### 3.4. Animation Synchronization, Locomotion Blending & Analog Input

#### The Isolation Guarantee (Why Attacks & Other Actions Are Not Affected)
A key architectural requirement is that **only walking/running steps scale with velocity**—attacks, spells, defenses, item drinking, and idles must never be sped up or slowed down:

1. **Combat Actions (Attacks, Defenses, Parries, Staggers)**:
   - Governed by [`CombatAnimationComponent`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/scenes/attacks/CombatAnimationComponent.cs), which operates on its own dedicated combat execution clock.
   - When an attack or defense starts, `CombatAnimationComponent` temporarily disables `AnimationTree.Active = false` and directly seeks the `AnimationPlayer` clip based on authored combat timing.
   - Because `AnimationTree` is bypassed entirely during combat poses, **attack speed and weapon animations are 100% isolated and immune to locomotion scaling**.
2. **Non-Combat Action States (`Idle`, `Use Item`, `Hit`, `Dying`)**:
   - Within `AnimationTree`'s state machine, each state is an independent graph node.
   - `Idle`, `Use Item` (drinking potions, reading scrolls), and `Dying` are plain `AnimationNodeAnimation` nodes with no time-scale modification.
   - Only the `Walking` state node contains an internal `AnimationNodeTimeScale`.
   - Therefore, idle breathing, potion drinking, and death animations remain at their standard authored 1.0x playback speed.

#### Player Locomotion BlendTree (`Walking_B` + `Running_A` with Sync)
Rather than abruptly switching animations at a velocity threshold, the player's `Walking` state uses an `AnimationNodeBlendTree` containing an `AnimationNodeBlend2` with `sync = true`:

```text
[Walk: Walking_B] ────┐
                      ▼ (sync = true)
                 [Blend2] ──► [TimeScale] ──► [Output]
                      ▲
[Run: Running_A]  ────┘
```

- **Footstep Synchronization (`sync = true`)**: Both `Walking_B` and `Running_A` are 2-step locomotion cycles. With `sync = true`, Godot synchronizes their normalized playback phase so footsteps never stutter, desync, or limp during blending.
- **Dynamic Blend Calculation**: In `Player.cs`, `parameters/Walking/Blend2/blend_amount` smoothly transitions from $0.0$ (`Walking_B`, natural stride $\approx 1.5\text{ m/s}$) to $1.0$ (`Running_A`, natural stride $\approx 2.7\text{ m/s}$) over the speed range $1.5\text{ m/s} \to 5.5\text{ m/s}$:
  $$\text{blend} = \text{clamp}\left(\frac{v_{horiz} - 1.5}{4.0}, 0.0, 1.0\right)$$
  $$v_{natural} = \text{lerp}(1.5, 2.7, \text{blend})$$
  $$\text{scale} = \frac{v_{horiz}}{v_{natural}}$$

#### Analog Gamepad & Keyboard Walk Modifier
- **Gamepad Analog Deflection**: `InputComponent` reads stick input with `Input.GetVector(...)` and preserves magnitude ($0.0 \to 1.0$). `MovementComponent.SetInputDirection` clamps magnitude to $\le 1.0$ without normalizing away sub-maximal deflections, allowing the player to tilt the stick gently to walk or push fully to run.
- **Keyboard Walk Modifier**: Holding `Shift` (or the `"walk"` input action) caps input magnitude to `WalkSpeedFactor = 0.4f` ($3.0\text{ m/s}$), immediately blending the character into a steady walk pace. Full WASD input without Shift runs at the full $7.5\text{ m/s}$.

#### Monster Locomotion Architecture (Single-Clip Independence)
Monsters may not possess running animations (especially non-humanoid monsters like rats or crawling beasts). Monsters maintain a lean, robust single-clip `AnimationNodeBlendTree`:
```text
[AnimationNodeAnimation: Move / Walk / Run] ──► [AnimationNodeTimeScale] ──► [Output]
```
`EnemyBehaviorComponent` dynamically scales `TimeScale/scale` against each monster's calibrated `NaturalLocomotionSpeed` without requiring or referencing `Blend2`.
   $$v_{horiz} = \sqrt{v_x^2 + v_z^2}$$
   $$\text{Scale} = \max\left(0.1, \frac{v_{horiz}}{v_{natural}}\right)$$
   Update only the locomotion timescale parameter:
   ```csharp
   AnimationTree.Set("parameters/Walking/TimeScale/scale", speedScale);
   ```
3. **Dynamic Deceleration & Slow Effects**:
   - As an actor accelerates from $0 \to \text{MaxSpeed}$, step frequency smoothly increases.
   - During braking ($0.08\text{s}$ deceleration), steps rapidly slow down to match the foot-plant skid before transitioning to `Idle`.
   - When slowed by `SlowStatusComponent` (e.g., 50% slow), $v_{horiz}$ drops to half, and footstep frequency automatically cuts in half—zero sliding under status debuffs.

---

## 4. Affected File Tree

```text
~ scenes/components/MovementComponent.cs             # Added Acceleration, Deceleration, UseAcceleration logic
~ scenes/player/resources/PlayerStats.cs             # BaseSpeed tuned from 10.0 to 7.5
~ scenes/player/Player.tscn                          # MovementComponent defaults updated; AnimationTree locomotion node updated
~ scenes/player/Player.cs                            # Update AnimationTree locomotion timescale from physical horizontal speed
~ scenes/enemies/EnemyBehaviorComponent.cs           # Update monster AnimationTree locomotion timescale
~ scenes/combat/definitions/player_dodge.tres        # Distance=6.0, Travel=0.22, Recovery=0.08, Recharge=4.0
~ scenes/enemies/MonsterDefinition.cs                # MoveSpeed defaults tuned for ARPG ratio
~ scenes/enemies/forsaken_warden/forsaken_warden_definition.tres
~ scenes/enemies/rat/rat_definition.tres
~ scenes/enemies/skeleton/skeleton_minion_definition.tres
~ scenes/enemies/skeleton/skeleton_warrior_definition.tres
~ docs/README.md                                     # Index implementation plan
+ docs/specs/movement-and-dash-rework-plan.md        # This specification
```

---

## 5. Execution Call Stack & Invariants

```text
_PhysicsProcess(delta)
│
├── InputComponent [Priority 100]
│   └── Reads Camera basis & device inputs -> Sets InputDirection
│
├── PlayerInputController [Priority 200]
│   ├── Buffers fresh presses (Dodge, Guard, Attack)
│   └── MovementComponent.SetInputDirection(InputDirection)
│
├── MovementComponent [Priority 400]
│   ├── UpdateVelocity(delta)
│   │   ├── If HasOverrideVelocity (Dash/Lunge): Apply direct override speed (bypasses acceleration)
│   │   ├── Else If Pushed: Linear push decay (bypasses acceleration)
│   │   └── Else:
│   │       ├── TargetDirection != Zero: MoveToward target velocity using Acceleration * delta
│   │       └── TargetDirection == Zero: MoveToward Zero using Deceleration * delta
│   ├── SmoothRotateToward(delta): Interpolates LookAtDirection
│   └── MoveAndSlideActor(): Applies velocity to CharacterBody3D
│
└── Locomotion Animation Update (Player / EnemyBehaviorComponent)
    ├── Reads physical horizontal velocity magnitude
    └── Sets AnimationTree TimeScale parameter: v_horiz / NaturalSpeed
```

---

## 6. Implementation Slices

### Slice 1: Movement Physics & Base Speed Calibration (Complete)
- **Changes**: Added `Acceleration` (50.0 m/s²), `Deceleration` (90.0 m/s²), and `UseAcceleration` to `MovementComponent.cs`. Updated `PlayerStats.BaseSpeed = 7.5f`. Calibrated monster definition move speeds (Skeletons 3.2–3.5 m/s, Forsaken Warden 3.0 m/s, Rat 5.0 m/s).
- **Verification**: Created `tests/MovementPhysicsTest.cs` (5/5 passed). Verified acceleration ramp, deceleration braking, reversal braking, and `Stop()`.

### Slice 2: Dash Rebalance (Complete)
- **Changes**: Updated `player_dodge.tres` (`Distance = 6.0`, `TravelDuration = 0.15`, `RecoveryDuration = 0.05`, `ImmunityEnd = 0.15`, `CooldownDuration = 0.25`, `MaxCharges = 2`, `ChargeRechargeDuration = 1.2`).
- **Verification**: Ran `tests/CombatAccessibilityTest.cs` (14/14 passed) and `tests/CombatDefenseTest.cs` (36/36 passed).

### Slice 3: Locomotion Animation Speed Synchronization & Walk/Run Blending (Complete)
- **Changes**:
  - Replaced single `AnimationNodeAnimation` in monster scenes (`skeleton_minion.tscn`, `skeleton_warrior.tscn`, `skeleton_crossbow.tscn`, `forsaken_warden.tscn`, `rat.tscn`, `melee_enemy.tscn`, `projectile_enemy.tscn`) with an `AnimationNodeBlendTree` containing `AnimationNodeAnimation` -> `AnimationNodeTimeScale` -> `output`.
  - Configured `player.tscn` with `AnimationNodeBlend2` (`sync = true`) blending `Walking_B` and `Running_A` into `TimeScale` -> `output`.
  - Wired real-time velocity-based blend and timescale calculations in `Player.cs` and `EnemyBehaviorComponent.cs`.
  - Added analog gamepad stick reading (`Input.GetVector`) and Shift walk modifier (`WalkSpeedFactor = 0.4f`) in `InputComponent.cs` and `MovementComponent.cs`.
  - Configured `Running_*` and `Walking_*` clips to enforce `LoopMode.Linear` at runtime to prevent animation freezes.
  - Verified non-locomotion actions (attacks, guards, staggers, item drinking, idles, deaths) remain untouched and play at their authored speeds.
- **Verification**: Created `tests/LocomotionAnimationSyncTest.cs` (3/3 passed). Verified full regression suite and verified clean `ExportRelease` compilation.
