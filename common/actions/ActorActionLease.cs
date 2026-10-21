using System;

public enum ActorActionKind
{
	None = 0,
	PrimaryAttack,
	SecondaryTechnique,
	ItemUse,
	Dodge,
	Guard,
	WorldInteraction,
	ForcedReaction
}

public enum ActorActionPhase
{
	None = 0,
	WindUp,
	Active,
	Recovery,
	Channeling,
	Stagger
}

public enum ActionInterruptReason
{
	None = 0,
	NewAction,
	HitStun,
	Damage,
	Death,
	Movement,
	InputReleased,
	Invalidated,
	EquipChanged
}

public enum ActorActionRequestResult
{
	Success = 0,
	Busy,
	Unavailable,
	Cooldown,
	Disabled,
	InvalidDefinition,
	NoDirection
}

public readonly record struct ActorActionLease(ulong Generation, ActorActionKind Kind, ulong LeaseId)
{
	public static readonly ActorActionLease None = new(0, ActorActionKind.None, 0);
	public bool IsValid => Kind != ActorActionKind.None && LeaseId != 0;
}