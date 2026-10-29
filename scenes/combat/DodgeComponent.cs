using Godot;
using System;

[GlobalClass]
public partial class DodgeComponent : Node, IDefenseEvaluator
{
	[Signal] public delegate void DodgeStartedEventHandler(Vector3 direction);
	[Signal] public delegate void DodgeEndedEventHandler();

	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public DodgeDefinition DodgeDef { get; set; }
	[Export] public CombatAnimationComponent AnimationComponent { get; set; }

	private DodgeDefinition _activeDefinition;
	private DodgeDefinition EffectiveDefinition => _activeDefinition ?? DodgeDef;

	public bool IsDodging { get; private set; }
	public float ElapsedTime => _dodgeElapsed;
	public bool IsImmune => IsDodging && EffectiveDefinition != null
		&& _dodgeElapsed >= EffectiveDefinition.ImmunityStart && _dodgeElapsed < EffectiveDefinition.ImmunityEnd;
	public float RecoveryRemaining => IsDodging ? Mathf.Max(0, EffectiveDefinition.TravelDuration + EffectiveDefinition.RecoveryDuration - _dodgeElapsed) : 0;

	/// <summary>Seconds left of the minimum interval between dodges.</summary>
	public float IntervalRemaining { get; private set; }
	/// <summary>Charges available now; spent charges refill one at a time.</summary>
	public int ChargesRemaining => Mathf.Max(0, (DodgeDef?.MaxCharges ?? 1) - _spentCharges);
	/// <summary>Seconds until the next spent charge refills; zero while every charge is available.</summary>
	public float RechargeRemaining { get; private set; }
	/// <summary>Seconds until another dodge may start: the interval, or the next recharge when no charge is left.</summary>
	public float CooldownRemaining => ChargesRemaining > 0 ? IntervalRemaining : Mathf.Max(IntervalRemaining, RechargeRemaining);

