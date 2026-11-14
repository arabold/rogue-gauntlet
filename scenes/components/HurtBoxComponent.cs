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
[GlobalClass]
public partial class HurtBoxComponent : Area3D, IDamageable, ICombatDamageable
{
	public event CombatContactFeedbackHandler ContactResolved;
	private readonly System.Collections.Generic.Dictionary<CollisionShape3D, MeshInstance3D> _debugMeshes = [];
	private bool _debugDrawEnabled;
	/// <summary>Every resolved contact: its <see cref="DamageOutcome"/>, final damage and <see cref="DamageEmphasis"/> flags.</summary>
	[Signal] public delegate void DamageResolvedEventHandler(int outcome, float amount, int emphasis);
	[Signal]
	public delegate void DamageTakenEventHandler(float amount, Vector3 attackDirection);
	/// <summary>Presentation hook after an unprotected damaging contact; carries a <see cref="HitReaction"/>.</summary>
	[Signal] public delegate void HitReactedEventHandler(int reaction);

	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public BuffController Buffs { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	/// <summary>
	/// Optional hit-reaction meter. Unassigned keeps the poise-free rule (Diablo II's hit recovery): a hit interrupts the
	/// actor only if it deals at least <see cref="InterruptThreshold"/> of its max health, and not again within
	/// <see cref="InterruptImmunity"/>; smaller hits only make it flinch.
	/// </summary>
	[Export] public PoiseComponent Poise { get; set; }
	/// <summary>Without poise, the share of max health one hit must deal to interrupt this actor; 0 lets every hit interrupt.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float InterruptThreshold { get; set; } = 0.1f;
	/// <summary>Without poise, seconds after an interruption during which further hits only make the actor flinch, so it gets its next action off.</summary>
	[Export(PropertyHint.Range, "0,5,0.05")] public float InterruptImmunity { get; set; } = 1f;
	private ulong _interruptibleFromFrame;
	/// <summary>Optional directional and sneak-attack vulnerability. Unassigned means every contact counts as frontal.</summary>
	[Export] public HitVulnerabilityProfile Vulnerability { get; set; }
	/// <summary>Runtime awareness of the owning actor, registered by its root like a defense evaluator. Null means never unaware.</summary>
	public ICombatAwareness Awareness { get; set; }
	[Export] public PackedScene HitEffect { get; set; }
	[Export] public CombatFeedbackProfile FeedbackProfile { get; set; }
	[Export] public ImpactSurfaceProfile ImpactSurface { get; set; }
	[Export]
	public bool DebugDrawEnabled
	{
		get => _debugDrawEnabled;
		set { _debugDrawEnabled = value; if (IsNodeReady()) RefreshDebugDraw(); }
	}

	[Export] public float Armor { get; set; } = 0;
	[Export] public bool Invulnerable { get; set; } = false;

	public override void _Ready() => RefreshDebugDraw();

	public override void _ExitTree() => ClearDebugDraw();

	private readonly System.Collections.Generic.List<IDefenseEvaluator> _defenseEvaluators = new();
	private ICombatShield _shield;
	/// <summary>Replace the active magic guard; each actor has at most one.</summary>
	public void InstallShield(ICombatShield shield)
	{
		var previous = _shield; _shield = null; previous?.End(); _shield = shield;
	}
	public void RemoveShield(ICombatShield shield)
	{
		if (ReferenceEquals(_shield, shield)) _shield = null;
	}

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
			DeliveryKind = DamageDeliveryKind.DirectMelee,
			// Unauthored sources (traps, scripted hits) carry poise in proportion to their damage.
			PoiseDamage = amount,
			Knockback = AttackDefinition.DefaultKnockback
		};
		return ResolveDamageInternal(context, random);
	}

	private DamageResolution ResolveDamageInternal(in DamageContext contact, RandomNumberGenerator random)
	{
		var snapshot = contact;
		float armorRoll = 0;
		HitReaction? reaction = null;
		var zone = HitZone.Front;
		bool unaware = false;
		float hitStopScale = 1;
		bool reactionSuppressed = false;
		DamageResolution Resolved(DamageOutcome outcome, float amount = 0)
		{
			if (CombatTrace.Enabled) CombatTrace.Write(this, "damage.resolved", ("source", CombatTrace.Identity(snapshot.SourceNode)),
				("owner", CombatTrace.Identity(snapshot.OwnerNode)), ("execution", snapshot.ExecutionId.Sequence),
				("execution_source", snapshot.ExecutionId.SourceInstanceId), ("parent_execution", snapshot.Parent.Sequence),
				("parent_source", snapshot.Parent.SourceInstanceId), ("delivery", snapshot.DeliveryKind.ToString()),
				("traits", snapshot.DefenseTraits.ToString()), ("incoming", CombatTrace.Vector(snapshot.IncomingDirection)),
				("raw_damage", snapshot.Amount), ("armor", Armor),
				("armor_roll", armorRoll), ("poise_damage", snapshot.PoiseDamage), ("reaction", reaction?.ToString() ?? "None"),
				("poise", Poise?.Current), ("zone", zone.ToString()), ("unaware", unaware), ("critical", snapshot.Critical),
				("outcome", outcome.ToString()), ("damage", amount), ("health", HealthComponent?.CurrentHealth));
			var emphasis = outcome != DamageOutcome.Damaged ? DamageEmphasis.None
				: (snapshot.Critical ? DamageEmphasis.Critical : 0) | (zone == HitZone.Rear ? DamageEmphasis.Rear : 0)
					| (unaware ? DamageEmphasis.Sneak : 0);
			EmitSignalDamageResolved((int)outcome, amount, (int)emphasis);
			var feedback = new CombatContactFeedback(snapshot, this, outcome, amount, emphasis, reaction,
				reactionSuppressed, HealthComponent?.IsDead == true, GlobalPosition, snapshot.IncomingDirection,
				snapshot.Feedback ?? FeedbackProfile?.Capture() ?? default);
			PublishContactResolved(in feedback);
			return new DamageResolution(outcome, amount, hitStopScale);
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
					if (defenseEval.Outcome is DamageOutcome.Blocked or DamageOutcome.GuardBroken)
					{
						break;
					}
				}
			}
		}

		static float Roll(RandomNumberGenerator rng, float maximum) =>
			rng?.RandfRange(0, Mathf.Max(0, maximum)) ?? (float)GD.RandRange(0f, Mathf.Max(0, maximum));

		// Blocked and guard-broken contacts both carry the guard's reduced amount; the guard owns their reaction.
		bool isDefended = defenseEval.Outcome is DamageOutcome.Blocked or DamageOutcome.GuardBroken;
		if (Invulnerable) return isDefended ? Resolved(defenseEval.Outcome, 0f) : Resolved(DamageOutcome.Invulnerable, 0f);

		float incomingAmount = isDefended ? defenseEval.Amount : contact.Amount;
		float poiseDamage = contact.PoiseDamage;
		// Read awareness before this contact can alert the defender.
		unaware = !isDefended && Awareness?.IsUnaware == true;
		if (!isDefended && Vulnerability != null && contact.DeliveryKind is DamageDeliveryKind.DirectMelee or DamageDeliveryKind.Projectile)
		{
			zone = HitZones.Classify(Facing, contact.IncomingDirection, Vulnerability.FrontHalfAngle, Vulnerability.RearHalfAngle);
			// Rear and sneak bonuses do not stack; the larger applies.
			incomingAmount *= Mathf.Max(zone == HitZone.Rear ? Vulnerability.RearDamageMultiplier : 1, unaware ? Vulnerability.UnawareDamageMultiplier : 1);
			if (zone == HitZone.Rear) poiseDamage *= Vulnerability.RearPoiseMultiplier;
			if (unaware && Vulnerability.UnawareBreaksPoise) poiseDamage = float.PositiveInfinity;
			if (zone == HitZone.Rear || unaware) hitStopScale = Vulnerability.EmphasisHitStopScale;
		}
		// A staggered defender (poise break or parry) is the punish window: its hits land harder before armor.
		if (Poise?.IsAvailable == true && Poise.ActionGate.IsStaggered)
			incomingAmount *= Poise.Definition.StaggeredDamageMultiplier;
		// Armor stops physical blows only: elemental damage (missiles, spells, burning) passes it.
		armorRoll = contact.BypassArmor || contact.Element != DamageElement.None ? 0 : Roll(random, Armor);
		float finalDamage = Mathf.Max(0, incomingAmount - armorRoll);
		if (unaware) Awareness.NoticeAttacker(contact.OwnerNode);
		// Capture the guard before absorption: the final hit can deplete it but still reflects.
		var shield = _shield;
		float absorbed = shield?.Absorb(finalDamage) ?? 0;
		finalDamage = Mathf.Max(0, finalDamage - absorbed);
		DamageResolution Complete(DamageOutcome outcome, float amount = 0)
		{
			var resolution = Resolved(outcome, amount);
			if (absorbed > 0) shield?.Reflect(absorbed, in snapshot);
			return resolution;
		}

		if (finalDamage <= 0)
		{
			if (!isDefended && float.IsFinite(contact.ForcedPush) && contact.ForcedPush > 0) MovementComponent?.PushForced(contact.IncomingDirection, contact.ForcedPush);
			return isDefended ? Complete(defenseEval.Outcome, 0f) : Complete(DamageOutcome.Absorbed, 0f);
		}

		// Death can clear a status during TakeDamage; preserve the contact's reaction rules.
		bool statusLocked = MovementComponent?.ActionGate?.IsStatusLocked == true;
		EmitSignalDamageTaken(finalDamage, contact.IncomingDirection);
		HealthComponent?.TakeDamage(finalDamage);

		reactionSuppressed = defenseEval.SuppressHitStun || statusLocked || contact.SuppressHitReaction;
		if (!reactionSuppressed)
			reaction = ReactToHit(contact, poiseDamage, finalDamage);
		if (!isDefended && float.IsFinite(contact.ForcedPush) && contact.ForcedPush > 0 && HealthComponent?.IsDead != true)
			MovementComponent?.PushForced(contact.IncomingDirection, contact.ForcedPush);
		if (!isDefended && HealthComponent?.IsDead != true
			&& contact.DeliveryKind is DamageDeliveryKind.DirectMelee or DamageDeliveryKind.Projectile)
			ApplyOnHitStatuses(contact, random);

		return isDefended ? Complete(defenseEval.Outcome, finalDamage) : Complete(DamageOutcome.Damaged, finalDamage);
	}

	/// <summary>Roll authored payloads only after damage has survived defenses, armor, shields and death.</summary>
	private void ApplyOnHitStatuses(in DamageContext contact, RandomNumberGenerator random)
	{
		if (Buffs == null || contact.OnHitStatuses == null) return;
		foreach (var status in contact.OnHitStatuses)
		{
			if (status?.Effect == null) continue;
			float roll = status.Chance >= 1 ? 0 : random?.Randf() ?? 1;
			bool chancePassed = status.Chance >= 1 || (random != null && roll < status.Chance);
			BuffApplication application = chancePassed
				? Buffs.ApplyBuff(status.Effect)
				: BuffApplication.Rejected;
			if (CombatTrace.Enabled)
				CombatTrace.Write(this, "status.resolved", ("execution_source", contact.ExecutionId.SourceInstanceId),
					("execution", contact.ExecutionId.Sequence),
					("key", status.Effect.RefreshKey), ("chance", status.Chance), ("roll", roll),
					("chance_passed", chancePassed),
					("applied", application is BuffApplication.Applied or BuffApplication.Refreshed),
					("result", application.ToString()));
		}
	}

	/// <summary>The actor's facing: its movement's look direction, or the hurtbox's own forward without one.</summary>
	private Vector3 Facing => MovementComponent?.LookAtDirection ?? -GlobalTransform.Basis.Z;

	/// <summary>
	/// Poise decides whether an unprotected hit flinches or staggers. Without poise, a hit interrupts the
	/// current action unless the gate reports hyper-armor. Interruptions and staggers push; a forced pushback
	/// also pushes and interrupts a flinch, unless the defender is hyper-armored or resists it. A running
	/// stagger is pushed but never cut short.
	/// </summary>
	/// <summary>Without poise: a hit big enough for <see cref="InterruptThreshold"/>, once the last interruption's immunity has passed.</summary>
	private bool Interrupts(float damage) => Engine.GetPhysicsFrames() >= _interruptibleFromFrame
		&& damage >= InterruptThreshold * (HealthComponent?.MaxHealth ?? 0);

	private HitReaction ReactToHit(in DamageContext contact, float poiseDamage, float damage)
	{
		var gate = MovementComponent?.ActionGate;
		HitReaction reaction;
		if (Poise?.IsAvailable == true)
			reaction = Poise.Apply(poiseDamage) ? HitReaction.Staggered : HitReaction.Flinch;
		else if (gate?.IsHyperArmored == true || !Interrupts(damage))
			reaction = HitReaction.Flinch;
		else
		{
			gate?.CancelCurrent(ActionInterruptReason.HitStun);
			_interruptibleFromFrame = Engine.GetPhysicsFrames() + (ulong)Mathf.CeilToInt(InterruptImmunity * Engine.PhysicsTicksPerSecond);
			reaction = HitReaction.Interrupted;
		}
		bool forced = reaction == HitReaction.Flinch && contact.ForcePushback && gate?.IsHyperArmored != true
			&& Poise?.Definition?.ResistsForcedPushback != true;
		if (forced && gate?.IsStaggered != true)
		{
			gate?.CancelCurrent(ActionInterruptReason.HitStun);
			reaction = HitReaction.Interrupted;
		}
		if (reaction != HitReaction.Flinch || forced) MovementComponent?.Push(contact.IncomingDirection, contact.Knockback);
		EmitSignalHitReacted((int)reaction);
		return reaction;
	}


	private void PublishContactResolved(in CombatContactFeedback feedback)
	{
		if (ContactResolved == null && feedback.Outcome == DamageOutcome.Damaged && !feedback.ReactionSuppressed)
			SpawnHitEffect(feedback.Context.IncomingDirection);
		if (ContactResolved != null)
		{
			foreach (CombatContactFeedbackHandler handler in ContactResolved.GetInvocationList())
			{
				try { handler(in feedback); }
				catch (System.Exception error)
				{
					ContactResolved -= handler;
					GD.PushWarning($"Combat contact presentation listener disabled: {error.Message}");
				}
			}
		}
		SignalBus.EmitCombatContactResolved(in feedback);
	}

	/// <summary>Keep the authored generic hit burst for damageable props without an actor feedback component.</summary>
	private void SpawnHitEffect(Vector3 attackDirection)
	{
		if (HitEffect == null || !IsInsideTree()) return;
		var particles = ScenePool.Spawn<GpuParticles3D>(HitEffect, this);
		particles.Position = attackDirection.Normalized() * 0.5f;
		particles.ResetPhysicsInterpolation();
		particles.OneShot = true;
		particles.Restart();
		particles.Emitting = true;
	}

	private void RefreshDebugDraw()
	{
		if (!DebugDrawEnabled) { ClearDebugDraw(); return; }
		foreach (Node child in GetChildren())
		{
			if (child is not CollisionShape3D shape || shape.Shape == null || _debugMeshes.ContainsKey(shape)) continue;
			var mesh = new MeshInstance3D
			{
				Name = "HurtboxDebugMesh",
				Mesh = shape.Shape.GetDebugMesh(),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = new Color(0.1f, 0.8f, 1f, 0.28f),
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
					NoDepthTest = true
				}
			};
			shape.AddChild(mesh);
			_debugMeshes.Add(shape, mesh);
		}
	}

	private void ClearDebugDraw()
	{
		foreach (MeshInstance3D mesh in _debugMeshes.Values)
			if (GodotObject.IsInstanceValid(mesh)) mesh.QueueFree();
		_debugMeshes.Clear();
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
