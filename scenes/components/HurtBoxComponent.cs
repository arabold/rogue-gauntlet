using Godot;
using System;

/// <summary>
/// Defines the area that can be hit or receive damage.
/// </summary>
public partial class HurtBoxComponent : Area3D, IDamageable
{
	// Signal emitted when damage is taken
	[Signal]
	public delegate void DamageTakenEventHandler(float amount, Vector3 attackDirection);

	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public PackedScene HitEffect { get; set; }

	[Export] public float Armor { get; set; } = 0;
	[Export] public bool Invulnerable { get; set; } = false;

	public void TakeDamage(float amount, Vector3 attackDirection)
	{
		if (Invulnerable)
		{
			GD.Print($"{GetParent().Name} is invulnerable!");
			return;
		}

		GD.Print($"{GetParent().Name} took {amount} damage with {Armor} armor");

		var finalDamage = Mathf.Max(0, amount - Armor);
		EmitSignalDamageTaken(finalDamage, attackDirection);
		HealthComponent?.TakeDamage(finalDamage);

		if (finalDamage > 0)
		{
			SpawnHitEffect(attackDirection);
		}
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
