using Godot;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Handles timing-synchronized hit detection and projectile spawning for actors.
/// </summary>
public partial class AttackController : Node, IParryInterruptible
{
	/// <summary>One completion notification per accepted execution, after releasing its action/pose ownership.</summary>
	public event System.Action<AttackIdentity, bool> AttackFinished;
	[Export] public CombatAnimationComponent AnimationComponent { get; set; }
	public bool IsAttacking => _isAttacking;
	public double PerformDuration => _preparedAttack?.Duration ?? 0;
	public StringName CurrentStyle => _preparedAttack?.Playback?.Style ?? "";
	public AttackDefinition CurrentAttack => _currentAttack;
	public bool IsRanged => _currentAttack?.IsRanged ?? false;
	private PreparedAttack _preparedAttack;
	private ActorActionGateComponent _actionGate;
	private ActorActionLease _actionLease;
	public float RecoveryRemaining => _isAttacking ? Mathf.Max(0, (float)_preparedAttack.Duration - _elapsedTime) : 0;
	public ActorActionPhase Phase => !_isAttacking ? ActorActionPhase.None
		: _elapsedTime < _currentAttack.HitWindowStart ? ActorActionPhase.WindUp
		: _elapsedTime <= _currentAttack.HitWindowEnd ? ActorActionPhase.Active : ActorActionPhase.Recovery;
	public void ConfigureRandom(ulong seed) => _random.Seed = seed;

	[Export] public MeleeHitDetector MeleeDetector { get; set; }
	[Export] public MeleeAttackProfile DefaultMeleeContact { get; set; }
	[Export] public bool EffectsEnabled { get; set; } = true;
	[Signal] public delegate void AttackFeedbackEventHandler(string result, float damage);
	public string LastResult { get; private set; } = "Ready";
	public float LastDamage { get; private set; }
	public int ContactCount { get; private set; }
	public bool HasCommittedFacing { get; private set; }
	public Vector3 CommittedFacing { get; private set; }
	private Basis _contactBasis;
	private bool _contactEvaluated;
	private bool _blockedContact;
	private MeleeSwoosh _swoosh;
	[Export] public PackedScene DefaultProjectileScene { get; set; }
	[Export] public bool DebugDrawEnabled { get; set; } = false;
	public static bool GlobalDebugDrawEnabled { get; set; } = false;

	/// <summary>
	/// Maximum distance a melee attack will auto-turn the actor toward a target ("aim assist"), so a
	/// swing connects as long as the target is roughly in front rather than requiring exact facing.
	/// </summary>
	[Export] public float MeleeAimAssistRange { get; set; } = 3.0f;

	/// <summary>
	/// Half-angle, in degrees, of the melee aim-assist cone measured from the actor's forward.
	/// </summary>
	[Export] public float MeleeAimAssistAngle { get; set; } = 90.0f;

	private bool _isAttacking;
	private float _elapsedTime;
	private AttackDefinition _currentAttack;
	private float _currentMinDamage;
	private float _currentMaxDamage;
	private float _currentCritChance;
	private uint _targetMask;

	private ulong _executionSequence = 0;
	public AttackIdentity CurrentExecutionId => new((ulong)GetInstanceId(), _executionSequence);

	private bool _projectileSpawned;
	private HashSet<Node> _hitTargets = new HashSet<Node>();