	private float _dodgeElapsed;
	private int _spentCharges;
	private bool _perfectRefunded;
	private Vector3 _dodgeDirection;
	private Vector3 _dodgeFacing;
	private float _dodgeSpeed;
	private ActorActionLease _dodgeLease = ActorActionLease.None;
	// Layers this dodge actually removed from the actor's mask; restored on every exit path.
	private uint _passThroughMask;
	private float _exitElapsed;
	// Shape queries for the exit check, built once per pass-through dodge; only their transforms change.
	private readonly System.Collections.Generic.List<(PhysicsShapeQueryParameters3D Query, Node3D Shape)> _exitQueries = [];

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Defense;
		SetPhysicsProcess(false);
	}

	public DefenseRequestResult TryDodge(Vector3 direction)
	{
		var result = TryDodgeInternal(direction);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.request", ("kind", "dodge"), ("result", result.ToString()), ("direction", CombatTrace.Vector(direction)));
		return result;
	}

	private DefenseRequestResult TryDodgeInternal(Vector3 direction)
	{
		if (DodgeDef == null)
		{
			return DefenseRequestResult.Unavailable;
		}

		if (IsDodging)
		{
			return DefenseRequestResult.Busy;
		}

		if (CooldownRemaining > 0)
		{
			return DefenseRequestResult.Cooldown;
		}

		Vector3 horiz = new(direction.X, 0, direction.Z);
		if (horiz.LengthSquared() < 0.0001f)
		{
			horiz = MovementComponent != null ? MovementComponent.LookAtDirection : (Actor != null ? -Actor.GlobalTransform.Basis.Z : Vector3.Forward);
			horiz.Y = 0;
			if (horiz.LengthSquared() < 0.0001f) horiz = Vector3.Forward;
		}
		_dodgeDirection = horiz.Normalized();

		if (ActionGate == null)
		{
			return DefenseRequestResult.Disabled;
		}

		if (Actor is Player { IsDead: true } || Actor is EnemyBase { IsDead: true } || MovementComponent?.IsPushed == true)
			return DefenseRequestResult.Disabled;
		if (!float.IsFinite(DodgeDef.TravelDuration) || DodgeDef.TravelDuration <= 0 || !float.IsFinite(DodgeDef.Distance) || DodgeDef.Distance < 0
			|| !float.IsFinite(DodgeDef.RecoveryDuration) || DodgeDef.RecoveryDuration < 0 || !float.IsFinite(DodgeDef.CooldownDuration) || DodgeDef.CooldownDuration < 0
			|| !float.IsFinite(DodgeDef.ImmunityStart) || !float.IsFinite(DodgeDef.ImmunityEnd)
			|| DodgeDef.ImmunityStart < 0 || DodgeDef.ImmunityEnd < DodgeDef.ImmunityStart || DodgeDef.ImmunityEnd > DodgeDef.TravelDuration
			|| !float.IsFinite(DodgeDef.PassThroughExitTime) || DodgeDef.PassThroughExitTime < 0
			|| !(DodgeDef.PassThroughExitSpeedScale >= 0 && DodgeDef.PassThroughExitSpeedScale <= 1) || !DodgeDef.HasValidCharges)
			return DefenseRequestResult.InvalidDefinition;
		Vector3 facing = MovementComponent?.LookAtDirection ?? (Actor != null ? -Actor.GlobalTransform.Basis.Z : Vector3.Forward);
		if (AnimationComponent != null && !AnimationComponent.HasClip(ChooseDodgeClip(facing, DodgeDef.AnimationProfile))) return DefenseRequestResult.Unavailable;

		var acquireResult = ActionGate.TryAcquire(
			ActorActionKind.Dodge,
			ActorActionPhase.Active,
			OnActionInterrupted,
			out var newLease);

		if (acquireResult != ActorActionRequestResult.Success)
		{
			return DefenseRequestResult.Busy;
		}

		_activeDefinition = (DodgeDefinition)DodgeDef.Duplicate(true);
		_dodgeLease = newLease;
		IsDodging = true;
		_dodgeElapsed = 0f;
		_dodgeFacing = facing;
		_dodgeSpeed = EffectiveDefinition.Distance / Mathf.Max(0.01f, EffectiveDefinition.TravelDuration);

		MovementComponent?.SetInputDirection(Vector3.Zero);
		MovementComponent?.SetLocomotionOverride(this, _dodgeDirection, _dodgeSpeed);
		MovementComponent?.SetFixedFacing(this, _dodgeFacing);
		IntervalRemaining = EffectiveDefinition.CooldownDuration;
		_perfectRefunded = false;
		if (EffectiveDefinition.ChargeRechargeDuration > 0)
		{
			if (_spentCharges == 0) RechargeRemaining = EffectiveDefinition.ChargeRechargeDuration;
			_spentCharges++;
		}
		BeginPassThrough();

		SetPhysicsProcess(true);
		PlayDodgeAnimation();
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.started", ("kind", "dodge"), ("travel", EffectiveDefinition.TravelDuration),
			("recovery", EffectiveDefinition.RecoveryDuration), ("distance", EffectiveDefinition.Distance),
			("immunity_start", EffectiveDefinition.ImmunityStart), ("immunity_end", EffectiveDefinition.ImmunityEnd),
			("interval", IntervalRemaining), ("charges", ChargesRemaining));
		EmitSignalDodgeStarted(_dodgeDirection);
		return DefenseRequestResult.Success;
	}

	public void Cancel(ActionInterruptReason reason)
	{
		if (!IsDodging) return;
		IsDodging = false;
		EndPassThrough();
		MovementComponent?.ClearLocomotionOverride(this);
		if (ActionGate?.IsLeaseCurrent(_dodgeLease) == true) ActionGate.CancelCurrent(reason);
		_dodgeLease = ActorActionLease.None;
		StopDodgeAnimation();
		EmitSignalDodgeEnded();
		if (!IsTimerRunning) SetPhysicsProcess(false);
	}

	public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
	{
		if (contact.DeliveryKind is not (DamageDeliveryKind.Hazard or DamageDeliveryKind.Environmental)
			&& IsDodging && DodgeDef != null
			&& _dodgeElapsed >= EffectiveDefinition.ImmunityStart
			&& _dodgeElapsed < EffectiveDefinition.ImmunityEnd)
		{
			if ((contact.DefenseTraits & AttackDefenseTraits.Dodgeable) != 0)
			{
				RefundPerfectDodge();
				evaluation = new DefenseEvaluation(DamageOutcome.Dodged, 0f, ConsumesParry: false, SuppressHitStun: true);
				return true;
			}
		}

		evaluation = default;
		return false;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		IntervalRemaining = Mathf.Max(0, IntervalRemaining - dt);
		UpdateRecharge(dt);

		if (IsDodging)
		{
			float previous = _dodgeElapsed;
			_dodgeElapsed += dt;
			bool travelEnded = previous >= EffectiveDefinition.TravelDuration;
			AnimationComponent?.SamplePose(this, ActionGate?.CurrentPhase == ActorActionPhase.Recovery
				? _dodgeElapsed - EffectiveDefinition.TravelDuration
				: _dodgeElapsed / EffectiveDefinition.TravelDuration * AnimationComponent.PoseLength(this));
			// Ending inside a pass-through body keeps the dodge moving out, without immunity, before collision returns.
			bool exiting = travelEnded && UpdatePassThroughExit(dt);
			if (!travelEnded)
			{
				if (MovementComponent?.HasOverrideVelocity == true && dt > 0)
					MovementComponent.SetLocomotionOverride(this, _dodgeDirection,
						_dodgeSpeed * Mathf.Min(1, (EffectiveDefinition.TravelDuration - previous) / dt));
			}
			else if (!exiting) MovementComponent?.ClearLocomotionOverride(this);
			// Fresh steering may queue the next turn, but a sidestep keeps its starting orientation through recovery.
			MovementComponent?.SetFixedFacing(this, _dodgeFacing);

			if (_dodgeElapsed >= EffectiveDefinition.TravelDuration && ActionGate?.CurrentPhase == ActorActionPhase.Active)
			{
				if (EffectiveDefinition.RecoveryDuration > 0)
				{
					ActionGate?.ReportPhase(_dodgeLease, ActorActionPhase.Recovery);
					PlayDodgeExitAnimation(0.08f);
				}
				else if (travelEnded && !exiting)
				{
					FinishDodge();
				}
			}

			float totalDodgeTime = EffectiveDefinition.TravelDuration + EffectiveDefinition.RecoveryDuration;
			if (_dodgeElapsed >= totalDodgeTime && !exiting && (EffectiveDefinition.RecoveryDuration > 0 || travelEnded))
			{
				FinishDodge();
			}
		}

		if (!IsDodging && !IsTimerRunning)
		{
			SetPhysicsProcess(false);
		}
	}

	private bool IsTimerRunning => IntervalRemaining > 0 || _spentCharges > 0;

	/// <summary>Refill spent charges one at a time; a removed recharge duration refills them at once.</summary>
	private void UpdateRecharge(float dt)
	{
		float duration = DodgeDef?.ChargeRechargeDuration ?? 0;
		_spentCharges = Mathf.Min(_spentCharges, DodgeDef?.MaxCharges ?? 0);
		if (_spentCharges == 0 || !(duration > 0)) { _spentCharges = 0; RechargeRemaining = 0; return; }
		RechargeRemaining -= dt;
		if (RechargeRemaining > 0) return;
		_spentCharges--;
		RechargeRemaining = _spentCharges > 0 ? RechargeRemaining + duration : 0;
	}

	/// <summary>Return the charge this dodge spent when it evades a contact inside the perfect window.</summary>
	private void RefundPerfectDodge()
	{
		if (_perfectRefunded || _dodgeElapsed > EffectiveDefinition.PerfectWindow || EffectiveDefinition.PerfectWindow <= 0) return;
		_perfectRefunded = true;
		if (_spentCharges > 0 && --_spentCharges == 0) RechargeRemaining = 0;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "defense.perfect_dodge", ("elapsed", _dodgeElapsed), ("charges", ChargesRemaining));
	}

	private void FinishDodge()
	{
		if (!IsDodging) return;
		IsDodging = false;
		EndPassThrough();
		MovementComponent?.ClearLocomotionOverride(this);
		if (ActionGate != null && ActionGate.IsLeaseCurrent(_dodgeLease))
		{
			ActionGate.Release(_dodgeLease);
		}
		_dodgeLease = ActorActionLease.None;

		RestoreTreePlayback();
		EmitSignalDodgeEnded();
	}

	private void OnActionInterrupted(ActionInterruptReason reason)
	{
		if (!IsDodging) return;
		IsDodging = false;
		EndPassThrough();
		MovementComponent?.ClearLocomotionOverride(this);
		_dodgeLease = ActorActionLease.None;
		StopDodgeAnimation();
		EmitSignalDodgeEnded();
	}

	private void BeginPassThrough()
	{
		_exitElapsed = 0;
		_exitQueries.Clear();
		_passThroughMask = Actor != null ? Actor.CollisionMask & EffectiveDefinition.PassThroughLayers : 0;
		if (_passThroughMask == 0) return;
		Actor.CollisionMask &= ~_passThroughMask;
		var exclude = new Godot.Collections.Array<Rid> { Actor.GetRid() };
		foreach (uint owner in Actor.GetShapeOwners())
		{
			if (Actor.IsShapeOwnerDisabled(owner) || Actor.ShapeOwnerGetOwner(owner) is not Node3D shapeNode) continue;
			for (int i = 0; i < Actor.ShapeOwnerGetShapeCount(owner); i++)
				_exitQueries.Add((new PhysicsShapeQueryParameters3D
				{
					Shape = Actor.ShapeOwnerGetShape(owner, i),
					CollisionMask = _passThroughMask,
					Exclude = exclude
				}, shapeNode));
		}
	}

	private void EndPassThrough()
	{
		if (_passThroughMask == 0) return;
		if (GodotObject.IsInstanceValid(Actor)) Actor.CollisionMask |= _passThroughMask;
		_passThroughMask = 0;
	}

	/// <summary>True while the actor still overlaps a pass-through body and may keep pushing out.</summary>
	private bool UpdatePassThroughExit(float dt)
	{
		if (_passThroughMask == 0) return false;
		if (_exitElapsed < EffectiveDefinition.PassThroughExitTime && OverlapsPassThroughBody())
		{
			_exitElapsed += dt;
			MovementComponent?.SetLocomotionOverride(this, _dodgeDirection, _dodgeSpeed * EffectiveDefinition.PassThroughExitSpeedScale);
			return true;
		}
		if (CombatTrace.Enabled && _exitElapsed > 0) CombatTrace.Write(this, "defense.pass_through_exit", ("elapsed", _exitElapsed));
		EndPassThrough();
		return false;
	}

	private bool OverlapsPassThroughBody()
	{
		if (!GodotObject.IsInstanceValid(Actor) || !Actor.IsInsideTree()) return false;
		var space = Actor.GetWorld3D().DirectSpaceState;
		foreach (var (query, shape) in _exitQueries)
		{
			query.Transform = shape.GlobalTransform;
			if (space.IntersectShape(query, 1).Count > 0) return true;
		}
		return false;
	}

	/// <summary>Restore saved charge and interval timers; values are clamped to the current definition.</summary>
	public void RestoreCharges(int chargesRemaining, float rechargeRemaining, float intervalRemaining)
	{
		int max = DodgeDef?.MaxCharges ?? 1;
		_spentCharges = DodgeDef?.ChargeRechargeDuration > 0 ? Mathf.Clamp(max - chargesRemaining, 0, max) : 0;
		RechargeRemaining = _spentCharges > 0 ? Mathf.Clamp(rechargeRemaining, 0, DodgeDef.ChargeRechargeDuration) : 0;
		IntervalRemaining = Mathf.Max(0, intervalRemaining);
		if (IsTimerRunning) SetPhysicsProcess(true);
	}

	private void PlayDodgeAnimation() => AnimationComponent?.BeginPose(this, ChooseDodgeClip(_dodgeFacing, EffectiveDefinition.AnimationProfile), 0.08f);
	private void PlayDodgeExitAnimation(float duration) => AnimationComponent?.BeginPose(this, EffectiveDefinition.AnimationProfile?.RecoveryClip ?? "", duration, true);
	private void StopDodgeAnimation() => AnimationComponent?.EndPose(this);
	private void RestoreTreePlayback() => AnimationComponent?.EndPose(this);
	private StringName ChooseDodgeClip(Vector3 forward, DefenseAnimationProfile profile)
	{
		if (profile == null) return "";
		Vector3 right = forward.Cross(Vector3.Up);
		float fwd = _dodgeDirection.Dot(forward), side = _dodgeDirection.Dot(right);
		return Mathf.Abs(fwd) >= Mathf.Abs(side)
			? (fwd >= 0 ? profile.DodgeForwardClip : profile.DodgeBackwardClip)
			: (side >= 0 ? profile.DodgeRightClip : profile.DodgeLeftClip);
	}
	public override void _ExitTree()
	{
		Cancel(ActionInterruptReason.Invalidated);
		Actor?.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent")?.UnregisterDefenseEvaluator(this);
		MovementComponent?.ClearLocomotionOverride(this);
	}
}