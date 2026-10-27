using System.Collections.Generic;
using Godot;

public enum EnemyDefenseMode
{
	ProfileDefault, // Uses EnemyBehaviorProfile chances & tuning (production game mode)
	Dodge,          // Forced dodge (100% chance, for harness/debug testing)
	GuardParry,     // Forced guard/parry (100% chance, for harness/debug testing)
	None            // Disabled defense
}

/// <summary>
/// Observes visible threats (opponent attack wind-up and incoming projectiles)
/// and triggers active defense via composed DodgeComponent and/or GuardComponent.
/// Supports continuous combo blocking and riposte counter-attacks on guard release.
/// </summary>
public partial class EnemyDefenseController : Node
{
	[Signal] public delegate void RiposteTriggeredEventHandler();

	[Export] public EnemyBase Enemy { get; set; }

	private GuardComponent _guardComponent;
	[Export]
	public GuardComponent GuardComponent
	{
		get => _guardComponent;
		set => SetGuardComponent(value);
	}

	[Export] public DodgeComponent DodgeComponent { get; set; }
	[Export] public Node3D Target { get; set; }
	[Export] public EnemyDefenseMode Mode { get; set; } = EnemyDefenseMode.ProfileDefault;

	public bool HasGuardCapability => GuardComponent != null;
	public bool HasDodgeCapability => DodgeComponent != null;
	public bool IsGuarding => GuardComponent?.IsGuarding ?? false;
	public bool IsDodging => DodgeComponent?.IsDodging ?? false;

	/// <summary>Maximum distance from attacker to consider a melee attack threatening.</summary>
	[Export] public float MeleeThreatDistance { get; set; } = 3.8f;

	/// <summary>Fallback reaction delay in seconds before an enemy reacts to a perceived attack wind-up.</summary>
	[Export] public float ReactionDelay { get; set; } = 0.04f;

	/// <summary>Distance within which incoming projectiles are tracked.</summary>
	[Export] public float ProjectileDetectionRadius { get; set; } = 8.0f;

	/// <summary>Distance at which an incoming projectile triggers a dodge or guard.</summary>
	[Export] public float ProjectileThreatDistance { get; set; } = 4.5f;

	/// <summary>Fallback duration in seconds to hold guard against an attack before lowering.</summary>
	[Export] public float GuardHoldDuration { get; set; } = 0.55f;

	private EnemyDefenseStrategy Strategy => Enemy?.EnemyBehaviorComponent?.Profile?.DefenseStrategy;
	private float PressureWindow => Strategy?.GuardPressureWindow ?? GuardHoldDuration;

	private AttackIdentity _lastObservedMeleeExecution;
	private float _meleeReactionTimer;
	private Node3D _pendingMeleeAttacker;
	private AttackController _pendingMeleeController;
	private EnemyDefenseMode _chosenMeleeDefense;

	private float _pressureRemaining;
	private float _totalGuardTime;
	private int _consecutiveBlockedHits;
	private bool _ripostePending;
	private readonly RandomNumberGenerator _rng = new();

