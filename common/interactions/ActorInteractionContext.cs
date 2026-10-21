using Godot;

public readonly record struct ActorInteractionContext(
	Node3D Actor,
	ActorCapabilities Capabilities = null,
	Inventory Inventory = null
)
{
	public bool HasPermission(InteractionPermission permission)
	{
		if (permission == InteractionPermission.None) return true;
		return Capabilities?.HasPermission(permission) ?? false;
	}
}