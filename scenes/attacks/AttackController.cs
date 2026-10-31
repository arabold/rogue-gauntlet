using Godot;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Handles timing-synchronized hit detection and projectile spawning for actors.
/// </summary>
[GlobalClass]
public partial class AttackController : Node, IParryInterruptible
{
	/// <summary>One completion notification per accepted execution, after releasing its action/pose ownership.</summary>
	public event System.Action<AttackIdentity, bool> AttackFinished;
	[Export] public CombatAnimationComponent AnimationComponent { get; set; }
	[Export] public ProjectileAimIndicator AimIndicator { get; set; }
	/// <summary>Optional footprint cue for Heavy and Unblockable melee; presentation only.</summary>
	[Export] public AttackTelegraph Telegraph { get; set; }
	public bool IsAttacking => _isAttacking;
	public double PerformDuration => _preparedAttack?.Duration ?? 0;
	public StringName CurrentStyle => _preparedAttack?.Playback?.Style ?? "";
	public AttackDefinition CurrentAttack => _currentAttack;
	public bool IsRanged => _currentAttack?.IsRanged ?? false;
	private PreparedAttack _preparedAttack;
	/// <summary>The actor's action gate. Unassigned runs attacks without action ownership, phases or hyper-armor.</summary>
	[Export] public ActorActionGateComponent ActionGate { get; set; }
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
	/// <summary>Presentation hook (camera impulse, sound) when a contact pauses this execution.</summary>
	[Signal] public delegate void HitStopStartedEventHandler(float duration);
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
	private float _hitStopRemaining;
	private bool _hitStopUsed;
	private AttackDefinition _currentAttack;
	private AttackStats _currentHit;
	private uint _targetMask;

	private ulong _executionSequence = 0;
	public AttackIdentity CurrentExecutionId => new((ulong)GetInstanceId(), _executionSequence);

	// The first release captures the target and fan; staggered projectiles then leave one by one.
	private bool _releaseStarted;
	private int _projectilesReleased;
	private readonly List<Vector3> _releaseDirections = [];
	private Node3D _releaseTarget;
	private Node3D _projectileAimTarget;
	private Vector3 _projectileAimOrigin;
	private Vector3 _projectileAimDirection;
	private float _projectileAimLength;
	public bool IsProjectileAimLocked => IsRanged && HasCommittedFacing;
	private HashSet<Node> _hitTargets = new HashSet<Node>();

