using Godot;
using System;

/// <summary>
/// Defines the area that can be hit or receive damage.
/// </summary>
public partial class HurtBoxComponent : Area3D, IDamageable
{
	// Signal emitted when damage is taken
	[Signal]
	public delegate void DamageTakenEventHandler(int amount, Vector3 attackDirection);

	[Export] public HealthComponent HealthComponent { get; set; }

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		GD.Print($"{GetParent().Name} took {amount} damage");
		EmitSignal(SignalName.DamageTaken, amount, attackDirection);
		HealthComponent?.TakeDamage(amount);
	}
}
