# Monster Defense, Frontal Cone & Riposte Implementation Plan

Status: implemented for the current skeleton rollout. See [combat actions and defense](../combat-system.md) and [combat harness traces](../combat-harness.md) for current behavior; the declarations below record the original design.

## Scope and Context

This plan extends the combat defense framework established in [`specs/actor-actions-and-interactions-plan.md`](actor-actions-and-interactions-plan.md) (specifically Slice 7: Enemy intentions and content rollout) by implementing organic, designer-authored defensive behavior:
1. **Designer-Authored Defense Profiles**: Expose defensive traits (`GuardChance`, `DodgeChance`, `DefenseReactionDelay`, `GuardPressureWindow`, `RiposteChance`) on [`EnemyBehaviorProfile`](../../scenes/enemies/EnemyBehaviorProfile.cs) so monster archetypes (beasts, shielded skeletons, agile rogues, bosses) exhibit distinct behaviors rather than 100% deterministic bot reactions.
2. **Strict $120^\circ$ Frontal Cone Protection**: Guard and parry apply only within the forward $120^\circ$ sector ($60^\circ$ half-angle). Attacks from behind or the flank completely bypass shields and weapons, dealing full damage and stagger to both players and monsters.
3. **Pressure-Maintained Sustained Blocking**: Rather than dropping guard after a fixed timer while the player continues attacking, the monster maintains its raised shield under pressure (refreshing on blocked hits and ongoing attacks).
4. **Riposte Counter-Attacks**:
   - **On Parry**: Successful parries stagger the attacker and sustain guard pressure. Counterattacks wait for guard hold and release recovery, then revalidate melee reach.
   - **On Blocked Combos**: When the player exhausts a string of hits against a shield and pauses, the defender can riposte with an immediate counter-attack, punishing mindless button-mashing.

---

## Architecture & Data Contracts

### 1. `EnemyBehaviorProfile` Additions
Authored in `.tres` assets under `scenes/enemies/`:

```csharp
public partial class EnemyBehaviorProfile : Resource
{
    // ... existing detection, roam, and attack properties ...

    /// <summary>Probability (0.0 to 1.0) of attempting to raise guard when perceiving an attack.</summary>
    [Export] public float GuardChance { get; set; } = 0.0f;

    /// <summary>Probability (0.0 to 1.0) of attempting an evasive dodge when perceiving an attack.</summary>
    [Export] public float DodgeChance { get; set; } = 0.0f;

    /// <summary>Reaction delay in seconds before an enemy reacts to a perceived attack wind-up.</summary>
    [Export] public float DefenseReactionDelay { get; set; } = 0.10f;

    /// <summary>Duration to maintain guard after the last blocked impact or observed attack before dropping.</summary>
    [Export] public float GuardPressureWindow { get; set; } = 0.85f;

    /// <summary>Probability (0.0 to 1.0) of launching a riposte counter-attack after successfully holding guard through a combo or landing a parry.</summary>
    [Export] public float RiposteChance { get; set; } = 0.65f;

    /// <summary>Whether this enemy defends against airborne projectiles.</summary>
    [Export] public bool CanDefendProjectiles { get; set; } = true;
}
```

### 2. `EnemyDefenseController` Defense Modes & State
Extending [`EnemyDefenseController`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/scenes/enemies/EnemyDefenseController.cs):

```csharp
public enum EnemyDefenseMode
{
    ProfileDefault, // Uses EnemyBehaviorProfile chances (production game mode)
    Dodge,          // Forced dodge (100% chance, for harness/debug testing)
    GuardParry,     // Forced guard/parry (100% chance, for harness/debug testing)
    None            // Disabled defense
}

public partial class EnemyDefenseController : Node
{
    [Export] public EnemyDefenseMode Mode { get; set; } = EnemyDefenseMode.ProfileDefault;

    private float _pressureRemaining;
    private int _consecutiveBlockedHits;
    private bool _pendingRiposte;

    // Signals & Feedback
    [Signal] public delegate void RiposteTriggeredEventHandler();
}
```

---

## Call-Stack Flow & Invariants

