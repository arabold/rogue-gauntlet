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
	[Export] public bool Invulnerable { get; set; } = false;

	private readonly System.Collections.Generic.List<IDefenseEvaluator> _defenseEvaluators = new();

	public void RegisterDefenseEvaluator(IDefenseEvaluator evaluator)
	{
		if (evaluator != null && !_defenseEvaluators.Contains(evaluator))
		{
			_defenseEvaluators.Add(evaluator);
		}
	}

	public void UnregisterDefenseEvaluator(IDefenseEvaluator evaluator)
	{
		if (evaluator != null)
		{
			_defenseEvaluators.Remove(evaluator);
		}
	}


	/// <summary>
	/// Bitmask defining which factions/sources are allowed to damage this hurtbox.
	/// </summary>
	[Export] public DamageSourceFlags DamageFilter { get; set; } = DamageSourceFlags.Player | DamageSourceFlags.Boss | DamageSourceFlags.Environment;

	/// <summary>Preserves the common damage contract while publishing the resolved contact outcome.</summary>
	public void TakeDamage(float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		ResolveDamage(amount, attackDirection, attacker, attackerFaction);
	}

	public DamageResolution ResolveDamage(in DamageContext contact)
	{
		return ResolveDamageInternal(contact, contact.Random);
	}

	/// <summary>Resolve defense separately from geometric contact; melee supplies its actor-owned RNG.</summary>
	public DamageResolution ResolveDamage(float amount, Vector3 attackDirection, Node attacker = null,
		DamageSourceFlags attackerFaction = DamageSourceFlags.None, RandomNumberGenerator random = null)
	{
		var context = new DamageContext
		{
			Amount = amount,
			IncomingDirection = attackDirection,
			SourceNode = attacker,
			OwnerNode = attacker,
			Faction = attackerFaction != DamageSourceFlags.None ? attackerFaction : GetDamageSource(attacker),
			DefenseTraits = AttackDefenseTraits.None,
			DeliveryKind = DamageDeliveryKind.DirectMelee
		};
		return ResolveDamageInternal(context, random);
	}

	private DamageResolution ResolveDamageInternal(in DamageContext contact, RandomNumberGenerator random)
	{
		var snapshot = contact;
		float armorRoll = 0;
		DamageResolution Resolved(DamageOutcome outcome, float amount = 0)
		{
			if (CombatTrace.Enabled) CombatTrace.Write(this, "damage.resolved", ("source", CombatTrace.Identity(snapshot.SourceNode)),
				("owner", CombatTrace.Identity(snapshot.OwnerNode)), ("execution", snapshot.ExecutionId.Sequence),
				("execution_source", snapshot.ExecutionId.SourceInstanceId), ("delivery", snapshot.DeliveryKind.ToString()),
				("traits", snapshot.DefenseTraits.ToString()), ("incoming", CombatTrace.Vector(snapshot.IncomingDirection)),
				("raw_damage", snapshot.Amount), ("armor", Armor),
				("armor_roll", armorRoll),
				("outcome", outcome.ToString()), ("damage", amount), ("health", HealthComponent?.CurrentHealth));
			return Report(outcome, amount);
		}
		var attackerFaction = contact.Faction != DamageSourceFlags.None ? contact.Faction : GetDamageSource(contact.OwnerNode ?? contact.SourceNode);
		if ((DamageFilter & attackerFaction) == 0) return Resolved(DamageOutcome.Filtered);
		if (HealthComponent != null && HealthComponent.IsDead) return Resolved(DamageOutcome.Filtered);


		DefenseEvaluation defenseEval = default;
		if (contact.DefenseTraits != AttackDefenseTraits.None)
		{
			for (int i = 0; i < _defenseEvaluators.Count; i++)
			{
				if (_defenseEvaluators[i].TryEvaluate(in contact, out defenseEval))
				{
					if (defenseEval.Outcome == DamageOutcome.Dodged)
					{
						return Resolved(DamageOutcome.Dodged, 0f);
					}
					if (defenseEval.Outcome == DamageOutcome.Parried)
					{
						return Resolved(DamageOutcome.Parried, 0f);
					}
					if (defenseEval.Outcome == DamageOutcome.Blocked)
					{
						break;
					}
				}
			}
		}

		static float Roll(RandomNumberGenerator rng, float maximum) =>
			rng?.RandfRange(0, Mathf.Max(0, maximum)) ?? (float)GD.RandRange(0f, Mathf.Max(0, maximum));

		bool isBlocked = defenseEval.Outcome == DamageOutcome.Blocked;
		if (Invulnerable) return isBlocked ? Resolved(DamageOutcome.Blocked, 0f) : Resolved(DamageOutcome.Invulnerable, 0f);

		float incomingAmount = isBlocked ? defenseEval.Amount : contact.Amount;
		armorRoll = Roll(random, Armor);
		float finalDamage = Mathf.Max(0, incomingAmount - armorRoll);

		if (finalDamage <= 0)
		{
			return isBlocked ? Resolved(DamageOutcome.Blocked, 0f) : Resolved(DamageOutcome.Absorbed, 0f);
		}

		EmitSignalDamageTaken(finalDamage, contact.IncomingDirection);
		HealthComponent?.TakeDamage(finalDamage);

		if (!defenseEval.SuppressHitStun)
		{
			MovementComponent?.ActionGate?.CancelCurrent(ActionInterruptReason.HitStun);
			MovementComponent?.Push(contact.IncomingDirection, 3);
			SpawnHitEffect(contact.IncomingDirection);
		}

		return isBlocked ? Resolved(DamageOutcome.Blocked, finalDamage) : Resolved(DamageOutcome.Damaged, finalDamage);
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