using System;
using System.Collections.Generic;
using Godot;

/// <summary>The source of horizontal motion selected for the latest physics step.</summary>
public enum MovementMotionKind { None, Locomotion, Override, Push, Lunge }

[GlobalClass]
public partial class MovementComponent : Node
{
	/// <summary>
	/// The actor that will be moved by this component.
	/// </summary>
	[Export] public CharacterBody3D Actor { get; set; }
	/// <summary>
	/// Movement speed
	/// </summary>
	[Export] public float Speed { get; set; } = 3.0f;
	/// <summary>
	/// Horizontal acceleration rate in meters/second^2 when starting or changing direction.
	/// </summary>
	[Export(PropertyHint.Range, "10,200,1")] public float Acceleration { get; set; } = 50.0f;
	/// <summary>
	/// Horizontal deceleration rate in meters/second^2 when input ceases (braking friction).
	/// </summary>
	[Export(PropertyHint.Range, "10,300,1")] public float Deceleration { get; set; } = 90.0f;
	/// <summary>
	/// When false, velocity jumps directly to target speed (instantaneous step-function).
	/// </summary>
	[Export] public bool UseAcceleration { get; set; } = true;
	/// <summary>
	/// Rotation speed
	/// </summary>
	[Export] public float RotationSpeed { get; set; } = 20.0f;
	/// <summary>
	/// Hard cap on smooth turning in degrees per second, on top of <see cref="RotationSpeed"/> smoothing.
	/// Heavy actors use it so a player can outrun their turn and get behind them. Zero is unlimited.
	/// Explicit <see cref="SetLookAtDirection"/> snaps are not capped.
	/// </summary>
	[Export(PropertyHint.Range, "0,1440,1")] public float MaxTurnRate { get; set; }
	/// <summary>Runtime factor on all turning, smoothing and caps alike; a monster's health phase sets it.</summary>
	public float TurnRateScale { get; set; } = 1;
	/// <summary>
	/// The StairsTrigger node that will be used to detect stairs.
	/// </summary>
	[Export] public StairsTrigger StairsTrigger { get; set; }
	/// <summary>
	/// The gravity strength.
	/// </summary>
	[Export] public float Gravity { get; set; } = 9.8f;
	/// <summary>
	/// Maximum fall speed
	/// </summary>
	[Export] public float MaxFallSpeed { get; set; } = 50.0f;
	/// <summary>
	/// Riser height of the stairs, in units. A body pushing into a riser while on stairs rises this far in the time it
	/// would take to walk one riser height horizontally, twice over, so any body shape climbs the same steps briskly.
	/// </summary>
	[Export] public float StairsStepHeight { get; set; } = 0.5f;
	/// <summary>
	/// Grace window (in seconds) that an actor pressing into an obstacle can maintain locomotion state
	/// before being considered blocked and transitioning to idle. Prevents animation fluttering when
	/// grazing corners or sliding along walls.
	/// </summary>
	[Export(PropertyHint.Range, "0,0.5,0.01,suffix:s")] public float BlockedGraceDuration { get; set; } = 0.15f;

	// On stairs, contacts steeper than about 37° (normal.y below this) that face against the heading are a step's edge
	// or riser: rounded bodies meet a step edge at an angle, not head on, and a contact at exactly the 45° floor limit
	// would otherwise hold them in place.
	private const float RiserNormalY = .8f;
	private const float RiserFacing = -.3f;

	private float _blockedDuration = 0.15f;
	private bool _isMoving;

	public bool IsMoving => _isMoving;
	/// <summary>Collision-resolved horizontal speed from the latest <see cref="CharacterBody3D.MoveAndSlide"/> call.</summary>
	public float GroundSpeed { get; private set; }
	/// <summary>Movement source selected before the latest slide, for consumers that distinguish locomotion from actions.</summary>
	public MovementMotionKind LastMotionKind { get; private set; }
	public bool IsPushed => _pushStrength > 0.1f;
	/// <summary>Pushed by a knockback that no stagger overlapped; a stagger's pose already shows its own knockback.</summary>
	public bool IsKnockedBack => IsPushed && !_pushCoveredByStagger;
	public bool IsFalling => Velocity.Y < 0;

