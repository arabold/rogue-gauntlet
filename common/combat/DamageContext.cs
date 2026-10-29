using Godot;
using System;

[Flags]
public enum AttackDefenseTraits
{
	None = 0,
	Dodgeable = 1,
	Guardable = 2,
	Parryable = 4,
	/// <summary>An unparried contact against a raised guard breaks it and staggers the defender.</summary>
	BreaksGuard = 8
}

/// <summary>
/// Readable threat label derived from an attack's <see cref="AttackDefenseTraits"/>: Unblockable attacks can be
/// neither guarded nor parried, Heavy attacks break guard, everything else is Normal. Drives telegraphs and
/// the minimum readable wind-up; the traits remain the single source of what a defense can do.
/// </summary>
public enum AttackThreatTier { Normal, Heavy, Unblockable }

public static class AttackThreatRules
{
	public static AttackThreatTier ThreatTier(this AttackDefenseTraits traits) =>
		(traits & (AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable)) == 0 ? AttackThreatTier.Unblockable
		: (traits & AttackDefenseTraits.BreaksGuard) != 0 ? AttackThreatTier.Heavy : AttackThreatTier.Normal;

	/// <summary>A guard both stops the contact and survives it.</summary>
	public static bool IsSafeToGuard(this AttackDefenseTraits traits) =>
		(traits & AttackDefenseTraits.Guardable) != 0 && (traits & AttackDefenseTraits.BreaksGuard) == 0;

	/// <summary>Seconds from attack start to contact a monster attack of this tier needs so a player can read and answer it.</summary>
	public static float MinimumWindUp(this AttackThreatTier tier) => tier switch
	{
		AttackThreatTier.Heavy => .5f,
		AttackThreatTier.Unblockable => .6f,
		_ => .35f
	};
}

/// <summary>
/// How hard a contact hits beyond its damage: poise removed and push on interruption or stagger.
/// <paramref name="ForcePushback"/> also pushes a defender that only flinches (see <see cref="AttackDefinition.ForcePushback"/>).
/// </summary>
public readonly record struct AttackImpact(float PoiseDamage, float Knockback, bool ForcePushback = false)
{
	public static readonly AttackImpact Default = new(AttackDefinition.DefaultPoiseDamage, AttackDefinition.DefaultKnockback);
}

public enum DamageDeliveryKind
{
	DirectMelee,
	Projectile,
	AreaImpact,
	Hazard,
	Environmental
}

/// <summary>Optional elemental identity for protection and resistance; ordinary damage has none.</summary>
public enum DamageElement { None, Fire, Earth, Air, Water }

public readonly record struct AttackIdentity(ulong SourceInstanceId, ulong Sequence);

public readonly record struct DamageContext
{
	public AttackIdentity ExecutionId { get; init; }
	public Node SourceNode { get; init; }
	public Node OwnerNode { get; init; }
	public DamageSourceFlags Faction { get; init; }
	public float Amount { get; init; }
	public RandomNumberGenerator Random { get; init; }
	public Vector3 IncomingDirection { get; init; }
	public AttackDefenseTraits DefenseTraits { get; init; }
	public DamageDeliveryKind DeliveryKind { get; init; }
	public DamageElement Element { get; init; }
	/// <summary>Poise removed from a defender with poise; see <see cref="PoiseComponent"/>.</summary>
	public float PoiseDamage { get; init; }
	/// <summary>Push strength applied only when the contact interrupts or staggers its defender, or forces pushback.</summary>
	public float Knockback { get; init; }
	/// <summary>Outward force on an unblocked contact, even if armor absorbs the damage.</summary>
	public float ForcedPush { get; init; }
	/// <summary>Reflection cannot trigger another guard reflection.</summary>
	public bool IsReflected { get; init; }
	/// <summary>Periodic status damage can bypass armor and avoid interrupting actions.</summary>
	public bool BypassArmor { get; init; }
	public bool SuppressHitReaction { get; init; }
	/// <summary>Push and interrupt a defender that would only flinch, unless it is hyper-armored or resists it.</summary>
	public bool ForcePushback { get; init; }
	/// <summary>The attacker's critical roll succeeded; <see cref="Amount"/> already includes it. Presentation only.</summary>
	public bool Critical { get; init; }

	/// <summary>Both impact values at once, so contact builders copy an attack's impact in one assignment.</summary>
	public AttackImpact Impact
	{
		get => new(PoiseDamage, Knockback, ForcePushback);
		init { PoiseDamage = value.PoiseDamage; Knockback = value.Knockback; ForcePushback = value.ForcePushback; }
	}
}
