using Godot;
using System;

[Flags]
public enum AttackDefenseTraits
{
	None = 0,
	Dodgeable = 1,
	Guardable = 2,
	Parryable = 4
}

/// <summary>
/// Readable threat language for an attack. Normal attacks allow their authored defenses; Heavy attacks
/// break a guard that does not parry them; Unblockable attacks can only be dodged.
/// </summary>
public enum AttackThreatTier { Normal, Heavy, Unblockable }

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
	public AttackThreatTier ThreatTier { get; init; }
}