	public Vector3 Velocity { get; private set; } = Vector3.Zero;
	public Vector3 TargetDirection { get; private set; } = Vector3.Zero;
	public Vector3 LookAtDirection { get; private set; } = Vector3.Forward;

	private Vector3 _pushDirection = Vector3.Zero;
	private float _pushStrength = 0.0f;
	private bool _forcePushWhileLocked;
	private bool _pushCoveredByStagger;
	private Vector3 _lungeDirection;
	private float _lungeSpeed;
	private float _lungeRemaining;
	private AttackIdentity _lastLungeExecution;
	private bool _hasLungedExecution;

	private object _locomotionOverrideOwner;
	private Vector3 _overrideDirection = Vector3.Zero;
	private float _overrideSpeed = 0f;
	private bool _hasOverrideVelocity = false;
	private float _speedMultiplier = 1.0f;
	private readonly Dictionary<object, float> _statusSpeedFactors = [];
	private float _statusSpeedFactor = 1f;
	private Vector3? _fixedFacing;

	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public Vector3 LocomotionDirection => _hasOverrideVelocity
		? _overrideDirection
		: (TargetDirection != Vector3.Zero ? TargetDirection.Normalized() : Vector3.Zero);
	public float LocomotionSpeed => (_hasOverrideVelocity ? _overrideSpeed : Speed * _speedMultiplier) * _statusSpeedFactor;
	public float StatusSpeedFactor => _statusSpeedFactor;
	/// <summary>Apply a source-owned status multiplier independently of attack locomotion overrides.</summary>
	public void SetStatusSpeedFactor(object source, float factor)
	{
		if (source == null || !float.IsFinite(factor) || factor <= 0 || factor > 1) return;
		if (factor == 1) _statusSpeedFactors.Remove(source); else _statusSpeedFactors[source] = factor;
		_statusSpeedFactor = 1;
		foreach (float value in _statusSpeedFactors.Values) _statusSpeedFactor = Mathf.Min(_statusSpeedFactor, value);
	}
	public void HaltLocomotion() { _hasOverrideVelocity = false; CancelLunge(); Stop(); }
	public bool HasOverrideVelocity => _hasOverrideVelocity;
	public float SpeedMultiplier => _speedMultiplier;

	public void SetLocomotionOverride(object owner, Vector3 direction, float speed)
	{
		_locomotionOverrideOwner = owner;
		_overrideDirection = direction.Normalized();
		_overrideSpeed = speed;
		_hasOverrideVelocity = true;
	}

	/// <summary>Apply a short, collision-resolved forward movement once for a successful melee execution.</summary>
	public bool TryLunge(AttackIdentity execution, Vector3 direction, float distance, float duration)
	{
		if (execution == default || (_hasLungedExecution && execution == _lastLungeExecution)
			|| !GodotObject.IsInstanceValid(Actor) || !float.IsFinite(distance) || distance <= 0
			|| !float.IsFinite(duration) || duration <= 0 || direction.LengthSquared() < 0.0001f
			|| ActionGate?.Disabled == true || ActionGate?.IsStatusLocked == true || ActionGate?.IsStaggered == true
			|| _pushStrength > 0.1f)
			return false;

		direction.Y = 0;
		if (direction.LengthSquared() < 0.0001f) return false;
		_lastLungeExecution = execution;
		_hasLungedExecution = true;
		_lungeDirection = direction.Normalized();
		_lungeSpeed = distance / duration;
		_lungeRemaining = duration;
		return true;
	}

	private void CancelLunge()
	{
		_lungeDirection = Vector3.Zero;
		_lungeSpeed = 0;
		_lungeRemaining = 0;
	}

	public void SetSpeedMultiplier(object owner, float multiplier)
	{
		_locomotionOverrideOwner = owner;
		_speedMultiplier = multiplier;
	}

	public void SetFixedFacing(object owner, Vector3 facing)
	{
		_locomotionOverrideOwner = owner;
		_fixedFacing = facing.LengthSquared() > 0.0001f ? facing.Normalized() : null;
	}

	public void ClearLocomotionOverride(object owner)
	{
		if (_locomotionOverrideOwner == owner)
		{
			_locomotionOverrideOwner = null;
			_hasOverrideVelocity = false;
			_speedMultiplier = 1.0f;
			_fixedFacing = null;
			Velocity = new Vector3(0, Velocity.Y, 0);
			if (GodotObject.IsInstanceValid(Actor)) Actor.Velocity = Velocity;
		}
	}