	private Node3D _actor;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		_actor = GetParent<Node3D>();
		ProcessPhysicsPriority = CombatPhysics.Damage;
		AddToGroup("attack_controller");
		_random.Seed = 42;
		SetPhysicsProcess(false);
	}

	/// <summary>
	/// Begins playing an attack with its stats and targeting parameters.
	/// </summary>
	public bool StartAttack(AttackDefinition def, AttackStats hit, uint targetMask)
	{
		return TryPrepareAttack(def, "", 0, out var attack)
			&& StartAttack(attack, hit, targetMask);
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
		if (!copy.HasValidImpact(copy.HitWindowStart) || !copy.HasValidFlight(copy.HitWindowStart, duration)) return false;
		if (!float.IsFinite(copy.ProjectileAimLockDuration) || copy.ProjectileAimLockDuration < 0) return false;
		if (copy.ProjectileAimLockDuration > 0 && (!copy.IsRanged || copy.ProjectileAimLockDuration > copy.HitWindowStart)) return false;
		attack = new PreparedAttack(copy, playback, duration);
		return true;
	}

	/// <summary>Commit an already validated execution snapshot to this actor.</summary>
	public bool StartAttack(PreparedAttack attack, AttackStats hit, uint targetMask, ActorActionLease lease = default)
	{
		if (attack == null || _actor == null) return false;
		var def = attack.Definition;
		Node3D aimTarget = def.ProjectileAimLockDuration > 0 ? FindProjectileAimTarget(def.Range, def.AimingAngle) : null;
		if (def.ProjectileAimLockDuration > 0 && aimTarget == null) return false;

		if (_actor is Player { IsDead: true } || _actor is EnemyBase { IsDead: true }) return false;
		if (_isAttacking) return false;
		if (ActionGate != null)
		{
			if (lease.IsValid)
			{
				if (lease.Kind is not (ActorActionKind.PrimaryAttack or ActorActionKind.SecondaryTechnique)
					|| !ActionGate.Adopt(lease, _ => CancelAttack())) return false;
			}
			else if (ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp,
				_ => CancelAttack(), out lease) != ActorActionRequestResult.Success) return false;
		}
		_actionLease = lease;
		_executionSequence++;
		_preparedAttack = attack;
		_currentAttack = def;
		_currentHit = hit;
		_targetMask = targetMask;

		_elapsedTime = 0.0f;
		_hitStopRemaining = 0;
		_hitStopUsed = false;
		_isAttacking = true;
		HasCommittedFacing = false;
		_contactEvaluated = _blockedContact = false;
		ContactCount = 0;
		LastDamage = 0;
		LastResult = "Preparing";
		if (!def.IsRanged) MeleeDetector.Begin(_actor, def.MeleeContact, targetMask, DebugDrawEnabled || GlobalDebugDrawEnabled);
		ResetRelease();
		_projectileAimTarget = null;
		AimIndicator?.Clear();
		if (def.ProjectileAimLockDuration > 0)
		{
			_projectileAimTarget = aimTarget;
			UpdateProjectileAim();
		}
		_hitTargets.Clear();

		GameDebug.Combat($"{_actor.Name} starting attack: {CurrentStyle} (Ranged: {def.IsRanged})");

		ReportProgress();
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.started", ("execution", _executionSequence), ("execution_source", GetInstanceId()), ("style", CurrentStyle.ToString()),
			("contact_start", def.HitWindowStart), ("contact_end", def.HitWindowEnd), ("duration", attack.Duration),
			("traits", def.DefenseTraits.ToString()), ("min_damage", hit.MinDamage), ("max_damage", hit.MaxDamage),
			("poise_damage", def.PoiseDamage), ("facing_lock_lead", def.FacingLockLead), ("hyper_armor", $"{def.HyperArmorStart}-{def.HyperArmorEnd}"));
		if (attack.Playback != null) AnimationComponent.Begin(attack.Playback);
		UpdateTelegraph();
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
		else
		{
			ActionGate?.TryStagger(staggerDuration);
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
		_hitStopRemaining = 0;
		AimIndicator?.Clear();
		Telegraph?.Clear();
		_projectileAimTarget = null;
		ResetRelease();
		ActionGate?.Release(_actionLease);
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
		// Hit-stop freezes this execution's clock (timing, contact sampling and pose) without slowing anyone else.
		if (_hitStopRemaining > 0)
		{
			_hitStopRemaining -= (float)delta;
			return;
		}
		float previous = _elapsedTime;
		_elapsedTime += (float)delta;
		ReportProgress();
		if (_preparedAttack.Playback != null)
			AnimationComponent.Sample(_preparedAttack.Playback, _elapsedTime / _preparedAttack.Duration);

		// A positive lead commits melee facing before contact, so a defender can step out of the swing.
		if (!_currentAttack.IsRanged && !HasCommittedFacing && _elapsedTime >= _currentAttack.HitWindowStart - _currentAttack.FacingLockLead)
			CommitMeleeFacing();
		UpdateTelegraph();

		if (_preparedAttack.Spell != null)
		{
			if (!_releaseStarted && _elapsedTime >= _currentAttack.HitWindowStart)
			{
				_releaseStarted = true;
				ReleaseSpell(_preparedAttack.Spell);
			}
		}
		else if (_currentAttack.IsRanged)
		{
			if (_currentAttack.ProjectileAimLockDuration > 0 && !_releaseStarted)
			{
				if (!GodotObject.IsInstanceValid(_projectileAimTarget) || !IsValidAimTarget(_projectileAimTarget))
				{
					CancelAttack();
					return;
				}
				UpdateProjectileAim();
			}
			if (_elapsedTime >= _currentAttack.HitWindowStart)
			{
				if (!_releaseStarted)
				{
					_releaseStarted = true;
					BeginRelease();
					AimIndicator?.Clear();
				}
				ReleaseDueProjectiles();
			}
		}
		else if (_elapsedTime >= _currentAttack.HitWindowStart && previous <= _currentAttack.HitWindowEnd
			&& (!_contactEvaluated || previous < _currentAttack.HitWindowEnd))
		{
			// Interval intersection, not just the current timestamp: even a whole skipped window queries once.
			if (!HasCommittedFacing) CommitMeleeFacing();
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

	/// <summary>Draw a Heavy/Unblockable melee footprint until its contact window closes; it follows tracking until facing commits.</summary>
	private void UpdateTelegraph()
	{
		if (Telegraph == null || _currentAttack.IsRanged || _currentAttack.ThreatTier == AttackThreatTier.Normal) return;
		if (_elapsedTime > _currentAttack.HitWindowEnd) { Telegraph.Clear(); return; }
		Telegraph.ShowThreat(_currentAttack.ThreatTier, _currentAttack.MeleeContact, _actor.GlobalPosition,
			HasCommittedFacing ? CommittedFacing : GetActorForward(), HasCommittedFacing);
	}

	private void CommitMeleeFacing()
	{
		CommittedFacing = GetActorForward();
		_contactBasis = Basis.LookingAt(CommittedFacing, Vector3.Up);
		HasCommittedFacing = true;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.facing_committed", ("execution", _executionSequence),
			("elapsed", _elapsedTime), ("facing", CombatTrace.Vector(CommittedFacing)));
	}

	/// <summary>Publish the phase and hyper-armor state of the current elapsed time to the action gate.</summary>
	private void ReportProgress()
	{
		ActionGate?.ReportPhase(_actionLease, Phase);
		ActionGate?.ReportHyperArmor(_actionLease, _currentAttack.IsHyperArmoredAt(_elapsedTime / (float)_preparedAttack.Duration));
	}

	private void ApplyMeleeContact(Node3D target)
	{
		Node owner = target is HurtBoxComponent ? target.GetParent() : target;
		if (!_hitTargets.Add(owner)) return;
		if (target is HurtBoxComponent { HealthComponent: { IsDead: true } }) return;
		ContactCount++;
		var (damage, critical) = _currentHit.Roll(_random);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.contact", ("execution", _executionSequence), ("execution_source", GetInstanceId()),
			("target", CombatTrace.Identity(target)), ("critical", critical), ("raw_damage", damage));

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
			DeliveryKind = DamageDeliveryKind.DirectMelee,
			Impact = _currentAttack.Impact,
			Critical = critical
		};

		if (target is ICombatDamageable combatDamageable)
		{
			var result = combatDamageable.ResolveDamage(in context);
			ReportFeedback(result.Outcome.ToString(), result.Amount);
			if (result.Outcome is DamageOutcome.Damaged or DamageOutcome.Blocked or DamageOutcome.GuardBroken)
				StartHitStop(result.HitStopScale);
		}
		else if (target is IDamageable damageable)
		{
			damageable.TakeDamage(damage, incomingDir, _actor);
			ReportFeedback("Contact", damage);
		}
	}

	/// <summary>Pause this execution once per swing; later contacts in the same swing do not extend it.</summary>
	private void StartHitStop(float scale)
	{
		if (_hitStopUsed || !_isAttacking || _currentAttack.HitStopDuration <= 0) return;
		_hitStopUsed = true;
		_hitStopRemaining = _currentAttack.HitStopDuration * scale;
		EmitSignalHitStopStarted(_hitStopRemaining);
	}

	private void ReportFeedback(string result, float damage = 0)
	{
		LastResult = result; LastDamage = damage;
		EmitSignalAttackFeedback(result, damage);
	}

	public override void _ExitTree() => CancelAttack();

	private PackedScene CurrentProjectileScene => _currentAttack.ProjectileScene ?? DefaultProjectileScene;

	private void ResetRelease()
	{
		_releaseStarted = false;
		_projectilesReleased = 0;
		_releaseDirections.Clear();
		_releaseTarget = null;
	}

	/// <summary>
	/// Capture the release once per execution: the aim target (also the homing target), the fan of directions and
	/// the cast/muzzle effects.
	/// </summary>
	private void BeginRelease()
	{
		PackedScene scene = CurrentProjectileScene;
		if (scene == null)
		{
			GD.PushError($"{Name} cannot spawn projectile without a projectile scene.");
			return;
		}

		Vector3 origin = IsProjectileAimLocked ? _projectileAimOrigin : GetProjectileOrigin();
		Vector3 aimedDirection;
		if (IsProjectileAimLocked)
		{
			aimedDirection = _projectileAimDirection;
			_releaseTarget = _projectileAimTarget;
		}
		else
		{
			_releaseTarget = FindReleaseTarget(_currentAttack.Range, _currentAttack.AimingAngle, out float fallbackHeight);
			aimedDirection = _releaseTarget != null ? GetDirectionToTarget(origin, _releaseTarget, fallbackHeight) : GetActorForward();
		}
		SpawnEffect(_currentAttack.CastEffectScene, _actor.GlobalPosition, GetActorForward());
		SpawnEffect(_currentAttack.MuzzleEffectScene, origin, aimedDirection);
		_releaseDirections.AddRange(GetProjectileDirections(aimedDirection));
	}

	/// <summary>
	/// Casts the attack's spell once at its contact time; a spell freed with its focus no longer casts. Like a projectile,
	/// the cast rolls from its own seeded stream, so its lingering contacts never shift this controller's later draws.
	/// </summary>
	private void ReleaseSpell(SpellRelease release)
	{
		SpawnEffect(_currentAttack.CastEffectScene, _actor.GlobalPosition, GetActorForward());
		var random = new RandomNumberGenerator { Seed = _random.Randi() };
		bool cast = release.Spell is GodotObject spell && GodotObject.IsInstanceValid(spell)
			&& release.Spell.Cast(_actor, release.Power, random);
		ReportFeedback(cast ? "Cast" : "Fizzled");
		if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.spell_released", ("execution", _executionSequence),
			("spell", release.Spell?.DisplayName ?? ""), ("level", release.Power.Level), ("cast", cast));
	}

	/// <summary>
	/// Spawn every captured projectile whose staggered release time has passed, so a tick that skips several
	/// release times still fires each of them. Cancelling the attack discards the rest.
	/// </summary>
	private void ReleaseDueProjectiles()
	{
		while (_projectilesReleased < _releaseDirections.Count
			&& _elapsedTime >= _currentAttack.HitWindowStart + _projectilesReleased * _currentAttack.ProjectileStagger)
		{
			SpawnProjectile(_releaseDirections[_projectilesReleased++]);
		}
	}

	/// <summary>A staggered projectile leaves from the current muzzle; an aim-locked shot keeps its committed origin.</summary>
	private void SpawnProjectile(Vector3 direction)
	{
		Vector3 origin = IsProjectileAimLocked ? _projectileAimOrigin : GetProjectileOrigin();
		Projectile projectile = ScenePool.Spawn<Projectile>(CurrentProjectileScene, (GetTree().CurrentScene ?? _actor.GetParent()));
		ApplyProjectileOverrides(projectile);
		projectile.Initialize(
			origin,
			direction,
			_currentAttack.ProjectileSpeed,
			_currentAttack.Range,
			_currentHit.Scaled(_currentAttack.ProjectileDamageScale),
			_actor, _currentAttack.DefenseTraits, _currentAttack.AreaDefenseTraits, _random.Randi(), CurrentExecutionId);
		projectile.ConfigureFlight(_currentAttack.Flight, _releaseTarget);

		GameDebug.Combat($"{_actor.Name} spawned projectile flying {direction}");
	}

	private void ApplyProjectileOverrides(Projectile projectile)
	{
		projectile.Impact = _currentAttack.Impact;
		projectile.Element = _currentAttack.Element;
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
		effect.ResetPhysicsInterpolation();
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

	/// <summary>
	/// The release-time aim target: the opponent nearest the actor's facing inside the aiming cone, range and line
	/// of sight, else, for the player, the nearest such damageable object. Null aims along the actor's facing.
	/// </summary>
	private Node3D FindReleaseTarget(float range, float aimingAngle, out float fallbackHeight)
	{
		bool isPlayer = _actor.IsInGroup("player");
		string targetGroup = isPlayer ? "enemy" : "player";
		fallbackHeight = 1.0f;

		var target = GetTree().GetNodesInGroup(targetGroup).OfType<Node3D>()
			.Where(candidate => IsValidAimTarget(candidate) && TestLineOfSight(candidate, range, aimingAngle))
			.OrderBy(GetAimAngle)
			.ThenBy(candidate => _actor.GlobalPosition.DistanceTo(candidate.GlobalPosition))
			.FirstOrDefault();
		if (target != null || !isPlayer) return target;

		fallbackHeight = 0.5f;
		return GetTree().GetNodesInGroup("damageable").OfType<Node3D>()
			.Where(node => node is IDamageable
				&& !IsOwnedByActor(node)
				&& TestLineOfSight(node, range, aimingAngle))
			.OrderBy(GetAimAngle)
			.ThenBy(node => _actor.GlobalPosition.DistanceTo(node.GlobalPosition))
			.FirstOrDefault();
	}

	/// <summary>Keep the AI's chosen target when available; never switch targets after accepting an aimed shot.</summary>
	private Node3D FindProjectileAimTarget(float range, float aimingAngle)
	{
		if (_actor is EnemyBase enemy && GodotObject.IsInstanceValid(enemy.EnemyBehaviorComponent?.Target))
		{
			var target = enemy.EnemyBehaviorComponent.Target;
			// The strategy already chose this target; wind-up turns toward it even before the body has rotated.
			return TestLineOfSight(target, range, 180) ? target : null;
		}
		string group = _actor.IsInGroup("player") ? "enemy" : "player";
		return GetTree().GetNodesInGroup(group).OfType<Node3D>()
			.Where(target => TestLineOfSight(target, range, aimingAngle))
			.OrderBy(GetAimAngle).ThenBy(target => _actor.GlobalPosition.DistanceSquaredTo(target.GlobalPosition))
			.FirstOrDefault();
	}

	/// <summary>One gameplay clock owns tracking, commitment and release; presentation never supplies aim.</summary>
	private void UpdateProjectileAim()
	{
		if (!GodotObject.IsInstanceValid(_projectileAimTarget)) return;
		if (!HasCommittedFacing)
		{
			_projectileAimOrigin = GetProjectileOrigin();
			_projectileAimDirection = GetDirectionToTarget(_projectileAimOrigin, _projectileAimTarget, 1);
			_projectileAimLength = Mathf.Min(_currentAttack.Range,
				_projectileAimOrigin.DistanceTo(GetTargetCenter(_projectileAimTarget, 1)) + .75f);
			if (_elapsedTime >= _currentAttack.HitWindowStart - _currentAttack.ProjectileAimLockDuration)
			{
				HasCommittedFacing = true;
				CommittedFacing = _projectileAimDirection;
				if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.aim_locked", ("execution", _executionSequence),
					("origin", CombatTrace.Vector(_projectileAimOrigin)), ("direction", CombatTrace.Vector(CommittedFacing)),
					("release_in", _currentAttack.HitWindowStart - _elapsedTime));
			}
		}
		Vector3 end = _projectileAimOrigin + _projectileAimDirection * _projectileAimLength;
		// Only incoming shots get warnings. Ordinary depth testing and sight checks avoid revealing shooters through walls.
		bool visible = _actor.IsInGroup("enemy") && _projectileAimTarget.IsInGroup("player")
			&& (_actor as EnemyBase)?.Invisibility?.IsInvisible != true
			&& WorldSight.IsClear(_actor.GetWorld3D(), _projectileAimOrigin, end);
		if (visible) AimIndicator?.ShowAim(_projectileAimOrigin, end, HasCommittedFacing);
		else AimIndicator?.Clear();
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

	private bool IsValidAimTarget(Node node) => !IsOwnedByActor(node) && IsLivingTarget(node);

	/// <summary>
	/// A target that still exists and, when it is an actor or an actor's part, is not dead. Shared with homing
	/// projectiles, which give up a target that fails it.
	/// </summary>
	public static bool IsLivingTarget(Node node)
	{
		if (!GodotObject.IsInstanceValid(node)) return false;
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

	private float GetAimAngle(Node3D target) => GetActorForward().HorizontalAngleTo(target.GlobalPosition - _actor.GlobalPosition);

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

		return WorldSight.IsClear(_actor.GetWorld3D(), GetProjectileOrigin(), GetTargetCenter(target, 1),
			(collider, _) => collider is Node node && (node == target || target.IsAncestorOf(node) || node.IsAncestorOf(target)));
	}
}