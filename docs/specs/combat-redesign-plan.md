# Combat redesign: remaining player kit

Status: slices 1–4 are implemented: poise and hit-stop, committed monster wind-ups, rear and sneak hits, dodging through monsters, attack slots with an engagement ring, threat tiers with guard break and telegraphs, and strike-aligned monster contact windows. [Combat actions and defense](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) describe them; [monster authoring](../monster-authoring-design.md) covers their authoring. This plan records only the remaining player-kit slice and a Warden guard-breaking attack; the [roadmap](../development-roadmap.md) owns priority. When it lands, move its rules into those documents and remove this plan.

## Goal

Keep the controls simple, but make every exchange follow **telegraph → respond → punish**. The existing dedicated inputs (primary, secondary, dodge, guard, interact, four item slots) stay as they are. There is no jump, no separate parry button and no lock-on button. The monster side now telegraphs, commits and takes turns; the player side still has one basic attack per weapon, unbuffered presses, a single dodge with a 0.6 s cooldown and contact windows that open before the visible strike.

## Rules this plan overturns

| Current rule | Where | Replacement |
| --- | --- | --- |
| "Dodge or guard can replace attack startup; dodge can also replace attack recovery." | [Action ownership](../combat-system.md#action-ownership) | Recovery may also be replaced by guard, and by the next primary step inside its combo window. |
| Player dodge "0.6-second cooldown from acceptance". | [Dodge, guard and parry](../combat-system.md#dodge-guard-and-parry) | Dodge charges, plus a short minimum interval between dodges. |

## Rules retained

- There is no passive evasion or accuracy roll and no random hit rejection.
- Melee stays horizontal with one vertical contact envelope. There is no jump and no height-based avoidance.
- Each actor owns one committed action through `ActorActionGateComponent` leases. Stale callbacks cannot complete or cancel newer actions.
- Capabilities are composed explicitly in scenes; an unassigned optional reference makes that capability unavailable.

## Player kit

**What lands**

- Primary attacks become a three-step combo per weapon family, with a stronger finisher.
- A 0.2 s input buffer for fresh presses.
- Guard or the next combo step may cancel recovery.
- Dodge has two charges and an optional perfect-dodge refund.
- Guard allows slow walking where the rig has a matching animation.
- Player contact windows match the visible strike without making attacks feel slower.

**Contracts**

```csharp
// + scenes/attacks/AttackComboStep.cs
[GlobalClass] public partial class AttackComboStep : Resource
{
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public float Duration { get; set; }        // 0 = clip length
	[Export] public float DamageScale { get; set; } = 1f;
}
// + scenes/attacks/AttackCombo.cs
[GlobalClass] public partial class AttackCombo : Resource
{
	[Export] public Godot.Collections.Array<AttackComboStep> Steps { get; set; } = [];
	[Export] public float ChainWindow { get; set; } = 0.35f; // seconds after recovery starts
}
// ~ Weapon: [Export] public AttackCombo PrimaryCombo { get; set; } — null keeps today's single primary attack

// + scenes/player/InputIntentBuffer.cs — plain C#
public enum PlayerIntent { Primary, Secondary, Dodge, GuardPress }
public sealed class InputIntentBuffer(double window)
{
	public void Push(PlayerIntent intent, double now);
	public bool TryConsume(PlayerIntent intent, double now);
	public void Clear();
}

// ~ DodgeDefinition
[Export] public int MaxCharges { get; set; } = 1;            // 1 + CooldownDuration = today's behavior
[Export] public float ChargeRechargeDuration { get; set; }   // per charge
[Export] public float PerfectWindow { get; set; }            // evading inside this many seconds of acceptance refunds a charge; 0 = off
// CooldownDuration becomes the minimum interval between dodges.
```

**Behavior details**

- **Combo index.** `PlayerAttackController` owns it. The index advances only when the previous step finished, or is in recovery inside `ChainWindow`. It resets on interruption, equipment change, a dodge or the window expiring.
- **Recovery cancels.** `ActorActionGateComponent.CanTransition` allows `Recovery` → `Guard` and `Recovery` → `PrimaryAttack`. The controller only requests the latter for a valid chain. Monster AI already waits for attack completion.
- **Charged secondary.** Deferred: start with the plain heavy plus hyper-armor, and add charging only if playtests ask for it.
- **Player contact timing.** Every `kaykit_player.tres` style also uses `ContactStart = 0.3`, and the axe and blunt styles share the skeleton chop clip, whose strike is at 50–62%. Retune each style's window with `sample_attack_clip.gd` together with the weapon perform durations, so later contact does not make the player's attacks feel slower.

**Persistence:** replace `SaveGame.DodgeCooldownRemaining` with `DodgeChargesRemaining`, `DodgeRechargeRemaining` and `DodgeIntervalRemaining`. Bump `SaveGame.CurrentVersion` and reject older saves without migration, following [save compatibility](../randomness-and-seeds.md#save-compatibility-and-acceptance).

**Verification**

- `InputIntentBufferTest` (plain C#).
- Combo chaining and reset rules.
- Gate transition table in `ActorActionGateTest`.
- Charges, recharge and perfect-dodge refund.
- `PlayerSaveCodecTest` and `SaveGameVersionTest` round trips.
- A harness review of buffered dodges out of a combo.

## Warden guard-breaking attack

The Warden's slash is now Normal, so blocking it and countering during its recovery works. Guard break then has no monster user. Give the Warden a second, clearly different attack so guarding him stays a choice rather than a habit:

- **Overhead slam:** `BreaksGuard` plus Guardable, Parryable and Dodgeable traits, so it reads as Heavy with the orange footprint through the already-wired `AttackTelegraph`. A wind-up of at least the Heavy minimum (0.5 s), ideally longer than the slash, and hyper-armor through the swing. It needs a distinct clip or a slower playback of an existing one.
- **Attack selection:** monsters currently have one `EnemyAttackStrategy`. Add an ordered list of attack policies on `EnemyBehaviorProfile` with an authored choice rule (weights, cooldown per entry, optional "target is guarding" preference from visible guard state, never player input). The executor, slots, validator and defense observation keep working per accepted attack.
- **Verification:** validator coverage for every listed attack, a harness review of block → counter against the slash and parry/dodge against the slam, and the authoring guide updated for multiple attacks.

## Delivery and verification

One vertical slice. Defaults reproduce current behavior until a weapon opts into a combo or a dodge definition into charges. Run:

1. `dotnet build "Rogue Gauntlet.sln"`.
2. The targeted GdUnit4 tests (`dotnet test "Rogue Gauntlet.csproj" --settings .runsettings`).
3. A new or updated JSON scenario under `scenes/debug/combat/scenarios/`.
4. A headless `main.tscn` smoke.
5. An editor playtest for feel.

## Affected files

```text
scenes/actions/ActorActionGateComponent.cs      ~ recovery transitions
scenes/attacks/
  + AttackCombo.cs, AttackComboStep.cs
  ~ animation_profiles/kaykit_player.tres       contact windows on the visible strike
scenes/combat/DodgeDefinition.cs, DodgeComponent.cs   ~ charges, perfect window
scenes/combat/definitions/player_dodge.tres, weapon_guard.tres, shield_guard.tres   ~ charges, guard walking
scenes/items/Weapon.cs                          ~ PrimaryCombo; perform durations retuned with contact timing
scenes/player/
  ~ PlayerAttackController.cs                   combo index
  ~ PlayerInputController.cs                    intent buffer, recovery cancels
  + InputIntentBuffer.cs
scripts/saves/SaveGame.cs, PlayerSaveCodec.cs   ~ dodge charges, version bump
tests/                                          + InputIntentBufferTest; ~ ActorActionGateTest, PlayerSaveCodecTest, SaveGameVersionTest
docs/                                           ~ combat-system.md, weapon-attack-system.md when the slice lands
```

## Starting tuning

| Knob | Value |
| --- | --- |
| Dodge | 2 charges, 1.2 s recharge each, 0.25 s minimum interval, 0.10 s perfect window |
| Input buffer | 0.2 s |
| Combo chain window | 0.35 s after recovery starts |

## Open decisions and risks

- **Guard-walk animation.** KayKit rigs may lack a guarded walk. Keep `MovementSpeedMultiplier` at 0 where no matching clip exists.
- **Charged secondary and depth-scaled attack slots.** Deferred until playtests show a need.
- **Playtest review of slices 1–4.** Engagement-ring navigation and ring spacing in generated rooms, dodging through monsters next to walls, sneak-attack frequency, Warden guard-break and riposte pacing, and whether flinches read clearly enough (a material flash or additive hit-react blend remains open). Headless runs cannot bake a navmesh, so ring movement needs the editor.
- **Further multi-attack content.** After the Warden slam, the same attack selection serves Unblockable area monsters and other tier mixes.
- **Player rear vulnerability.** The player has no vulnerability profile; revisit if being surrounded is not dangerous enough.
