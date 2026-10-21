using Godot;

public readonly record struct InteractionReservation(
	IInteractionTarget Target,
	Node3D Actor,
	string OperationId,
	ulong Token,
	ulong TargetRevision
)
{
	public static readonly InteractionReservation None = default;
	public bool IsValid => Target != null && Token != 0;
}