	private Node3D _actor;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		_actor = GetParent<Node3D>();
		ProcessPhysicsPriority = CombatPhysics.Damage;
		_actionGate = _actor.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");
		AddToGroup("attack_controller");
		_random.Seed = 42;
		SetPhysicsProcess(false);
	}

	/// <summary>
	/// Begins playing an attack with its stats and targeting parameters.
	/// </summary>
	public bool StartAttack(AttackDefinition def, float minDamage, float maxDamage, float critChance, uint targetMask)
	{
		return TryPrepareAttack(def, "", 0, out var attack)
			&& StartAttack(attack, minDamage, maxDamage, critChance, targetMask);
	}

	/// <summary>Validate and capture the complete attack before animations, timers or hits start.</summary>
	public bool TryPrepareAttack(AttackDefinition definition, StringName weaponStyle, double duration, out PreparedAttack attack, MeleeAttackProfile weaponContact = null)
	{
		attack = null;
		_actor ??= GetParent<Node3D>();
		if (definition == null || _actor == null) return false;
		StringName style = string.IsNullOrWhiteSpace(definition.AttackStyle) ? weaponStyle : definition.AttackStyle;
		AttackPlayback playback = null;
		if (AnimationComponent != null && !AnimationComponent.TryResolve(style, out playback))
		{
			GD.PushWarning($"{_actor.Name}: missing/invalid attack animation style '{style}' in {AnimationComponent.Profile?.ResourcePath}.");
			return false;
		}
		if (duration == 0) duration = playback?.ClipLength ?? System.Math.Max(definition.HitWindowEnd, definition.HitWindowStart + 0.1f);
		if (!double.IsFinite(duration) || duration <= 0) return false;
		var copy = (AttackDefinition)definition.Duplicate();
		if (!copy.IsRanged)
		{
			var contact = definition.MeleeContact ?? weaponContact ?? DefaultMeleeContact;
			if (contact == null || !contact.IsValid || MeleeDetector == null) return false;
			copy.MeleeContact = (MeleeAttackProfile)contact.Duplicate();
		}
		if (playback != null)
		{
			copy.HitWindowStart = (float)duration * playback.ContactStart;
			copy.HitWindowEnd = (float)duration * playback.ContactEnd;
		}
		attack = new PreparedAttack(copy, playback, duration);
		return true;
	}

	/// <summary>Commit an already validated execution snapshot to this actor.</summary>
	public bool StartAttack(PreparedAttack attack, float minDamage, float maxDamage, float critChance, uint targetMask, ActorActionLease lease = default)
	{
		if (attack == null || _actor == null) return false;
		var def = attack.Definition;

		_actionGate ??= _actor.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");
		if (_actor is Player { IsDead: true } || _actor is EnemyBase { IsDead: true }) return false;
		if (_isAttacking) return false;
		if (_actionGate != null)
		{
			if (lease.IsValid)
			{
				if (lease.Kind is not (ActorActionKind.PrimaryAttack or ActorActionKind.SecondaryTechnique)
					|| !_actionGate.Adopt(lease, _ => CancelAttack())) return false;
			}
			else if (_actionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp,
				_ => CancelAttack(), out lease) != ActorActionRequestResult.Success) return false;
		}
		_actionLease = lease;
		_executionSequence++;
		_preparedAttack = attack;
		_currentAttack = def;
		_currentMinDamage = minDamage;
		_currentMaxDamage = maxDamage;
		_currentCritChance = critChance;
		_targetMask = targetMask;

		_elapsedTime = 0.0f;
		_isAttacking = true;
		HasCommittedFacing = false;
		_contactEvaluated = _blockedContact = false;
		ContactCount = 0;
		LastDamage = 0;
		LastResult = "Preparing";
		if (!def.IsRanged) MeleeDetector.Begin(_actor, def.MeleeContact, targetMask, DebugDrawEnabled || GlobalDebugDrawEnabled);
		_projectileSpawned = false;
		_hitTargets.Clear();

		GameDebug.Combat($"{_actor.Name} starting attack: {def.AnimationId} (Ranged: {def.IsRanged})");

		_actionGate?.ReportPhase(_actionLease, Phase);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.started", ("execution", _executionSequence), ("execution_source", GetInstanceId()), ("style", CurrentStyle.ToString()),
			("contact_start", def.HitWindowStart), ("contact_end", def.HitWindowEnd), ("duration", attack.Duration),
			("traits", def.DefenseTraits.ToString()), ("min_damage", minDamage), ("max_damage", maxDamage));
		if (attack.Playback != null) AnimationComponent.Begin(attack.Playback);
		SetPhysicsProcess(true);
		return true;
	}

	/// <summary>
	/// Cancels any active attack and cleans up active hitboxes.
	/// </summary>
	public void CancelAttack() => FinishAttack(true);

	public bool TryInterruptForParry(AttackIdentity execution, float staggerDuration)
	{
		if (!_isAttacking || _preparedAttack == null) return false;
		if (CurrentExecutionId != execution) return false;

		_actor ??= GetParent<Node3D>();
		CancelAttack();

		if (_actor is IStaggerable staggerable)
		{
			staggerable.TryStagger(staggerDuration);
		}
		else if (_actor?.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent") is ActorActionGateComponent gate)
		{
			gate.TryStagger(staggerDuration);
		}
		return true;
	}

	private void FinishAttack(bool interrupted)
	{
		if (!_isAttacking) return;
		var identity = CurrentExecutionId;
		CloseContact();
		MeleeDetector?.End();
		if (interrupted) ReportFeedback("Interrupted");
		else if (!_currentAttack.IsRanged && ContactCount == 0) ReportFeedback(_blockedContact ? "Obstructed" : "No contact");
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.ended", ("execution", _executionSequence), ("interrupted", interrupted), ("contacts", ContactCount), ("result", LastResult));
		_isAttacking = false;
		_actionGate?.Release(_actionLease);
		_actionLease = ActorActionLease.None;
		_currentAttack = null;
		_preparedAttack = null;
		AnimationComponent?.End();
		SetPhysicsProcess(false);
		AttackFinished?.Invoke(identity, interrupted);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_isAttacking || _currentAttack == null) { SetPhysicsProcess(false); return; }
		if ((_actor is Player player && (player.IsDead || player.IsHit))
			|| (_actor is EnemyBase enemy && (enemy.IsDead || enemy.MovementComponent.IsPushed)))
		{
			CancelAttack();
			return;
		}
		float previous = _elapsedTime;
		_elapsedTime += (float)delta;
		_actionGate?.ReportPhase(_actionLease, Phase);
		if (_preparedAttack.Playback != null)
			AnimationComponent.Sample(_preparedAttack.Playback, _elapsedTime / _preparedAttack.Duration);

		if (_currentAttack.IsRanged)
		{
			if (_elapsedTime >= _currentAttack.HitWindowStart && !_projectileSpawned)
			{
				SpawnProjectile();
				_projectileSpawned = true;
			}
		}
		else if (_elapsedTime >= _currentAttack.HitWindowStart && previous <= _currentAttack.HitWindowEnd
			&& (!_contactEvaluated || previous < _currentAttack.HitWindowEnd))
		{
			// Interval intersection, not just the current timestamp: even a whole skipped window queries once.
			if (!HasCommittedFacing)
			{
				CommittedFacing = GetActorForward();
				_contactBasis = Basis.LookingAt(CommittedFacing, Vector3.Up);
				HasCommittedFacing = true;
			}
			var profile = _currentAttack.MeleeContact;
			var frame = new Transform3D(_contactBasis, _actor.GlobalPosition + Vector3.Up * profile.CenterHeight);
			var execution = _preparedAttack;
			foreach (var target in MeleeDetector.Sample(frame).OrderBy(node =>
				(node is HurtBoxComponent ? node.GetParent() : node).GetInstanceId()))
			{
				ApplyMeleeContact(target);
				if (!ReferenceEquals(execution, _preparedAttack)) return;
			}
			_blockedContact |= MeleeDetector.BlockedContacts > 0;
			_contactEvaluated = true;
			if (EffectsEnabled && profile.SwooshScene != null)
			{
				if (_swoosh == null)
				{
					_swoosh = ScenePool.Spawn<MeleeSwoosh>(profile.SwooshScene, GetTree().CurrentScene ?? _actor.GetParent());
					_swoosh.Initialize(frame, profile.Reach, FindWeaponInstance());
				}
				SampleSwoosh(frame, previous);
			}
		}
		if (!_currentAttack.IsRanged && _elapsedTime >= _currentAttack.HitWindowEnd) CloseContact();
		if (_elapsedTime >= _preparedAttack.Duration) FinishAttack(false);
	}

	/// <summary>Trace cosmetics through intermediate poses, keeping collision independent of animation geometry.</summary>
	private void SampleSwoosh(Transform3D frame, float previous)
	{
		float start = _currentAttack.HitWindowStart, end = _currentAttack.HitWindowEnd;
		float window = Mathf.Max(.0001f, end - start);
		if (_swoosh.FollowsWeapon && _preparedAttack.Playback != null)
		{
			float from = Mathf.Max(previous, start), to = Mathf.Min(_elapsedTime, end);
			int steps = Mathf.Clamp(Mathf.CeilToInt((to - from) / (float)_preparedAttack.Duration * 60), 1, 32);
			for (int i = 0; i <= steps; i++)
			{
				float time = Mathf.Lerp(from, to, (float)i / steps);
				AnimationComponent.Sample(_preparedAttack.Playback, time / _preparedAttack.Duration);
				_swoosh.Sample(frame, (time - start) / window);
			}
			AnimationComponent.Sample(_preparedAttack.Playback, _elapsedTime / _preparedAttack.Duration);
			_swoosh.RefreshWeaponPose();
		}
		else _swoosh.Sample(frame, (_elapsedTime - start) / window);
	}

	private void CloseContact()
	{
		MeleeDetector?.CloseWindow();
		if (GodotObject.IsInstanceValid(_swoosh)) _swoosh.Finish();
		_swoosh = null;
	}

	private void ApplyMeleeContact(Node3D target)
	{
		Node owner = target is HurtBoxComponent ? target.GetParent() : target;
		if (!_hitTargets.Add(owner)) return;
		if (target is HurtBoxComponent { HealthComponent: { IsDead: true } }) return;
		ContactCount++;
		float damage = _random.RandfRange(_currentMinDamage, _currentMaxDamage);
		float baseDamage = damage;
		bool critical = _random.Randf() < _currentCritChance;
		if (critical) damage *= 2;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.contact", ("execution", _executionSequence), ("execution_source", GetInstanceId()),
			("target", CombatTrace.Identity(target)), ("base_damage", baseDamage), ("critical", critical), ("raw_damage", damage));

		Vector3 incomingDir = CommittedFacing;
		if (target != null && _actor != null)
		{
			Vector3 offset = target.GlobalPosition - _actor.GlobalPosition;
			offset.Y = 0;
			if (offset.LengthSquared() > 0.0001f)
			{
				incomingDir = offset.Normalized();
			}
		}

		var traits = _currentAttack?.DefenseTraits ?? (AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable);
		var context = new DamageContext
		{
			ExecutionId = CurrentExecutionId,
			SourceNode = this,
			OwnerNode = _actor,
			Faction = HurtBoxComponent.GetDamageSource(_actor),
			Amount = damage,
			Random = _random,
			IncomingDirection = incomingDir,
			DefenseTraits = traits,
			DeliveryKind = DamageDeliveryKind.DirectMelee
		};

		if (target is ICombatDamageable combatDamageable)
		{
			var result = combatDamageable.ResolveDamage(in context);
			ReportFeedback(result.Outcome.ToString(), result.Amount);
		}
		else if (target is IDamageable damageable)
		{
			damageable.TakeDamage(damage, incomingDir, _actor);
			ReportFeedback("Contact", damage);
		}
	}

	private void ReportFeedback(string result, float damage = 0)
	{
		LastResult = result; LastDamage = damage;
		EmitSignalAttackFeedback(result, damage);
	}

	public override void _ExitTree() => CancelAttack();

	private void SpawnProjectile()
	{
		if (_currentAttack == null) return;

		PackedScene scene = _currentAttack.ProjectileScene ?? DefaultProjectileScene;
		if (scene == null)
		{
			GD.PushError($"{Name} cannot spawn projectile without a projectile scene.");
			return;
		}

		Vector3 origin = GetProjectileOrigin();
		if (_currentAttack.ProjectilePattern == ProjectilePattern.AreaDrop)
		{
			SpawnAreaDrop(origin, scene);
			return;
		}

		Vector3 aimedDirection = Aim(origin, _currentAttack.Range, _currentAttack.AimingAngle);
		SpawnEffect(_currentAttack.CastEffectScene, _actor.GlobalPosition, GetActorForward());
		SpawnEffect(_currentAttack.MuzzleEffectScene, origin, aimedDirection);

		foreach (Vector3 direction in GetProjectileDirections(aimedDirection))
		{
			Projectile projectile = ScenePool.Spawn<Projectile>(scene, (GetTree().CurrentScene ?? _actor.GetParent()));
			ApplyProjectileOverrides(projectile);
			projectile.Initialize(
				origin,
				direction,
				_currentAttack.ProjectileSpeed,
				_currentAttack.Range,
				_currentMinDamage * _currentAttack.ProjectileDamageScale,
				_currentMaxDamage * _currentAttack.ProjectileDamageScale,
				_currentCritChance,
				_actor, _currentAttack.DefenseTraits, _currentAttack.AreaDefenseTraits, _random.Randi(), CurrentExecutionId);

			GameDebug.Combat($"{_actor.Name} spawned projectile flying {direction}");
		}
	}

	private void SpawnAreaDrop(Vector3 origin, PackedScene scene)
	{
		Vector3 actorForward = GetActorForward();
		SpawnEffect(_currentAttack.CastEffectScene, _actor.GlobalPosition, actorForward);
		SpawnEffect(_currentAttack.MuzzleEffectScene, origin, actorForward);

		foreach (Vector3 targetPosition in GetAreaDropLandingPositions())
		{
			Vector3 dropOrigin = targetPosition + Vector3.Up * _currentAttack.SpawnHeight;
			Vector3 direction = Vector3.Down;
			Projectile projectile = ScenePool.Spawn<Projectile>(scene, (GetTree().CurrentScene ?? _actor.GetParent()));
			ApplyProjectileOverrides(projectile);
			projectile.Initialize(
				dropOrigin,
				direction,
				_currentAttack.ProjectileSpeed,
				_currentAttack.SpawnHeight + 1.0f,
				_currentMinDamage * _currentAttack.ProjectileDamageScale,
				_currentMaxDamage * _currentAttack.ProjectileDamageScale,
				_currentCritChance,
				_actor, _currentAttack.DefenseTraits, _currentAttack.AreaDefenseTraits, _random.Randi(), CurrentExecutionId);

			GameDebug.Combat($"{_actor.Name} spawned area drop at {targetPosition}");
		}
	}

	private void ApplyProjectileOverrides(Projectile projectile)
	{
		if (_currentAttack.ImpactRadiusOverride >= 0.0f)
		{
			projectile.ImpactRadius = _currentAttack.ImpactRadiusOverride;
		}

		if (_currentAttack.ImpactRadiusMinDamageScaleOverride >= 0.0f)
		{
			projectile.ImpactRadiusMinDamageScale = _currentAttack.ImpactRadiusMinDamageScaleOverride;
		}
	}

	private IEnumerable<Vector3> GetAreaDropLandingPositions()
	{
		int count = Mathf.Max(1, _currentAttack.ProjectileCount);
		Level level = _actor.GetAncestorOrNull<Level>() ?? (GetTree().CurrentScene ?? _actor.GetParent())?.GetNodeOrNull<Level>("Level");
		for (int i = 0; i < count; i++)
		{
			if (TryGetAreaDropLandingPosition(i, count, _random, level?.MapGenerator, out Vector3 landingPosition))
			{
				yield return landingPosition;
			}
		}
	}

	private bool TryGetAreaDropLandingPosition(int index, int count, RandomNumberGenerator random, MapGenerator mapGenerator, out Vector3 landingPosition)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			float angle = index / (float)count * Mathf.Tau + random.RandfRange(-0.45f, 0.45f) + attempt * 0.29f;
			float radius = random.RandfRange(_currentAttack.TargetAreaRadius * 0.25f, _currentAttack.TargetAreaRadius);
			Vector3 rawTarget = _actor.GlobalPosition + new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
			if (mapGenerator == null)
			{
				landingPosition = rawTarget;
				return true;
			}

			if (mapGenerator.TryFindEffectLandingPositionNear(rawTarget, 1.5f, out Vector3 candidate))
			{
				landingPosition = candidate;
				return true;
			}
		}

		landingPosition = default;
		return false;
	}

	private Vector3 GetProjectileOrigin()
	{
		Node3D muzzle = FindMuzzlePoint();
		if (muzzle != null)
		{
			return muzzle.GlobalPosition;
		}

		return _actor.GlobalPosition - _actor.GlobalTransform.Basis.Z * 1.0f + Vector3.Up * 1.2f;
	}

	private IEnumerable<Vector3> GetProjectileDirections(Vector3 aimedDirection)
	{
		int count = Mathf.Max(1, _currentAttack.ProjectileCount);

		if (_currentAttack.ProjectilePattern == ProjectilePattern.Radial)
		{
			float step = 360.0f / count;
			for (int i = 0; i < count; i++)
			{
				yield return RotateHorizontal(GetActorForward(), step * i);
			}
			yield break;
		}

		if (_currentAttack.ProjectilePattern == ProjectilePattern.Spread && count > 1)
		{
			float startAngle = -_currentAttack.SpreadAngle * 0.5f;
			float step = _currentAttack.SpreadAngle / (count - 1);
			for (int i = 0; i < count; i++)
			{
				yield return RotateHorizontal(aimedDirection, startAngle + step * i);
			}
			yield break;
		}

		yield return aimedDirection;
	}

	private Vector3 RotateHorizontal(Vector3 direction, float degrees)
	{
		return direction.Rotated(Vector3.Up, Mathf.DegToRad(degrees)).Normalized();
	}

	private void SpawnEffect(PackedScene effectScene, Vector3 origin, Vector3 direction)
	{
		if (effectScene == null) return;

		Node3D effect = ScenePool.Spawn<Node3D>(effectScene, (GetTree().CurrentScene ?? _actor.GetParent()));
		effect.GlobalPosition = origin;
		if (direction.LengthSquared() > 0)
		{
			effect.LookAt(origin + direction, Vector3.Up);
		}
	}

	private Node3D FindWeaponInstance()
	{
		var attachmentManager = _actor.GetNodeOrNull<BoneAttachmentManager>("BoneAttachmentManager");
		if (attachmentManager?.AttachmentNodes == null)
		{
			return null;
		}

		AttachmentType[] preferredTypes =
		{
			AttachmentType.OneHandedWeapon,
			AttachmentType.TwoHandedWeapon,
			AttachmentType.OffhandWeapon,
		};

		foreach (var attachmentType in preferredTypes)
		{
			if (!attachmentManager.AttachmentNodes.TryGetValue(attachmentType, out var attachment)
				|| attachment == null
				|| !attachment.Visible
				|| attachment.GetChildCount() == 0)
			{
				continue;
			}

			var child = attachment.GetChild<Node3D>(0);
			if (child != null)
			{
				return child;
			}
		}

		return null;
	}

	private Node3D FindMuzzlePoint()
	{
		Node3D weapon = FindWeaponInstance();
		if (weapon != null)
		{
			var muzzle = weapon.FindChild("Muzzle") as Node3D ?? weapon.FindChild("MuzzlePoint") as Node3D;
			if (muzzle != null) return muzzle;
		}
		return null;
	}

	private Vector3 Aim(Vector3 origin, float range, float aimingAngle)
	{
		bool isPlayer = _actor.IsInGroup("player");
		string targetGroup = isPlayer ? "enemy" : "player";

		var targets = GetTree().GetNodesInGroup(targetGroup).OfType<Node3D>()
			.Where(target => IsValidAimTarget(target) && TestLineOfSight(target, range, aimingAngle))
			.OrderBy(GetAimAngle)
			.ThenBy(target => _actor.GlobalPosition.DistanceTo(target.GlobalPosition));

		foreach (var target in targets)
		{
			return GetDirectionToTarget(origin, target, 1.0f);
		}

		if (isPlayer)
		{
			var damageables = GetTree().GetNodesInGroup("damageable").OfType<Node3D>()
				.Where(node => node is IDamageable
					&& !IsOwnedByActor(node)
					&& TestLineOfSight(node, range, aimingAngle))
				.OrderBy(GetAimAngle)
				.ThenBy(node => _actor.GlobalPosition.DistanceTo(node.GlobalPosition));

			foreach (var node in damageables)
			{
				return GetDirectionToTarget(origin, node, 0.5f);
			}
		}

		return GetActorForward();
	}

	private Vector3 GetDirectionToTarget(Vector3 origin, Node3D target, float fallbackHeight)
	{
		Vector3 direction = GetTargetCenter(target, fallbackHeight) - origin;
		direction.Y = 0;
		return direction.LengthSquared() > 0 ? direction.Normalized() : GetActorForward();
	}

	private Vector3 GetTargetCenter(Node3D target, float fallbackHeight)
	{
		var collisionShape = target.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		return collisionShape?.GlobalPosition ?? target.GlobalPosition + Vector3.Up * fallbackHeight;
	}

	private bool IsOwnedByActor(Node node)
	{
		return node == _actor || _actor.IsAncestorOf(node) || node.IsAncestorOf(_actor);
	}

	private bool IsValidAimTarget(Node node)
	{
		if (IsOwnedByActor(node)) return false;
		if (node is EnemyBase enemy) return !enemy.IsDead;
		if (node is Player player) return !player.IsDead;
		if (node.GetParent() is EnemyBase parentEnemy) return !parentEnemy.IsDead;
		if (node.GetParent() is Player parentPlayer) return !parentPlayer.IsDead;
		return true;
	}

	/// <summary>
	/// Returns the best target to auto-face for a melee swing: the opponent nearest to the actor's
	/// forward direction within the aim-assist cone and range, or null when nothing qualifies. Used
	/// to turn the attacker toward its target so hits do not require pixel-perfect facing.
	/// </summary>
	public Node3D FindMeleeAimAssistTarget()
	{
		if (_actor == null)
		{
			return null;
		}

		string targetGroup = _actor.IsInGroup("player") ? "enemy" : "player";
		return GetTree().GetNodesInGroup(targetGroup).OfType<Node3D>()
			.Where(target => IsValidAimTarget(target) && WithinMeleeAimAssist(target))
			.OrderBy(GetAimAngle)
			.ThenBy(target => new Vector2(_actor.GlobalPosition.X, _actor.GlobalPosition.Z)
				.DistanceSquaredTo(new Vector2(target.GlobalPosition.X, target.GlobalPosition.Z)))
			.FirstOrDefault();
	}

	private bool WithinMeleeAimAssist(Node3D target)
	{
		Vector3 offset = target.GlobalPosition - _actor.GlobalPosition;
		offset.Y = 0;
		if (offset.Length() > MeleeAimAssistRange)
		{
			return false;
		}

		return GetAimAngle(target) <= MeleeAimAssistAngle;
	}

	private float GetAimAngle(Node3D target)
	{
		Vector3 directionToTarget = target.GlobalPosition - _actor.GlobalPosition;
		directionToTarget.Y = 0;
		if (directionToTarget.LengthSquared() == 0) return 0.0f;

		return Mathf.RadToDeg(GetActorForward().AngleTo(directionToTarget.Normalized()));
	}

	private Vector3 GetActorForward()
	{
		Vector3 forward = -_actor.GlobalTransform.Basis.Z;
		forward.Y = 0;
		return forward.LengthSquared() > 0 ? forward.Normalized() : Vector3.Forward;
	}

	private bool TestLineOfSight(Node3D target, float range, float aimingAngle)
	{
		if (!IsValidAimTarget(target)) return false;

		float distance = _actor.GlobalPosition.DistanceTo(target.GlobalPosition);
		if (distance > range) return false;

		Vector3 directionToTarget = target.GlobalPosition - _actor.GlobalPosition;
		directionToTarget.Y = 0;
		if (directionToTarget.LengthSquared() == 0) return true;
		directionToTarget = directionToTarget.Normalized();

		float angle = Mathf.RadToDeg(GetActorForward().AngleTo(directionToTarget));
		if (angle > aimingAngle) return false;

		return true;
	}
}