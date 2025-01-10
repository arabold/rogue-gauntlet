using Godot;
using System;

/// <summary>
/// A component that the player can interact with.
/// </summary>
public partial class InteractiveComponent : Node, IInteractive
{
	/// <summary>
	/// Signal emitted when the player interacts with the object.
	/// </summary>
	[Signal]
	public delegate void InteractedEventHandler(Node3D actor);

	public void Interact(Player actor)
	{
		GD.Print($"{GetParent().Name} was interacted with by {actor.Name}");
		EmitSignal(SignalName.Interacted, actor);
	}
}
