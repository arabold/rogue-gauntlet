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

	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		if (attackerFaction == DamageSourceFlags.None)
		{
			attackerFaction = GetDamageSource(attacker);
		}

		if ((DamageFilter & attackerFaction) == 0)
		{
			GameDebug.Combat($"{GetParent().Name}'s hurtbox filtered out damage from {GetAttackerName(attacker)} ({attackerFaction})");
			return;
		}

		var attack = GD.RandRange(0f, accuracy);
		var defense = GD.RandRange(0f, Evasion);
		if (defense > attack)
		{
			GameDebug.Combat($"{GetParent().Name} evaded the attack!");
			return;
		}

		if (Invulnerable)
		{
			GameDebug.Combat($"{GetParent().Name} is invulnerable!");
			return;
		}

		var armor = (float)GD.RandRange(0f, Armor);
		GameDebug.Combat($"{GetParent().Name} took {amount} damage with {armor} armor");

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

		var hitEffect = ScenePool.Spawn<GpuParticles3D>(HitEffect, this);
		hitEffect.Position = attackDirection.Normalized() * 0.5f;
		hitEffect.OneShot = true;
	}

	public static DamageSourceFlags GetDamageSource(Node attacker)
	{
		if (attacker == null)
		{
			return DamageSourceFlags.Environment;
		}

		if (!GodotObject.IsInstanceValid(attacker))
		{
			return DamageSourceFlags.None;
		}

		if (attacker.IsInGroup("player"))
		{
			return DamageSourceFlags.Player;
		}

		if (attacker.IsInGroup("boss"))
		{
			return DamageSourceFlags.Boss;
		}

		if (attacker.IsInGroup("enemy"))
		{
			return DamageSourceFlags.Enemy;
		}

		if (attacker.IsInGroup("trap") || attacker is FloorTrap)
		{
			return DamageSourceFlags.Trap;
		}

		return DamageSourceFlags.Environment;
	}

	private static string GetAttackerName(Node attacker)
	{
		if (attacker == null)
		{
			return "unknown";
		}

		return GodotObject.IsInstanceValid(attacker) ? attacker.Name : "invalid";
	}
}
