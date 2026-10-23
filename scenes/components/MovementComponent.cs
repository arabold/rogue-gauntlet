using System;
using Godot;

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
	/// Rotation speed
	/// </summary>
	[Export] public float RotationSpeed { get; set; } = 20.0f;
	/// <summary>
	/// The StairsTrigger node that will be used to detect stairs.
	/// </summary>
	[Export] public StairsTrigger StairsTrigger;
	/// <summary>
	/// The gravity strength.
	/// </summary>
	[Export] public float Gravity = 9.8f;
	/// <summary>
	/// Maximum fall speed
	/// </summary>
	[Export] public float MaxFallSpeed = 50.0f;
	/// <summary>
	/// Stairs step height
	/// </summary>
	[Export] public float StairsStepHeight = 0.5f;

	public bool IsMoving => TargetDirection != Vector3.Zero;
	public bool IsPushed => _pushStrength > 0.1f;
	public bool IsFalling => Velocity.Y < 0;

	public Vector3 Velocity { get; private set; } = Vector3.Zero;
	public Vector3 TargetDirection { get; private set; } = Vector3.Zero;
	public Vector3 LookAtDirection { get; private set; } = Vector3.Forward;

	private Vector3 _pushDirection = Vector3.Zero;
	private float _pushStrength = 0.0f;

	private object _locomotionOverrideOwner;
	private Vector3 _overrideDirection = Vector3.Zero;
	private float _overrideSpeed = 0f;
	private bool _hasOverrideVelocity = false;
	private float _speedMultiplier = 1.0f;
	private Vector3? _fixedFacing;

	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public Vector3 LocomotionDirection => _hasOverrideVelocity ? _overrideDirection : TargetDirection;
	public float LocomotionSpeed => _hasOverrideVelocity ? _overrideSpeed : Speed * _speedMultiplier;
	public void HaltLocomotion() { _hasOverrideVelocity = false; Stop(); }
	public bool HasOverrideVelocity => _hasOverrideVelocity;
	public float SpeedMultiplier => _speedMultiplier;

	public void SetLocomotionOverride(object owner, Vector3 direction, float speed)
	{
		_locomotionOverrideOwner = owner;
		_overrideDirection = direction.Normalized();
		_overrideSpeed = speed;
		_hasOverrideVelocity = true;
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
		}
	}

	public void RequestFacing(Vector3 direction) => FaceDirection(direction);

	// A one-frame facing request that overrides movement-based facing for this physics frame.
	// Re-issue it every frame (e.g. while attacking) to keep the actor turned toward a target;
	// stop issuing it and facing falls back to the movement direction. Kept as a per-frame request
	// so it never lingers and fights normal locomotion.
	private Vector3 _requestedFacing = Vector3.Zero;
	private bool _hasRequestedFacing = false;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Movement;
		LookAtDirection = -Actor.GlobalTransform.Basis.Z;
	}

	public void Push(Vector3 direction, float strength)
	{
		_pushDirection = direction.Normalized();
		_pushStrength = strength;
		_hasOverrideVelocity = false;
	}

	public void SetInputDirection(Vector3 inputDirection)
	{
		// Set target direction based on input direction
		TargetDirection = inputDirection.Normalized();
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
	public void FaceDirection(Vector3 worldDirection)
	{
		worldDirection.Y = 0;
		if (worldDirection.LengthSquared() < 0.0001f)
		{
			return;
		}

		_requestedFacing = worldDirection.Normalized();
		_hasRequestedFacing = true;
	}

	/// <summary>Discard scene-local motion and ownership before restoring a saved actor.</summary>
	public void ResetTransientMotion()
	{
		Stop();
		_pushDirection = Vector3.Zero;
		_pushStrength = 0;
		_locomotionOverrideOwner = null;
		_hasOverrideVelocity = false;
		_speedMultiplier = 1;
		_fixedFacing = null;
		_hasRequestedFacing = false;
		if (GodotObject.IsInstanceValid(Actor)) Actor.Velocity = Vector3.Zero;
	}

	public void Stop()
	{
		TargetDirection = Vector3.Zero;
		Velocity = Vector3.Zero;
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateVelocity(delta);
		SmoothRotateToward(delta);
		MoveAndSlideActor();
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

		if (_hasOverrideVelocity && Actor != null && Actor.GetSlideCollisionCount() > 0)
		{
			Vector3 realHoriz = Actor.GetRealVelocity();
			realHoriz.Y = 0;
			if (realHoriz.LengthSquared() < 0.05f * _overrideSpeed * _overrideSpeed)
			{
				_hasOverrideVelocity = false;
			}
		}
	}

	private void UpdateVelocity(double delta)
	{
		Vector3 velocity;

		// Apply horizontal velocity
		if (ActionGate?.Disabled == true || ActionGate?.IsStaggered == true)
		{
			velocity = Vector3.Zero;
			_pushStrength = 0;
		}
		else if (_pushStrength > 0.1f)
		{
			// Pushed by an external force
			velocity = _pushDirection * _pushStrength;
			_pushStrength -= Speed * (float)delta; // Decrease push strength over time
		}
		else if (_hasOverrideVelocity)
		{
			velocity = _overrideDirection * _overrideSpeed;
		}
		else if (TargetDirection != Vector3.Zero && ActionGate?.AllowsLocomotion != false)
		{
			// General movement
			velocity = TargetDirection * (Speed * _speedMultiplier);
		}
		else
		{
			// Stop moving if no input
			velocity = Vector3.Zero;
		}

		// Apply vertical velocity
		velocity.Y += Velocity.Y;
		var isOnStairs = StairsTrigger?.stairs > 0;
		if (Actor.IsOnFloor())
		{
			// Reset vertical velocity when on the floor
			velocity.Y = 0;

			// Check if we're on stairs
			if (isOnStairs && (velocity.X != 0 || velocity.Z != 0))
			{
				velocity.Y = StairsStepHeight * Speed;
			}
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

		Velocity = velocity;
	}

	private void SmoothRotateToward(double delta)
	{
		// An explicit facing request (e.g. attack aim-assist) wins over the movement direction for
		// this frame; otherwise face where we are moving. Consume the request either way so it does
		// not linger past the frame it was issued.
		Vector3 lookAt;
		if (_fixedFacing.HasValue)
		{
			lookAt = _fixedFacing.Value;
		}
		else if (_hasRequestedFacing)
		{
			lookAt = _requestedFacing;
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
		float fraction = Mathf.Clamp(RotationSpeed * (float)delta, 0, 1);
		LookAtDirection = current.Rotated(Vector3.Up, yaw * fraction).Normalized();
	}
}