using Godot;

/// <summary>
/// Actor-neutral offer/reserve/commit/release contract for interactives in the world.
/// </summary>
public interface IInteractionTarget
{
	InteractionOffer GetOffer(in ActorInteractionContext actor);
	bool TryReserve(in InteractionOffer offer, Node3D actor, out InteractionReservation reservation);
	InteractionCommitResult TryCommit(in InteractionReservation reservation);
	void Release(in InteractionReservation reservation);
	Node3D TargetNode { get; }
	bool IsInteractive { get; }
}