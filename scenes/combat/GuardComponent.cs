using Godot;
using System;

[GlobalClass]
public partial class GuardComponent : Node, IDefenseEvaluator
{
	[Signal] public delegate void GuardStartedEventHandler(Vector3 facing);
	[Signal] public delegate void GuardEndedEventHandler();
	[Signal] public delegate void ParrySuccessfulEventHandler();
	[Signal] public delegate void BlockSuccessfulEventHandler(float chipDamage);

	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public GuardDefinition GuardDef { get; set; }
	[Export] public CombatAnimationComponent AnimationComponent { get; set; }

	private GuardDefinition _activeDefinition;
	private GuardDefinition EffectiveDefinition => _activeDefinition ?? GuardDef;

	public bool IsGuarding { get; private set; }
	public float ElapsedTime => _guardElapsed;
	public bool ParryWindowOpen => IsGuarding && _isParryEligible && EffectiveDefinition != null
		&& _guardElapsed >= EffectiveDefinition.RaiseDuration && _guardElapsed < EffectiveDefinition.ParryWindow;
	public bool IsGuardRecovering { get; private set; }
	public Vector3 GuardFacing { get; private set; } = Vector3.Forward;
	public float RecoveryRemaining => IsGuardRecovering ? _guardRecoveryRemaining : 0;
	public float ParryRearmRemaining { get; private set; }

	public Vector3 GetEffectiveGuardFacing()
	{
		return GuardFacing.LengthSquared() > 0.001f ? GuardFacing.Normalized() : Vector3.Forward;
	}

	private float _guardElapsed;
	private float _timeSinceGuardRelease = 100f;
	private float _guardRecoveryRemaining;
	private bool _isParryEligible;
	private ActorActionLease _guardLease = ActorActionLease.None;
	private bool _isReactionPlaying;
	private float _reactionRemaining;

	public override void _Ready()
	{
		Actor ??= GetParentOrNull<CharacterBody3D>();
		MovementComponent ??= Actor?.GetNodeOrNull<MovementComponent>("MovementComponent") ?? GetNodeOrNull<MovementComponent>("MovementComponent");
		ActionGate ??= Actor?.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent") ?? GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");

