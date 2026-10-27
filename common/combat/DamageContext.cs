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

/// <summary>How hard a contact hits beyond its damage: poise removed and push on interruption or stagger.</summary>
public readonly record struct AttackImpact(float PoiseDamage, float Knockback)
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
	/// <summary>Poise removed from a defender with poise; see <see cref="PoiseComponent"/>.</summary>
	public float PoiseDamage { get; init; }
	/// <summary>Push strength applied only when the contact interrupts or staggers its defender.</summary>
	public float Knockback { get; init; }

	/// <summary>Both impact values at once, so contact builders copy an attack's impact in one assignment.</summary>
	public AttackImpact Impact
	{
		get => new(PoiseDamage, Knockback);
		init { PoiseDamage = value.PoiseDamage; Knockback = value.Knockback; }
	}
}