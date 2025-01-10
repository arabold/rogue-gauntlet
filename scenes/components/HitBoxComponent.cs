using Godot;
using System;

/// <summary>
/// Defines the area where an attack can register a hit.
/// </summary>
public partial class HitBoxComponent : Area3D
{
	// Signal emitted when a hit is detected
	[Signal]
	public delegate void HitDetectedEventHandler(Node3D damageable);

	private Node3D _parent;

	public override void _Ready()
	{
		_parent = GetParent<Node3D>();
		AreaEntered += OnAreaEntered;
		BodyEntered += OnBodyEntered;
	}

	private void OnAreaEntered(Area3D area)
	{
		if (area is IDamageable damageable)
		{
			EmitSignal(SignalName.HitDetected, (Node3D)damageable);
		}
	}

	private void OnBodyEntered(Node3D node)
	{
		if (node is IDamageable damageable)
		{
			EmitSignal(SignalName.HitDetected, (Node3D)damageable);
		}
	}
}
