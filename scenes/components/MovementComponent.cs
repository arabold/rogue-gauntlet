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
	/// <summary>
	/// Hover altitude above the floor for flying actors; zero (default) keeps normal gravity.
	/// Hovering bodies spring toward floor height + HoverHeight instead of falling. They still
	/// collide with walls and path on the ground navmesh, so dungeon flyers cannot cross walls
	/// or gain real altitude; disabling hover (e.g. on death) lets the body drop naturally.
	/// </summary>
	[Export] public float HoverHeight { get; set; } = 0f;

	public bool IsMoving => TargetDirection != Vector3.Zero;
	public bool IsPushed => _pushStrength > 0.1f;
	public bool IsHovering => HoverHeight > 0f;
	public bool IsFalling => !IsHovering && Velocity.Y < 0;

	public Vector3 Velocity { get; private set; } = Vector3.Zero;
	public Vector3 TargetDirection { get; private set; } = Vector3.Zero;
	public Vector3 LookAtDirection { get; private set; } = Vector3.Forward;

	private Vector3 _pushDirection = Vector3.Zero;
	private float _pushStrength = 0.0f;

	public override void _Ready()
	{
		LookAtDirection = -Actor.GlobalTransform.Basis.Z;
	}

	public void Push(Vector3 direction, float strength)
	{
		_pushDirection = direction.Normalized();
		_pushStrength = strength;
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
	}

	private void UpdateVelocity(double delta)
	{
		Vector3 velocity;

		// Apply horizontal velocity
		if (_pushStrength > 0.1f)
		{
			// Pushed by an external force
			velocity = _pushDirection * _pushStrength;
			_pushStrength -= Speed * (float)delta; // Decrease push strength over time
		}
		else if (TargetDirection != Vector3.Zero)
		{
			// General movement
			velocity = TargetDirection * Speed;
		}
		else
		{
			// Stop moving if no input
			velocity = Vector3.Zero;
		}

		// Apply vertical velocity
		if (IsHovering)
		{
			velocity.Y = ComputeHoverLift();
			Velocity = velocity;
			return;
		}

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

	/// <summary>
	/// Vertical speed that springs a hovering body toward floor height + <see cref="HoverHeight"/>.
	/// The floor is found with a short downward ray against the world layer, so hover altitude
	/// follows the dungeon floor and never climbs terrain that is not there.
	/// </summary>
	private float ComputeHoverLift()
	{
		Vector3 from = Actor.GlobalPosition + Vector3.Up * 0.1f;
		Vector3 to = from + Vector3.Down * (HoverHeight + 4.0f);
		var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask: 1,
			exclude: [Actor.GetRid()]);
		var hit = Actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			// No floor below (e.g. over a pit): sink gently until one appears.
			return -1.0f;
		}

		float targetY = ((Vector3)hit["position"]).Y + HoverHeight;
		float error = targetY - Actor.GlobalPosition.Y;
		// Spring toward the target altitude, clamped so take-off and settling stay gentle.
		return Mathf.Clamp(error * 4.0f, -3.0f, 3.0f);
	}

	private void SmoothRotateToward(double delta)
	{
		if (TargetDirection != Vector3.Zero)
		{
			var lookAt = new Vector3(TargetDirection.X, 0, TargetDirection.Z).Normalized();
			if (lookAt.IsEqualApprox(LookAtDirection))
			{
				LookAtDirection = lookAt;
			}
			else
			{
				// Handle the case where lookAt is exactly opposite to LookAtDirection
				if (lookAt.Dot(LookAtDirection) < -0.999f)
				{
					lookAt += new Vector3(0.001f, 0, 0).Normalized();
				}
				LookAtDirection = LookAtDirection.Slerp(
					lookAt, RotationSpeed * (float)delta
				).Normalized();
			}
		}
	}
}