```text
Player Initiates Attack (Wind-up)
  │
  ├─> EnemyDefenseController.UpdateMeleeThreatObservation()
  │     ├─ Distance <= MeleeThreatDistance (3.8m)?
  │     ├─ Already guarding?
  │     │    └─ YES: Reset _pressureRemaining = GuardPressureWindow (maintain shield!)
  │     └─ NO: Check profile chances
  │          ├─ Mode == ProfileDefault: roll GuardChance / DodgeChance
  │          └─ Roll succeeds: Start ReactionDelay timer (e.g. 0.10s)
  │
  ├─> ReactionDelay Elapses -> TryExecuteMeleeDefense()
  │     ├─ DefenseComponent.TryBeginGuard(faceDir)
  │     └─ Initialize _pressureRemaining = GuardPressureWindow
  │
  ├─> Attack Hits Monster (ApplyMeleeContact)
  │     ├─ Angle between Defender Facing and -IncomingDirection <= 60°?
  │     │    ├─ NO (Flank / Rear Attack):
  │     │    │    └─ Bypass defense completely -> HurtBox resolves full Damaged + Stagger!
  │     │    │
  │     │    └─ YES (Frontal 120°):
  │     │         ├─ Elapsed < ParryWindow (0.21s)?
  │     │         │    ├─ YES: Outcome = Parried (0 dmg, attacker staggered)
  │     │         │    └─ Refresh guard pressure; finish guard hold and release before attempting riposte.
  │     │         │
  │     │         └─ NO: Outcome = Blocked (75% absorbed, 25% chip damage)
  │     │              ├─ Reset _pressureRemaining = GuardPressureWindow
  │     │              └─ Increment _consecutiveBlockedHits
  │
  └─> Pressure Ends (Player stops attacking, _pressureRemaining <= 0)
        ├─ DefenseComponent.EndGuard()
        └─ _consecutiveBlockedHits > 0 && Roll < RiposteChance?
             └─ Trigger Riposte: EnemyBehaviorComponent.TryMeleeAttack()
```

### Invariants:
1. **Directional Truth**: The $120^\circ$ cone is calculated from the defender's actual forward facing `MovementComponent.LookAtDirection` (or `Actor.GlobalTransform.Basis.Z`). Flanking or backstabbing always bypasses guard.
2. **No Infinite Guard Lock**: If the player does not attack for `GuardPressureWindow`, the shield lowers. Even under constant attack, `MaxGuardDuration` (3.0s) forces a guard drop/recovery to prevent unbreakable stalemates.
3. **Riposte Authorization**: A riposte only executes if the enemy is not dead, not staggered by another hit, and has valid melee range to the target.

---

## Affected File Tree

```text
~ scenes/enemies/EnemyBehaviorProfile.cs           + GuardChance, DodgeChance, DefenseReactionDelay, GuardPressureWindow, RiposteChance
~ scenes/enemies/skeleton/skeleton_warrior_behavior.tres  + Configured values for Shield Skeleton
~ scenes/combat/DefenseComponent.cs                ~ Ensure GuardFacing tracks actor orientation dynamically
~ scenes/enemies/EnemyDefenseController.cs         ~ Implement pressure tracking, profile rolls, rear-hit audit, and riposte trigger
~ scenes/debug/CombatHarness.cs           ~ Add "Profile Default" mode, verify rear attacks and riposte feedback
+ tests/EnemyDefenseTest.cs                        + Tests for rear hit penetration, sustained pressure, profile chance, and riposte
```

---

## Verification Plan

### 1. Automated GdUnit4 Tests
- **`RearAttackBypassesGuardAndDealsFullDamage`**: Place player at $(0, 0, 2)$ behind enemy facing $(0, 0, -1)$. Verify damage outcome is `Damaged` (not `Blocked`) and health is reduced by full unblocked amount.
- **`FrontalAttackWithin120DegreesIsBlocked`**: Place player at $(0, 0, -2)$ facing enemy. Verify damage outcome is `Blocked` with chip damage.
- **`FlankAttackAt90DegreesBypasses120DegreeCone`**: Place player at $(2, 0, 0)$ ($90^\circ$ to side). Verify outcome is `Damaged`.
- **`SustainedBlockingMaintainsGuardWhileUnderPressure`**: Deliver two attacks spaced $0.4\text{s}$ apart. Verify guard does not drop between hits.
- **`ParryRiposteWaitsForGuardRelease`**: Deliver an attack during the parry window. Verify the attacker is staggered, guard remains held under pressure, release recovery completes, and only then an in-range target can receive a riposte.
- **`ZeroGuardChanceNeverGuards`**: Set `GuardChance = 0`. Verify enemy takes all attacks unmitigated.

### 2. Build Verification
- `dotnet build "Rogue Gauntlet.sln"` (Debug)
- `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease` (Release)
- Run all test suites: `CombatDefenseTest`, `EnemyDefenseTest`, `MeleeContactTest`, `MainSceneSmokeTest`.
