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
public partial class HurtBoxComponent : Area3D, IDamageable, ICombatDamageable
{
	// Signal emitted when damage is taken
	[Signal] public delegate void DamageResolvedEventHandler(int outcome, float amount);
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

	/// <summary>Preserves the common damage contract while publishing the resolved contact outcome.</summary>
	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		ResolveDamage(accuracy, amount, attackDirection, attacker, attackerFaction);
	}

	public DamageResolution ResolveDamage(in DamageContext contact)
	{
		return ResolveDamage(contact.Accuracy, contact.Amount, contact.IncomingDirection, contact.SourceNode, contact.Faction);
	}

	/// <summary>Resolve defense separately from geometric contact; melee supplies its actor-owned RNG.</summary>
	public DamageResolution ResolveDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null,
		DamageSourceFlags attackerFaction = DamageSourceFlags.None, RandomNumberGenerator random = null)
	{
		if (attackerFaction == DamageSourceFlags.None) attackerFaction = GetDamageSource(attacker);
		if ((DamageFilter & attackerFaction) == 0) return Report(DamageOutcome.Filtered);
		float Roll(float maximum) => random?.RandfRange(0, Mathf.Max(0, maximum)) ?? (float)GD.RandRange(0f, Mathf.Max(0, maximum));
		if (Roll(Evasion) > Roll(accuracy)) return Report(DamageOutcome.Evaded);
		if (Invulnerable) return Report(DamageOutcome.Invulnerable);
		float finalDamage = Mathf.Max(0, amount - Roll(Armor));
		if (finalDamage <= 0) return Report(DamageOutcome.Absorbed);
		EmitSignalDamageTaken(finalDamage, attackDirection);
		HealthComponent?.TakeDamage(finalDamage);
		MovementComponent?.Push(attackDirection, 3);
		SpawnHitEffect(attackDirection);
		return Report(DamageOutcome.Damaged, finalDamage);
	}

	private DamageResolution Report(DamageOutcome outcome, float amount = 0)
	{
		EmitSignalDamageResolved((int)outcome, amount);
		return new DamageResolution(outcome, amount);
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