	private readonly HashSet<AttackIdentity> _observedProjectiles = new();
	public void ConfigureRandom(ulong seed) => _rng.Seed = seed;
	private Node3D _cachedTarget;
	private AttackController _cachedTargetAttackCtrl;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Intent;
		_rng.Seed = 42;
	}

	public override void _ExitTree()
	{
		if (GuardComponent != null)
		{
			GuardComponent.ParrySuccessful -= OnParrySuccessful;
			GuardComponent.BlockSuccessful -= OnBlockSuccessful;
		}
	}

	public void SetGuardComponent(GuardComponent guard)
	{
		if (_guardComponent == guard) return;
		if (_guardComponent != null)
		{
			_guardComponent.ParrySuccessful -= OnParrySuccessful;
			_guardComponent.BlockSuccessful -= OnBlockSuccessful;
		}
		_guardComponent = guard;
		if (_guardComponent != null)
		{
			_guardComponent.ParrySuccessful += OnParrySuccessful;
			_guardComponent.BlockSuccessful += OnBlockSuccessful;
		}
	}

	private void OnParrySuccessful() => RegisterDefendedContact();

	private void OnBlockSuccessful(float chipDamage) => RegisterDefendedContact();

	/// <summary>Every defended contact sustains guard; parries never bypass the hold or release sequence.</summary>
	private void RegisterDefendedContact()
	{
		_pressureRemaining = PressureWindow;
		_consecutiveBlockedHits++;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Enemy == null || Enemy.IsDead || Mode == EnemyDefenseMode.None)
		{
			if (GuardComponent?.IsGuarding == true) GuardComponent.EndGuard();
			_meleeReactionTimer = 0;
			_pressureRemaining = 0;
			_totalGuardTime = 0;
			_consecutiveBlockedHits = 0;
			_ripostePending = false;
			return;
		}


		if (!HasGuardCapability && !HasDodgeCapability) return;

		float dt = (float)delta;
		UpdateGuardHold(dt);
		if (_ripostePending && GuardComponent?.IsGuarding != true && GuardComponent?.IsGuardRecovering != true)
		{
			_ripostePending = false;
			TryExecuteRiposte();
		}
		UpdateMeleeThreatObservation(dt);
		UpdateProjectileThreatObservation(dt);
	}

	private void UpdateGuardHold(float dt)
	{
		if (GuardComponent == null || !GuardComponent.IsGuarding) return;

		float maxDuration = Strategy?.MaxGuardDuration ?? 3.5f;

		_totalGuardTime += dt;
		if (_pressureRemaining > 0)
		{
			_pressureRemaining -= dt;
		}

		if (_pressureRemaining <= 0 || _totalGuardTime >= maxDuration)
		{
			bool wasDefending = _consecutiveBlockedHits > 0;
			_pressureRemaining = 0;
			_totalGuardTime = 0;
			_consecutiveBlockedHits = 0;

			GuardComponent.EndGuard();

			if (wasDefending)
			{
				float riposteChance = Mode == EnemyDefenseMode.GuardParry ? 1.0f : (Strategy?.RiposteChance ?? 0.65f);
				if (_rng.Randf() <= riposteChance)
				{
					_ripostePending = true;
				}
			}
		}
	}

	/// <summary>Attempt one counterattack after defense completion, revalidating current sight, reach and commitment.</summary>
	public bool TryExecuteRiposte()
	{
		if (Enemy == null || Enemy.IsDead) return RecordRiposte(false, "unavailable");
		if (Enemy.MovementComponent?.IsPushed == true || Enemy.ActionGate?.IsBusy == true)
			return RecordRiposte(false, "busy");

		Node3D target = ResolveTarget();
		if (target == null || !CanObserve(target)) return RecordRiposte(false, "unobservable", target);
		Vector3 toTarget = GetPosition(target) - GetPosition(Enemy);
		toTarget.Y = 0;
		float distance = toTarget.Length();
		float reach = Enemy.EnemyBehaviorComponent?.Profile?.AttackStrategy?.EffectiveRange ?? 0;
		if (distance > reach) return RecordRiposte(false, "out_of_reach", target, distance, reach);

		// Turn toward the target at the actor's normal turn rate; an attack that cannot start yet is rejected.
		if (toTarget.LengthSquared() > 0.001f) Enemy.MovementComponent?.FaceDirection(toTarget.Normalized());

		Enemy.EnemyBehaviorComponent?.SetTarget(target);
		bool accepted = Enemy.EnemyBehaviorComponent?.TryAttack() ?? false;
		RecordRiposte(accepted, accepted ? "accepted" : "attack_rejected", target, distance, reach);
		if (accepted) EmitSignalRiposteTriggered();
		return accepted;
	}

	private bool RecordRiposte(bool accepted, string reason, Node3D target = null, float distance = 0, float reach = 0)
	{
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.riposte", ("accepted", accepted), ("reason", reason),
			("target", CombatTrace.Identity(target)), ("distance", distance), ("reach", reach));
		return accepted;
	}

	private static Vector3 GetPosition(Node3D node) =>
		node.IsInsideTree() ? node.GlobalPosition : node.Position;

	private Node3D ResolveTarget()
	{
		var target = GodotObject.IsInstanceValid(Target) ? Target : Enemy?.EnemyBehaviorComponent?.Target;
		return GodotObject.IsInstanceValid(target) ? target : null;
	}

	private bool CanObserve(Node3D target)
	{
		if (!GodotObject.IsInstanceValid(target) || target.GetNodeOrNull<HealthComponent>("HealthComponent")?.IsDead == true) return false;
		var perception = Enemy?.EnemyBehaviorComponent?.GetNodeOrNull<PerceptionComponent>("Perception");
		return perception != null ? perception.CanPerceive(target) : ReferenceEquals(target, Target);
	}

	private void UpdateMeleeThreatObservation(float dt)
	{
		if (_meleeReactionTimer > 0)
		{
			_meleeReactionTimer -= dt;
			if (_meleeReactionTimer <= 0)
			{
				TryExecuteMeleeDefense();
			}
		}

		Node3D target = ResolveTarget();
		if (target == null || !CanObserve(target)) return;

		var attackCtrl = _cachedTargetAttackCtrl;
		if (!ReferenceEquals(_cachedTarget, target) || attackCtrl == null || !GodotObject.IsInstanceValid(attackCtrl))
		{
			attackCtrl = target.GetNodeOrNull<AttackController>("AttackController")
				?? target.GetNodeOrNull<AttackController>("PlayerAttackController");
			_cachedTargetAttackCtrl = attackCtrl;
			_cachedTarget = target;
		}

		if (attackCtrl == null || !attackCtrl.IsAttacking || attackCtrl.Phase != ActorActionPhase.WindUp) return;
		if (attackCtrl.CurrentAttack != null && attackCtrl.CurrentAttack.IsRanged) return;

		var execution = attackCtrl.CurrentExecutionId;
		if (execution == _lastObservedMeleeExecution) return;

		Vector3 enemyPos = GetPosition(Enemy);
		Vector3 targetPos = GetPosition(target);
		float dist = enemyPos.DistanceTo(targetPos);
		if (dist > MeleeThreatDistance) return;

		// If already guarding, incoming attack refreshes pressure
		if (GuardComponent != null && GuardComponent.IsGuarding)
		{
			_lastObservedMeleeExecution = execution;
			_pressureRemaining = PressureWindow;
			return;
		}

		float delay = Strategy?.ReactionDelay ?? ReactionDelay;
		EnemyDefenseMode chosen = ResolveDefenseMode();
		// Forced harness modes are left alone so guard breaks stay reproducible.
		if (Mode == EnemyDefenseMode.ProfileDefault)
		{
			var tier = attackCtrl.CurrentAttack?.ThreatTier ?? AttackThreatTier.Normal;
			var filtered = ForThreat(chosen, tier, HasDodgeCapability);
			if (filtered != chosen && CombatTrace.Enabled) CombatTrace.Write(this, "defense.threat_filtered", ("tier", tier.ToString()), ("decision", filtered.ToString()));
			chosen = filtered;
		}

		_lastObservedMeleeExecution = execution;

		if (chosen == EnemyDefenseMode.None)
		{
			return;
		}

		// Forced mode changes probability only. Delay and cooldown remain production behavior.
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.observed", ("source", CombatTrace.Identity(attackCtrl)),
			("execution", execution.Sequence), ("decision", chosen.ToString()), ("delay", delay));

		_chosenMeleeDefense = chosen;
		_pendingMeleeAttacker = target;
		_pendingMeleeController = attackCtrl;

		if (delay <= 0f)
		{
			_meleeReactionTimer = 0f;
			TryExecuteMeleeDefense();
		}
		else
		{
			_meleeReactionTimer = delay;
		}
	}

	private EnemyDefenseMode ResolveDefenseMode()
	{
		var roll = _rng.Randf();
		var chosen = Mode != EnemyDefenseMode.ProfileDefault ? Mode
			: Strategy?.Choose(HasGuardCapability, HasDodgeCapability, roll) ?? EnemyDefenseMode.None;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.decision", ("mode", Mode.ToString()),
			("decision", chosen.ToString()), ("roll", roll));
		return chosen;
	}

	/// <summary>
	/// Guarding a Heavy or Unblockable attack is a mistake: a guard choice becomes a dodge when the actor can
	/// dodge, else no response. The roll already happened, so filtering never changes RNG draw order.
	/// </summary>
	public static EnemyDefenseMode ForThreat(EnemyDefenseMode chosen, AttackThreatTier tier, bool canDodge) =>
		chosen != EnemyDefenseMode.GuardParry || tier == AttackThreatTier.Normal ? chosen
			: canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;

	private void TryExecuteMeleeDefense()
	{
		if (_pendingMeleeAttacker == null || !GodotObject.IsInstanceValid(_pendingMeleeAttacker)
			|| !ReferenceEquals(_pendingMeleeAttacker, ResolveTarget())) return;
		if (!GodotObject.IsInstanceValid(_pendingMeleeController) || !_pendingMeleeController.IsAttacking
			|| _pendingMeleeController.Phase != ActorActionPhase.WindUp || !CanObserve(_pendingMeleeAttacker)) return;
		if (_pendingMeleeController.CurrentExecutionId != _lastObservedMeleeExecution) return;

		Vector3 enemyPos = GetPosition(Enemy);
		Vector3 attackerPos = GetPosition(_pendingMeleeAttacker);
		float dist = enemyPos.DistanceTo(attackerPos);
		if (dist > MeleeThreatDistance + 0.5f) return;

		EnemyDefenseMode chosen = Mode != EnemyDefenseMode.ProfileDefault ? Mode : _chosenMeleeDefense;
		TryExecuteDefensiveAction(chosen, attackerPos);
	}

	private bool TryExecuteDefensiveAction(EnemyDefenseMode mode, Vector3 threatOrigin)
	{
		Vector3 enemyPos = GetPosition(Enemy);
		if (mode == EnemyDefenseMode.Dodge && HasDodgeCapability)
		{
			Vector3 away = enemyPos - threatOrigin;
			away.Y = 0;
			Vector3 dodgeDir = away.LengthSquared() > 0.001f ? away.Normalized() : -Enemy.GlobalTransform.Basis.Z;
			if (!DodgeComponent.IsDodging && DodgeComponent.CooldownRemaining <= 0 && IsSafeDodge(dodgeDir))
			{
				return DodgeComponent.TryDodge(dodgeDir) == DefenseRequestResult.Success;
			}
		}
		else if (mode == EnemyDefenseMode.GuardParry && HasGuardCapability)
		{
			// A guard is raised along the current facing; a threat outside its frontal sector cannot be
			// guarded, so flanking a guarding monster works instead of snapping it around.
			Vector3 toThreat = threatOrigin - enemyPos;
			toThreat.Y = 0;
			Vector3 facing = Enemy.MovementComponent?.LookAtDirection ?? -Enemy.GlobalTransform.Basis.Z;
			facing.Y = 0;
			if (facing.LengthSquared() < 0.001f) facing = -Enemy.GlobalTransform.Basis.Z;
			float sector = GuardComponent.GuardDef?.FacingSectorDegrees ?? 120f;
			if (toThreat.LengthSquared() > 0.001f && Mathf.RadToDeg(facing.AngleTo(toThreat)) > sector * .5f)
			{
				if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.out_of_sector", ("angle", Mathf.RadToDeg(facing.AngleTo(toThreat))), ("sector", sector));
				return false;
			}
			if (!GuardComponent.IsGuarding && !GuardComponent.IsGuardRecovering)
			{
				if (GuardComponent.TryBeginGuard(facing.Normalized()) == DefenseRequestResult.Success)
				{
					_pressureRemaining = PressureWindow;
					_totalGuardTime = 0f;
					_consecutiveBlockedHits = 0;
					return true;
				}
			}
		}
		return false;
	}

	private IEnumerable<Projectile> FindProjectiles()
	{
		if (IsInsideTree() && GetTree() != null)
		{
			var nodes = GetTree().GetNodesInGroup("projectile");
			foreach (Node node in nodes)
			{
				if (node is Projectile p) yield return p;
			}
		}
		else
		{
			var parent = Enemy?.GetParent() ?? GetParent();
			if (parent != null)
			{
				foreach (Node child in parent.GetChildren())
				{
					if (child is Projectile p) yield return p;
				}
			}
		}
	}

	private void UpdateProjectileThreatObservation(float dt)
	{
		if (Mode == EnemyDefenseMode.ProfileDefault && Strategy?.CanDefendProjectiles != true)
		{
			return;
		}

		var active = new HashSet<AttackIdentity>();
		foreach (var p in FindProjectiles()) active.Add(p.ExecutionId);
		_observedProjectiles.IntersectWith(active);
		Vector3 enemyPos = GetPosition(Enemy);
		foreach (Projectile projectile in FindProjectiles())
		{
			if (!GodotObject.IsInstanceValid(projectile)) continue;
			if (projectile.IsInsideTree() && !projectile.IsPhysicsProcessing()) continue;

			if (projectile.Attacker == Enemy) continue;
			if (projectile.AttackerFaction != DamageSourceFlags.None && (projectile.AttackerFaction & DamageSourceFlags.Enemy) != 0)
				continue;

			Vector3 projPos = GetPosition(projectile);
			Vector3 toEnemy = enemyPos - projPos;
			toEnemy.Y = 0;
			float dist = toEnemy.Length();
			if (dist > ProjectileDetectionRadius) continue;

			Vector3 projDir = projectile.Direction;
			projDir.Y = 0;
			if (projDir.LengthSquared() < 0.001f || toEnemy.LengthSquared() < 0.001f) continue;

			float alignment = toEnemy.Normalized().Dot(projDir.Normalized());
			if (alignment < 0.60f) continue;

			if (dist <= ProjectileThreatDistance && CanObserve(projectile) && _observedProjectiles.Add(projectile.ExecutionId))
			{
				EnemyDefenseMode chosen = ResolveDefenseMode();
				if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.observed", ("source", CombatTrace.Identity(projectile)), ("execution", projectile.ExecutionId.Sequence), ("decision", chosen.ToString()), ("distance", dist));

				if (chosen == EnemyDefenseMode.Dodge && HasDodgeCapability)
				{
					if (!DodgeComponent.IsDodging && DodgeComponent.CooldownRemaining <= 0)
					{
						Vector3 lateral = Vector3.Up.Cross(projDir.Normalized()).Normalized();
						if (_rng.Randf() > 0.5f) lateral = -lateral;
						if (IsSafeDodge(lateral)) DodgeComponent.TryDodge(lateral);
						break;
					}
				}
				else if (chosen == EnemyDefenseMode.GuardParry && HasGuardCapability)
				{
					if (!GuardComponent.IsGuarding && !GuardComponent.IsGuardRecovering)
					{
						if (GuardComponent.TryBeginGuard(-projDir.Normalized()) != DefenseRequestResult.Success) continue;
						_pressureRemaining = PressureWindow;
						_totalGuardTime = 0f;
						_consecutiveBlockedHits = 0;
						break;
					}
				}
			}
		}
	}

	/// <summary>AI declines a dash without a supported endpoint or collision-free path.</summary>
	private bool IsSafeDodge(Vector3 direction)
	{
		if (!Enemy.IsInsideTree()) return true;
		float distance = DodgeComponent?.DodgeDef?.Distance ?? 0;
		Vector3 motion = direction * distance;
		if (Enemy.TestMove(Enemy.GlobalTransform, motion)) return false;
		Vector3 end = Enemy.GlobalPosition + motion;
		var fit = Enemy.GetNodeOrNull<LargeActorFitComponent>("LargeActorFitComponent");
		if (fit != null && !fit.CanMoveTo(end)) return false;
		var query = PhysicsRayQueryParameters3D.Create(end + Vector3.Up, end - Vector3.Up * 2, 1);
		return Enemy.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
	}

	public bool RequestDodge(Vector3 direction)
	{
		return DodgeComponent != null && DodgeComponent.TryDodge(direction) == DefenseRequestResult.Success;
	}

	public bool RequestGuard(Vector3 facing)
	{
		return GuardComponent != null && GuardComponent.TryBeginGuard(facing) == DefenseRequestResult.Success;
	}

	public void ReleaseGuard()
	{
		GuardComponent?.EndGuard();
	}
}