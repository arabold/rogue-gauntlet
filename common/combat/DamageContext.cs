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
}