		ProcessPhysicsPriority = CombatPhysics.Defense;
		AnimationComponent ??= Actor?.GetNodeOrNull<CombatAnimationComponent>("CombatAnimationComponent");
		SetPhysicsProcess(false);
	}

	public DefenseRequestResult TryBeginGuard(Vector3 facing)
	{
		var result = TryBeginGuardInternal(facing);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.request", ("kind", "guard"), ("result", result.ToString()), ("direction", CombatTrace.Vector(facing)));
		return result;
	}

	private DefenseRequestResult TryBeginGuardInternal(Vector3 facing)
	{
		if (GuardDef == null)
		{
			return DefenseRequestResult.Unavailable;
		}

		if (IsGuarding)
		{
			return DefenseRequestResult.Busy;
		}

		if (IsGuardRecovering) return DefenseRequestResult.Busy;

		Vector3 horiz = new(facing.X, 0, facing.Z);
		if (horiz.LengthSquared() < 0.0001f)
		{
			horiz = MovementComponent != null ? MovementComponent.LookAtDirection : (Actor != null ? -Actor.GlobalTransform.Basis.Z : Vector3.Forward);
			horiz.Y = 0;
			if (horiz.LengthSquared() < 0.0001f) horiz = Vector3.Forward;
		}

		if (ActionGate == null)
		{
			return DefenseRequestResult.Disabled;
		}

		if (Actor is Player { IsDead: true } || Actor is EnemyBase { IsDead: true } || MovementComponent?.IsPushed == true)
			return DefenseRequestResult.Disabled;
		if (!float.IsFinite(GuardDef.RaiseDuration) || GuardDef.RaiseDuration < 0 || !float.IsFinite(GuardDef.ReleaseRecovery) || GuardDef.ReleaseRecovery < 0
			|| !float.IsFinite(GuardDef.ParryWindow) || GuardDef.ParryWindow < GuardDef.RaiseDuration || !float.IsFinite(GuardDef.DamageMultiplier)
			|| GuardDef.DamageMultiplier < 0 || GuardDef.DamageMultiplier > 1
			|| !float.IsFinite(GuardDef.MovementSpeedMultiplier) || GuardDef.MovementSpeedMultiplier < 0 || GuardDef.MovementSpeedMultiplier > 1
			|| !float.IsFinite(GuardDef.RearmInterval) || GuardDef.RearmInterval < 0
			|| !float.IsFinite(GuardDef.MinimumReleaseInterval) || GuardDef.MinimumReleaseInterval < 0
			|| !float.IsFinite(GuardDef.FacingSectorDegrees) || GuardDef.FacingSectorDegrees <= 0 || GuardDef.FacingSectorDegrees > 360
			|| !float.IsFinite(GuardDef.ParryStaggerDuration) || GuardDef.ParryStaggerDuration < 0) return DefenseRequestResult.InvalidDefinition;
		if (AnimationComponent != null && !AnimationComponent.HasClip(GuardDef.AnimationProfile?.GuardHoldClip ?? "")) return DefenseRequestResult.Unavailable;

		var acquireResult = ActionGate.TryAcquire(
			ActorActionKind.Guard,
			ActorActionPhase.WindUp,
			OnActionInterrupted,
			out var newLease);

		if (acquireResult != ActorActionRequestResult.Success)
		{
			return DefenseRequestResult.Busy;
		}

		_activeDefinition = (GuardDefinition)GuardDef.Duplicate(true);
		_guardLease = newLease;
		IsGuarding = true;
		IsGuardRecovering = false;
		_guardElapsed = 0f;
		_isReactionPlaying = false;
		GuardFacing = horiz.Normalized();
		MovementComponent?.SetLookAtDirection(GuardFacing);

		bool parryAllowed = EffectiveDefinition.CanParry
			&& ParryRearmRemaining <= 0
			&& _timeSinceGuardRelease >= EffectiveDefinition.MinimumReleaseInterval;

		_isParryEligible = parryAllowed;
		if (parryAllowed)
		{
			ParryRearmRemaining = EffectiveDefinition.RearmInterval;
		}

		MovementComponent?.SetSpeedMultiplier(this, EffectiveDefinition.MovementSpeedMultiplier);
		MovementComponent?.SetFixedFacing(this, GuardFacing);
		MovementComponent?.SetInputDirection(Vector3.Zero);
		MovementComponent?.Stop();

		SetPhysicsProcess(true);
		PlayGuardAnimation();
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.started", ("kind", "guard"), ("raise", EffectiveDefinition.RaiseDuration),
			("parry_window", EffectiveDefinition.ParryWindow), ("parry_eligible", _isParryEligible), ("rearm", ParryRearmRemaining),
			("release_recovery", EffectiveDefinition.ReleaseRecovery), ("sector", EffectiveDefinition.FacingSectorDegrees), ("damage_multiplier", EffectiveDefinition.DamageMultiplier));
		EmitSignalGuardStarted(GuardFacing);
		return DefenseRequestResult.Success;
	}

	/// <summary>Normal release finishes the current contact reaction before recovery; forced cancellation can skip both.</summary>
	public void EndGuard(bool immediate = false)
	{
		if (!IsGuarding)
		{
			if (immediate && IsGuardRecovering) FinishGuardRelease();
			return;
		}
		IsGuarding = false;
		_timeSinceGuardRelease = 0f;

		float recoveryDuration = (EffectiveDefinition?.ReleaseRecovery ?? 0f)
			+ (_isReactionPlaying ? Mathf.Max(0, _reactionRemaining) : 0f);
		if (!immediate && recoveryDuration > 0)
		{
			IsGuardRecovering = true;
			_guardRecoveryRemaining = recoveryDuration;
			ActionGate?.ReportPhase(_guardLease, ActorActionPhase.Recovery);
			if (!_isReactionPlaying) PlayGuardExitAnimation(recoveryDuration);
		}
		else
		{
			StopGuardAnimation();
			FinishGuardRelease();
		}
	}

	private void FinishGuardRelease()
	{
		IsGuardRecovering = false;
		MovementComponent?.ClearLocomotionOverride(this);
		if (ActionGate != null && ActionGate.IsLeaseCurrent(_guardLease))
		{
			ActionGate.Release(_guardLease);
		}
		_guardLease = ActorActionLease.None;

		RestoreTreePlayback();
		EmitSignalGuardEnded();
	}

	public void Cancel(ActionInterruptReason reason)
	{
		if (!IsGuarding && !IsGuardRecovering) return;
		IsGuarding = false;
		IsGuardRecovering = false;
		_timeSinceGuardRelease = 0f;
		MovementComponent?.ClearLocomotionOverride(this);
		if (ActionGate?.IsLeaseCurrent(_guardLease) == true) ActionGate.CancelCurrent(reason);
		_guardLease = ActorActionLease.None;
		StopGuardAnimation();
		EmitSignalGuardEnded();
	}

	private void OnActionInterrupted(ActionInterruptReason reason)
	{
		if (!IsGuarding && !IsGuardRecovering) return;
		IsGuarding = false;
		IsGuardRecovering = false;
		_timeSinceGuardRelease = 0f;
		MovementComponent?.ClearLocomotionOverride(this);
		_guardLease = ActorActionLease.None;
		StopGuardAnimation();
		EmitSignalGuardEnded();
	}

	public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
	{
		if (IsGuarding && EffectiveDefinition != null && _guardElapsed >= EffectiveDefinition.RaiseDuration)
		{
			if ((contact.DefenseTraits & AttackDefenseTraits.Guardable) != 0)
			{
				Vector3 incoming = contact.IncomingDirection;
				if (incoming.LengthSquared() < 0.0001f)
				{
					Node3D attacker = contact.OwnerNode as Node3D ?? contact.SourceNode as Node3D;
					if (attacker != null && Actor != null)
					{
						Transform3D xform = Actor.IsInsideTree() ? Actor.GlobalTransform : Actor.Transform;
						Transform3D atkXform = attacker.IsInsideTree() ? attacker.GlobalTransform : attacker.Transform;
						incoming = (xform.Origin - atkXform.Origin).Normalized();
					}
				}

				Vector3 horizOpposite = new(-incoming.X, 0, -incoming.Z);
				Vector3 horizFacing = GetEffectiveGuardFacing();

				if (horizOpposite.LengthSquared() > 0.0001f && horizFacing.LengthSquared() > 0.0001f)
				{
					float angle = Mathf.RadToDeg(horizFacing.Normalized().AngleTo(horizOpposite.Normalized()));
					if (angle <= EffectiveDefinition.FacingSectorDegrees * 0.5f)
					{
						bool parryWindowActive = _isParryEligible && _guardElapsed < EffectiveDefinition.ParryWindow;
						if (parryWindowActive && (contact.DefenseTraits & AttackDefenseTraits.Parryable) != 0)
						{
							_isParryEligible = false;

							// Only the captured executor can authorize interruption of its current attack.
							if (GodotObject.IsInstanceValid(contact.SourceNode) && contact.SourceNode is IParryInterruptible source)
								source.TryInterruptForParry(contact.ExecutionId, EffectiveDefinition.ParryStaggerDuration);

							PlayParryAnimation();
							EmitSignalParrySuccessful();
							evaluation = new DefenseEvaluation(DamageOutcome.Parried, 0f, ConsumesParry: true, SuppressHitStun: true);
							return true;
						}

						PlayBlockHitAnimation();
						float chipDamage = contact.Amount * EffectiveDefinition.DamageMultiplier;
						EmitSignalBlockSuccessful(chipDamage);
						evaluation = new DefenseEvaluation(DamageOutcome.Blocked, chipDamage, ConsumesParry: false, SuppressHitStun: true);
						return true;
					}
				}
			}
		}

		evaluation = default;
		return false;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_poseElapsed += dt;
		AnimationComponent?.SamplePose(this, _poseElapsed);

		if (ParryRearmRemaining > 0)
		{
			ParryRearmRemaining = Mathf.Max(0, ParryRearmRemaining - dt);
		}
		_timeSinceGuardRelease += dt;

		if (_isReactionPlaying)
		{
			_reactionRemaining -= dt;
			if (_reactionRemaining <= 0)
			{
				_isReactionPlaying = false;
				if (IsGuarding)
				{
					PlayGuardAnimation();
				}
				else if (IsGuardRecovering)
				{
					PlayGuardExitAnimation(_guardRecoveryRemaining);
				}
			}
		}

		if (IsGuarding)
		{
			_guardElapsed += dt;
			if (_guardElapsed >= EffectiveDefinition.RaiseDuration && ActionGate?.CurrentPhase == ActorActionPhase.WindUp)
			{
				ActionGate?.ReportPhase(_guardLease, ActorActionPhase.Active);
			}
		}
		else if (IsGuardRecovering)
		{
			_guardRecoveryRemaining -= dt;
			if (_guardRecoveryRemaining <= 0)
			{
				FinishGuardRelease();
			}
		}

		if (!IsGuarding && !IsGuardRecovering && ParryRearmRemaining <= 0 && !_isReactionPlaying
			&& _timeSinceGuardRelease >= (EffectiveDefinition?.MinimumReleaseInterval ?? 0))
		{
			SetPhysicsProcess(false);
		}
	}

	public void RestoreCooldown(float parryRearm)
	{
		Cancel(ActionInterruptReason.Invalidated);
		_timeSinceGuardRelease = 0;
		ParryRearmRemaining = Mathf.Max(0, parryRearm);
		SetPhysicsProcess(true);
		if (ParryRearmRemaining > 0)
		{
			SetPhysicsProcess(true);
		}
	}

	public void ApplyGuardDefinition(GuardDefinition def)
	{
		if (ReferenceEquals(GuardDef, def)) return;
		Cancel(ActionInterruptReason.EquipChanged);
		GuardDef = def;
		_activeDefinition = null;
	}

	private double _poseElapsed;
	private void PlayGuardAnimation()
	{
		_poseElapsed = 0;
		AnimationComponent?.BeginPose(this, EffectiveDefinition?.AnimationProfile?.GuardHoldClip ?? "", 0.12f, true);
	}
	private void PlayGuardExitAnimation(float duration)
	{
		_isReactionPlaying = false;
		_poseElapsed = 0;
		AnimationComponent?.BeginPose(this, EffectiveDefinition?.AnimationProfile?.RecoveryClip ?? "", duration, true);
	}
	private void StopGuardAnimation()
	{
		_isReactionPlaying = false;
		AnimationComponent?.EndPose(this);
	}
	private void RestoreTreePlayback() => StopGuardAnimation();
	private void PlayReaction(StringName clip)
	{
		_poseElapsed = 0;
		if (AnimationComponent?.BeginPose(this, clip, 0.08f) == true)
		{
			_isReactionPlaying = true;
			_reactionRemaining = (float)AnimationComponent.PoseLength(this);
		}
	}
	private void PlayParryAnimation() => PlayReaction(EffectiveDefinition?.AnimationProfile?.ParryClip ?? "");
	private void PlayBlockHitAnimation() => PlayReaction(EffectiveDefinition?.AnimationProfile?.BlockHitClip ?? "");
	public override void _ExitTree()
	{
		Cancel(ActionInterruptReason.Invalidated);
		Actor?.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent")?.UnregisterDefenseEvaluator(this);
		MovementComponent?.ClearLocomotionOverride(this);
	}
}