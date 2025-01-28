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
	[Export] public PackedScene HitEffect { get; set; }

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		GD.Print($"{GetParent().Name} took {amount} damage");
		EmitSignalDamageTaken(amount, attackDirection);
		HealthComponent?.TakeDamage(amount);

		SpawnHitEffect(attackDirection);
	}

	private void SpawnHitEffect(Vector3 attackDirection)
	{
		if (HitEffect == null)
		{
			return;
		}

		// TODO: Use object pooling
		var hitEffect = HitEffect.Instantiate<GpuParticles3D>();
		hitEffect.Position = attackDirection.Normalized() * 0.5f;
		hitEffect.OneShot = true;
		AddChild(hitEffect);
	}
}