	public void RequestFacing(Vector3 direction) => FaceDirection(direction);

	// A one-frame facing request that overrides movement-based facing for this physics frame.
	// Re-issue it every frame (e.g. while attacking) to keep the actor turned toward a target;
	// stop issuing it and facing falls back to the movement direction. Kept as a per-frame request
	// so it never lingers and fights normal locomotion.
	private Vector3 _requestedFacing = Vector3.Zero;
	private float _requestedTurnRate;
	private bool _hasRequestedFacing = false;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Movement;
		LookAtDirection = -Actor.GlobalTransform.Basis.Z;
	}

	public void Push(Vector3 direction, float strength)
	{
		SetPush(direction, strength, false);
	}

	public void PushForced(Vector3 direction, float strength)
	{
		SetPush(direction, strength, true);
		_forcePushWhileLocked = true;
	}

	private void SetPush(Vector3 direction, float strength, bool bypassStatusLock)
	{
		CancelLunge();
		_pushDirection = direction.Normalized();
		_pushStrength = strength;
		_pushCoveredByStagger = ActionGate?.IsStaggered == true;
		_forcePushWhileLocked = false;
		_hasOverrideVelocity = false;
		CombatTrace.Write(this, "effect.knockback", ("direction", CombatTrace.Vector(_pushDirection)),
			("strength", strength), ("bypass_status_lock", bypassStatusLock));
	}

	public void SetInputDirection(Vector3 inputDirection)
	{
		// Preserve analog stick deflection (clamped to max length 1.0)
		float len = inputDirection.Length();
		TargetDirection = len > 1.0f ? inputDirection.Normalized() : inputDirection;
		if (TargetDirection == Vector3.Zero)
		{
			SetStopped();
		}
		else if (Actor == null || !Actor.IsInsideTree())
		{
			_isMoving = true;
		}
	}

	public void SetLookAtDirection(Vector3 lookAtDirection)
	{
		LookAtDirection = lookAtDirection.Normalized();
	}

	/// <summary>
	/// Requests a smooth turn toward a world-space direction for this physics frame, independent of
	/// movement. Call it every physics frame while the facing should hold (e.g. during an attack
	/// wind-up) so a stationary actor still rotates to face its target; it takes priority over the
	/// movement direction for that frame and clears itself once it is no longer re-issued.
	/// </summary>
	/// <param name="maxDegreesPerSecond">Caps this frame's turn toward the request; zero keeps the ordinary smoothing.</param>
	public void FaceDirection(Vector3 worldDirection, float maxDegreesPerSecond = 0)
	{
		worldDirection.Y = 0;
		if (worldDirection.LengthSquared() < 0.0001f)
		{
			return;
		}

		_requestedFacing = worldDirection.Normalized();
		_requestedTurnRate = Mathf.Max(0, maxDegreesPerSecond);
		_hasRequestedFacing = true;
	}

	/// <summary>Discard scene-local motion and ownership before restoring a saved actor.</summary>
	public void ResetTransientMotion()
	{
		Stop();
		_pushDirection = Vector3.Zero;
		_pushStrength = 0;
		_forcePushWhileLocked = false;
		CancelLunge();
		_locomotionOverrideOwner = null;
		_hasOverrideVelocity = false;
		_speedMultiplier = 1;
		_fixedFacing = null;
		_hasRequestedFacing = false;
		if (GodotObject.IsInstanceValid(Actor)) Actor.Velocity = Vector3.Zero;
		GroundSpeed = 0;
		LastMotionKind = MovementMotionKind.None;
	}

	public void Stop()
	{
		TargetDirection = Vector3.Zero;
		Velocity = Vector3.Zero;
		SetStopped();
	}

	private void SetStopped()
	{
		_blockedDuration = BlockedGraceDuration;
		_isMoving = false;
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateVelocity(delta);
		if (ActionGate?.IsStatusLocked != true) SmoothRotateToward(delta);
		MoveAndSlideActor();
		_lungeRemaining = Mathf.Max(0, _lungeRemaining - (float)delta);
		if (_lungeRemaining == 0) _lungeSpeed = 0;
		UpdateIsMoving(delta);
	}

	private void UpdateIsMoving(double delta)
	{
		if (TargetDirection == Vector3.Zero)
		{
			SetStopped();
			return;
		}

		if (Actor == null || !Actor.IsInsideTree())
		{
			_isMoving = true;
			return;
		}

		if (GroundSpeed > 0.05f)
		{
			_blockedDuration = 0f;
			_isMoving = true;
		}
		else
		{
			_blockedDuration += (float)delta;
			_isMoving = _blockedDuration < BlockedGraceDuration;
		}
	}

	/// <summary>
	/// Move the character body
	/// </summary>
	public void MoveAndSlideActor()
	{
		if (LookAtDirection != Vector3.Zero)
		{
			Actor.LookAt(Actor.GlobalPosition + LookAtDirection, Vector3.Up);
		}
		Actor.Velocity = Velocity;
		Actor.MoveAndSlide();
		Vector3 realVelocity = Actor.GetRealVelocity();
		GroundSpeed = new Vector2(realVelocity.X, realVelocity.Z).Length();

		if (_hasOverrideVelocity && Actor.GetSlideCollisionCount() > 0
			&& GroundSpeed * GroundSpeed < 0.05f * _overrideSpeed * _overrideSpeed)
		{
			_hasOverrideVelocity = false;
		}
	}

	private void UpdateVelocity(double delta)
	{
		LastMotionKind = MovementMotionKind.None;
		Vector3 velocity = Vector3.Zero;

		// Apply horizontal velocity
		if (ActionGate?.Disabled == true || (ActionGate?.IsStatusLocked == true && !_forcePushWhileLocked))
		{
			velocity = Vector3.Zero;
			_pushStrength = 0;
			_forcePushWhileLocked = false;
		}
		else if (_pushStrength > 0.1f)
		{
			LastMotionKind = MovementMotionKind.Push;
			// Pushed by an external force; a poise break's knockback plays out during its stagger.
			velocity = _pushDirection * _pushStrength;
			if (ActionGate?.IsStaggered == true) _pushCoveredByStagger = true;
			_pushStrength -= Speed * (float)delta; // Decrease push strength over time
			if (_pushStrength <= 0.1f) _forcePushWhileLocked = false;
		}
		else if (ActionGate?.IsStatusLocked == true || ActionGate?.IsStaggered == true)
		{
			velocity = Vector3.Zero;
		}
		else if (_hasOverrideVelocity)
		{
			LastMotionKind = MovementMotionKind.Override;
			velocity = _overrideDirection * (_overrideSpeed * _statusSpeedFactor);
		}
		else if (ActionGate?.AllowsLocomotion == false)
		{
			velocity = Vector3.Zero;
		}
		else
		{
			LastMotionKind = MovementMotionKind.Locomotion;
			Vector3 desiredHorizontal = TargetDirection * (Speed * _speedMultiplier * _statusSpeedFactor);

			if (!UseAcceleration)
			{
				velocity = desiredHorizontal;
			}
			else
			{
				velocity.X = ApproachHorizontal(Velocity.X, desiredHorizontal.X, (float)delta);
				velocity.Z = ApproachHorizontal(Velocity.Z, desiredHorizontal.Z, (float)delta);
			}
		}

			// Lunge is attack movement, so it can carry an actor while ordinary locomotion is locked
			// by its attack action. Status locks, stagger, death, and knockback still take priority.
			if (_lungeRemaining > 0 && _pushStrength <= 0.1f && ActionGate?.Disabled != true
				&& ActionGate?.IsStatusLocked != true && ActionGate?.IsStaggered != true && delta > 0)
			{
				LastMotionKind = MovementMotionKind.Lunge;
				float frameDuration = Mathf.Min((float)delta, _lungeRemaining);
				velocity += _lungeDirection * (_lungeSpeed * frameDuration / (float)delta);
			}

		// Apply vertical velocity
		velocity.Y += Velocity.Y;
		var isOnStairs = StairsTrigger?.stairs > 0;
		if (Gravity <= 0 || Actor.IsOnFloor())
		{
			// Reset vertical velocity when on the floor or when gravity is disabled
			velocity.Y = 0;
		}
		else
		{
			// Apply gravity if not on the floor
			velocity.Y -= Gravity * Gravity * (float)delta; // Gravity applied over time
			velocity.Y = Mathf.Clamp(velocity.Y, -MaxFallSpeed, MaxFallSpeed);
			if (!isOnStairs)
			{
				// Stop horizontal movement when falling
				velocity.X = velocity.X * 0.5f;
				velocity.Z = velocity.Z * 0.5f;
			}
		}

		// Pushing into a step riser on stairs lifts the body over it, on the floor or mid-step alike.
		if (isOnStairs && IsPressingIntoRiser(velocity)) velocity.Y = Mathf.Max(velocity.Y, StairsStepHeight * Speed * 2);

		Velocity = velocity;
	}

	/// <summary>
	/// Whether the last slide pressed the body against something too steep to stand on that it is still heading into,
	/// with open space one step up: a step, not a railing or a wall.
	/// </summary>
	private bool IsPressingIntoRiser(Vector3 velocity)
	{
		if (!velocity.TryNormalizeHorizontal(out var heading)) return false;
		bool pressing = false;
		int count = Actor.GetSlideCollisionCount();
		for (int index = 0; index < count && !pressing; index++)
		{
			var normal = Actor.GetSlideCollision(index).GetNormal();
			if (normal.Y >= RiserNormalY || normal.Y < 0) continue;
			pressing = normal.TryNormalizeHorizontal(out var facing) && facing.Dot(heading) < RiserFacing;
		}
		if (!pressing) return false;
		// Test from just off the riser: a body resting against it would report contact for any motion.
		var lift = Vector3.Up * (StairsStepHeight + .05f);
		var clear = Actor.GlobalTransform.Translated(-heading * .05f);
		return !Actor.TestMove(clear, lift) && !Actor.TestMove(clear.Translated(lift), heading * .3f);
	}

	private float ApproachHorizontal(float current, float target, float delta)
	{
		if (target == 0)
		{
			return Mathf.MoveToward(current, 0, Deceleration * delta);
		}
		// If reversing direction on this axis, brake with Deceleration first
		float rate = (current != 0 && Mathf.Sign(current) != Mathf.Sign(target)) ? Deceleration : Acceleration;
		return Mathf.MoveToward(current, target, rate * delta);
	}

	private void SmoothRotateToward(double delta)
	{
		// An explicit facing request (e.g. attack aim-assist) wins over the movement direction for
		// this frame; otherwise face where we are moving. Consume the request either way so it does
		// not linger past the frame it was issued.
		Vector3 lookAt;
		float turnRate = MaxTurnRate;
		if (_fixedFacing.HasValue)
		{
			lookAt = _fixedFacing.Value;
		}
		else if (_hasRequestedFacing)
		{
			lookAt = _requestedFacing;
			if (_requestedTurnRate > 0) turnRate = turnRate > 0 ? Mathf.Min(turnRate, _requestedTurnRate) : _requestedTurnRate;
		}
		else if (TargetDirection != Vector3.Zero)
		{
			lookAt = new Vector3(TargetDirection.X, 0, TargetDirection.Z).Normalized();
		}
		else
		{
			lookAt = Vector3.Zero;
		}
		_hasRequestedFacing = false;

		if (lookAt == Vector3.Zero)
		{
			return;
		}

		// Rotate by signed horizontal yaw. Perturbing an opposite direction before
		// Slerp can cancel a leftward target to zero and leave the actor facing away forever.
		Vector3 current = LookAtDirection;
		current.Y = 0;
		current = current.LengthSquared() > .0001f ? current.Normalized() : lookAt;
		float yaw = Mathf.Atan2(current.Cross(lookAt).Y, current.Dot(lookAt));
		float fraction = Mathf.Clamp(RotationSpeed * TurnRateScale * (float)delta, 0, 1);
		float step = yaw * fraction;
		// A capped request (an AI wind-up tracking its target) turns no faster than its authored rate.
		float turnCap = Mathf.DegToRad(turnRate * TurnRateScale) * (float)delta;
		if (turnCap > 0) step = Mathf.Clamp(step, -turnCap, turnCap);
		LookAtDirection = current.Rotated(Vector3.Up, step).Normalized();
	}
}
