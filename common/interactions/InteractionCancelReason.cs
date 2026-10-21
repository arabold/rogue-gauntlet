public enum InteractionCancelReason
{
	None = 0,
	InputReleased = 1,
	Movement = 2,
	CombatIntent = 3,
	Damage = 4,
	TargetInvalidated = 5,
	OutOfRange = 6,
	ActorDied = 7,
	ActionInterrupted = 8
}