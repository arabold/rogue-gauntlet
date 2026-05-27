using Godot;
[System.Flags]
public enum DamageSourceFlags
{
	None = 0,
	Player = 1,
	Enemy = 2,
	Environment = 4,
	Boss = 8,
	Trap = 16,
	All = Player | Enemy | Environment | Boss | Trap
}

/// <summary>
/// Defines the area that can be hit or receive damage.
/// </summary>
public partial class HurtBoxComponent : Area3D, IDamageable
{
	// Signal emitted when damage is taken
	[Signal]
	public delegate void DamageTakenEventHandler(float amount, Vector3 attackDirection);

	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public PackedScene HitEffect { get; set; }

	[Export] public float Armor { get; set; } = 0;
	[Export] public float Evasion { get; set; } = 0;
	[Export] public bool Invulnerable { get; set; } = false;

	/// <summary>
	/// Bitmask defining which factions/sources are allowed to damage this hurtbox.
	/// </summary>
	[Export] public DamageSourceFlags DamageFilter { get; set; } = DamageSourceFlags.Player | DamageSourceFlags.Boss | DamageSourceFlags.Environment;

	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null)
	{
		DamageSourceFlags attackerFaction = DamageSourceFlags.Environment;
		if (attacker != null)
		{
			if (attacker.IsInGroup("player"))
			{
				attackerFaction = DamageSourceFlags.Player;
			}
			else if (attacker.IsInGroup("boss"))
			{
				attackerFaction = DamageSourceFlags.Boss;
			}
			else if (attacker.IsInGroup("enemy"))
			{
				attackerFaction = DamageSourceFlags.Enemy;
			}
			else if (attacker.IsInGroup("trap") || attacker is FloorTrap)
			{
				attackerFaction = DamageSourceFlags.Trap;
			}
		}

		if ((DamageFilter & attackerFaction) == 0)
		{
			GD.Print($"{GetParent().Name}'s hurtbox filtered out damage from {attacker?.Name ?? "unknown"} ({attackerFaction})");
			return;
		}

		var attack = GD.RandRange(0f, accuracy);
		var defense = GD.RandRange(0f, Evasion);
		if (defense > attack)
		{
			GD.Print($"{GetParent().Name} evaded the attack!");
			return;
		}

		if (Invulnerable)
		{
			GD.Print($"{GetParent().Name} is invulnerable!");
			return;
		}

		var armor = (float)GD.RandRange(0f, Armor);
		GD.Print($"{GetParent().Name} took {amount} damage with {armor} armor");

		var finalDamage = Mathf.Max(0, amount - armor);
		if (finalDamage > 0)
		{
			EmitSignalDamageTaken(finalDamage, attackDirection);
			HealthComponent?.TakeDamage(finalDamage);
			MovementComponent?.Push(attackDirection, 3.0f);
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
