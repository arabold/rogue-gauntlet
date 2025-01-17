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

	public override void _Ready()
	{
		AreaEntered += OnAreaEntered;
		BodyEntered += OnBodyEntered;
	}

	private void OnAreaEntered(Area3D area)
	{
		EmitSignal(SignalName.HitDetected, area);
	}

	private void OnBodyEntered(Node3D node)
	{
		EmitSignal(SignalName.HitDetected, node);
	}
}
