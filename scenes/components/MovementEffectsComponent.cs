using Godot;

/// <summary>Turns resolved ground movement into small actor-local dust and dash cues.</summary>
[GlobalClass]
public partial class MovementEffectsComponent : Node3D, IPooledNode
{
	[Export] public MovementComponent Movement { get; set; }
	[Export] public WorldEffectHost Effects { get; set; }
	[Export] public MovementEffectProfile Profile { get; set; }
	[Export] public GpuParticles3D Dust { get; set; }
	[Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 513;
	[Export] public DodgeComponent Dodge { get; set; }
	[Export] public RibbonTrail Streak { get; set; }
	[Export] public VisibleOnScreenNotifier3D ScreenVisibility { get; set; }
	private bool _enabled = true;
	[Export]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if (_enabled == value) return;
			_enabled = value;
			if (IsNodeReady()) ResetFeedback();
		}
	}

	private readonly RandomNumberGenerator _random = new();
	private Cooldown _stopCooldown;
	private ExplorationVisibility _exploration;
	private CharacterBody3D Actor => Movement?.Actor;
	private ActorActionGateComponent ActionGate => Movement?.ActionGate;
	private Vector3 _previousPosition;
	private Vector3 _lastTravelDirection;
	private Vector3 _dashOrigin;
	private Vector3 _dashDirection;
	private float _distanceSinceStep;
	private float _previousSpeed;
	private bool _wasRunning;
	private bool _dashPending;
	private bool _dashTravelled;
	private bool _dashEnded;
	private bool _suppressed;

	public override void _EnterTree() => _exploration = new ExplorationVisibility(this);

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (!HasValidSetup())
		{
			SetPhysicsProcess(false);
			return;
		}

		ProcessPhysicsPriority = CombatPhysics.MovementPresentation;
		var level = this.GetAncestorOrNull<Level>();
		string identity = Actor.HasMeta("persistent_id") ? Actor.GetMeta("persistent_id").AsString() : Actor.GetPath().ToString();
		_random.Seed = GenerationRandom.DeriveSeed(level?.Seed ?? RunState.DefaultSeed, "vfx.movement", identity);
		if (Dust != null)
		{
			VisualHelper.Mark(Dust);
			ConfigureDust();
		}
		if (Streak != null)
		{
			VisualHelper.Mark(Streak);
			Streak.MaxPoints = 16;
			ConfigureStreak(0, 0);
			Streak.End();
		}
		if (ScreenVisibility != null) VisualHelper.Mark(ScreenVisibility);
		if (Dodge != null)
		{
			if (Dodge.Actor != Actor || Dodge.MovementComponent != Movement)
			{
				GD.PushWarning($"{Name}: dash effects require the actor's own DodgeComponent.");
				Dodge = null;
			}
			else this.SubscribeUntilExit(Dodge,
				dodge => { dodge.DodgeStarted += OnDodgeStarted; dodge.DodgeEnded += OnDodgeEnded; },
				dodge => { dodge.DodgeStarted -= OnDodgeStarted; dodge.DodgeEnded -= OnDodgeEnded; });
		}
		this.SubscribeUntilExit(Effects, host => host.GateChanged += OnGateChanged, host => host.GateChanged -= OnGateChanged);
		_previousPosition = Actor.GlobalPosition;
		ApplySuppression();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Enabled || !IsValidRuntime()) return;
		if (!_stopCooldown.IsReady) _stopCooldown.Tick(delta);
		Vector3 position = Actor.GlobalPosition;
		Vector3 startPosition = position - Actor.GetPositionDelta();
		if (startPosition.DistanceTo(_previousPosition) > 0.05f)
		{
			ResetFeedback();
			return;
		}

		Vector3 displacement = position - _previousPosition;
		displacement.Y = 0;
		float speed = Movement.GroundSpeed;
		Vector3 direction = displacement.LengthSquared() > 0.0001f ? displacement.Normalized() : Vector3.Zero;
		bool onGround = Actor.IsOnFloor();
		bool visible = CanPresent();
		if (visible && onGround) ProcessDodge(displacement, direction);
		else if (!visible || !onGround) CancelDash(clearTrail: false);

		bool canRun = visible && onGround && !ActionGate.Disabled && !ActionGate.IsStatusLocked
			&& !ActionGate.IsStaggered && Movement.LastMotionKind == MovementMotionKind.Locomotion && !Movement.IsFalling;
		if (canRun) ProcessRunning(displacement, direction, speed);
		else ResetRunningState();

		_previousSpeed = speed;
		_previousPosition = position;
	}

	private bool HasValidSetup()
	{
		bool referencesValid = Movement != null && Actor != null && ActionGate != null && Effects != null && Profile != null;
		bool profileValid = Profile != null && Profile.SizeScale > 0 && Profile.DustLifetime > 0 && Profile.ParticleCapacity > 0
			&& Profile.RunEnterSpeed > Profile.RunExitSpeed && Profile.StepDistance > 0;
		if (!referencesValid || !profileValid)
			GD.PushWarning($"{Name}: movement effects require a valid Movement, WorldEffects and MovementEffectProfile setup.");
		return referencesValid && profileValid;
	}

	private bool IsValidRuntime() => GodotObject.IsInstanceValid(Actor) && GodotObject.IsInstanceValid(Movement)
		&& GodotObject.IsInstanceValid(ActionGate) && GodotObject.IsInstanceValid(Effects)
		&& GodotObject.IsInstanceValid(Profile) && !Effects.IsStopped;

	private bool CanPresent() => !Effects.IsSuppressed && Actor.IsVisibleInTree()
		&& (ScreenVisibility == null || !GodotObject.IsInstanceValid(ScreenVisibility) || ScreenVisibility.IsOnScreen())
		&& _exploration.IsRevealed(Actor.GlobalPosition);

	private void ProcessRunning(Vector3 displacement, Vector3 direction, float speed)
	{
		bool moving = speed >= Profile.RunEnterSpeed && displacement.LengthSquared() > 0.0001f;
		if (moving)
		{
			_wasRunning = true;
			if (direction != Vector3.Zero) _lastTravelDirection = direction;
			_distanceSinceStep += displacement.Length();
			float stepDistance = Profile.StepDistance * Profile.SizeScale;
			int contacts = 0;
			while (_distanceSinceStep >= stepDistance && contacts < 2)
			{
				EmitDust(Profile.RunPuffCount, Actor.GlobalPosition, -direction);
				_distanceSinceStep -= stepDistance;
				contacts++;
			}
			if (contacts == 2) _distanceSinceStep = 0;
			return;
		}

		if (_wasRunning && speed <= Profile.RunExitSpeed)
		{
			if (_previousSpeed >= Profile.StopMinimumSpeed && !Movement.IsMoving && _stopCooldown.IsReady
				&& _lastTravelDirection != Vector3.Zero)
			{
				EmitDust(Profile.StopPuffCount, Actor.GlobalPosition, _lastTravelDirection);
				_stopCooldown.Start(Profile.StopCooldown);
			}
			ResetRunningState();
		}
		else if (Movement.LastMotionKind is MovementMotionKind.Push or MovementMotionKind.Lunge or MovementMotionKind.Override)
		{
			ResetRunningState();
		}
	}

	private void ProcessDodge(Vector3 displacement, Vector3 direction)
	{
		bool hasTravel = displacement.LengthSquared() > 0.0001f;
		if (_dashPending && !_dashTravelled && hasTravel && DodgeTravelIsActive())
		{
			if (direction != Vector3.Zero) _dashDirection = direction;
			EmitDust(Profile.DashPuffCount, _dashOrigin, -_dashDirection);
			_dashTravelled = true;
			Streak?.Begin();
			Streak?.Sample(_dashOrigin);
			_dashPending = false;
		}

		if (DodgeTravelIsActive() && hasTravel)
		{
			_dashTravelled = true;
			if (direction != Vector3.Zero)
			{
				_dashDirection = direction;
				Streak?.Sample(Actor.GlobalPosition);
			}
			return;
		}

		if (_dashTravelled && (_dashEnded || !DodgeTravelIsActive() || !hasTravel))
		{
			if (CanFinishDash()) EmitDust(Profile.RunPuffCount, Actor.GlobalPosition, -_dashDirection);
			Streak?.End();
			ResetDashState();
		}
		else if (_dashEnded && !_dashTravelled)
		{
			Streak?.End();
			ResetDashState();
		}
	}

	private bool DodgeTravelIsActive() => Dodge?.IsDodging == true
		&& Movement.LastMotionKind == MovementMotionKind.Override;

	private bool CanFinishDash() => !ActionGate.Disabled && !ActionGate.IsStatusLocked && !ActionGate.IsStaggered
		&& Movement.LastMotionKind != MovementMotionKind.Push;

	private void OnDodgeStarted(Vector3 direction)
	{
		ConfigureStreak(Dodge?.DodgeDef?.Distance ?? 0, Dodge?.DodgeDef?.TravelDuration ?? 0);
		_dashPending = true;
		_dashTravelled = false;
		_dashEnded = false;
		_dashOrigin = Actor.GlobalPosition;
		_dashDirection = direction.Normalized();
		if (_dashDirection.IsZeroApprox()) _dashDirection = Movement.LookAtDirection;
		ResetRunningState();
	}

	/// <summary>Matches the ribbon's range and fade time to the authored dodge while preserving its size-based profile.</summary>
	private void ConfigureStreak(float dashDistance, float travelDuration)
	{
		if (Streak == null || !GodotObject.IsInstanceValid(Streak) || Profile == null) return;
		Streak.MaxLength = Mathf.Max(Profile.StreakLength * Profile.SizeScale, dashDistance);
		Streak.PointLifetime = Mathf.Max(Profile.StreakLifetime, travelDuration + Profile.StreakLifetime);
		Streak.Width = Profile.StreakWidth * Profile.SizeScale;
		Streak.PointSpacing = Mathf.Max(0.06f, Streak.MaxLength / (Streak.MaxPoints - 1f));
	}

	private void OnDodgeEnded()
	{
		if (_dashPending) _dashEnded = true;
	}

	private void EmitDust(int count, Vector3 around, Vector3 direction)
	{
		if (Dust == null || count <= 0 || !GodotObject.IsInstanceValid(Dust)
			|| !HasGround(around, out Vector3 ground, out Vector3 normal)) return;

		Vector3 tangent = direction;
		tangent.Y = 0;
		if (tangent.LengthSquared() < 0.0001f) tangent = Vector3.Forward;
		tangent = tangent.Normalized();
		Vector3 side = tangent.Cross(normal).Normalized();
		int emitCount = Mathf.Min(count, Profile.ParticleCapacity);
		for (int i = 0; i < emitCount; i++)
		{
			Vector3 offset = side * _random.RandfRange(-0.16f, 0.16f) * Profile.SizeScale;
			Vector3 position = ground + normal * 0.025f + offset;
			float scale = Profile.PuffDiameter * Profile.SizeScale * _random.RandfRange(0.65f, 1.2f);
			Vector3 up = normal.Cross(side).Normalized();
			Basis basis = new Basis(side, up, normal).Rotated(normal, _random.RandfRange(0f, Mathf.Tau));
			basis = basis.Scaled(Vector3.One * scale);
			Vector3 velocity = normal * _random.RandfRange(0.15f, 0.55f)
				- tangent * _random.RandfRange(0.1f, 0.5f);
			Dust.EmitParticle(new Transform3D(basis, position), velocity, Profile.DustColor, Colors.White,
				(uint)(GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.RotationScale
					| GpuParticles3D.EmitFlags.Velocity | GpuParticles3D.EmitFlags.Color));
		}
	}

	private bool HasGround(Vector3 around, out Vector3 position, out Vector3 normal)
	{
		position = around;
		normal = Vector3.Up;
		if (!Actor.IsInsideTree()) return false;
		Vector3 anchor = GodotObject.IsInstanceValid(Effects.GroundAnchor) ? Effects.GroundAnchor.GlobalPosition : Actor.GlobalPosition;
		Vector3 from = new(around.X, anchor.Y + 0.3f, around.Z);
		Vector3 to = from - Vector3.Up * (0.5f + Profile.SizeScale * 0.1f);
		var query = PhysicsRayQueryParameters3D.Create(from, to, GroundMask,
			new Godot.Collections.Array<Rid> { Actor.GetRid() });
		var hit = Actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0 || !hit.TryGetValue("position", out var hitPosition)
			|| !hit.TryGetValue("normal", out var hitNormal)) return false;
		position = hitPosition.AsVector3();
		normal = hitNormal.AsVector3();
		return normal.Dot(Vector3.Up) >= 0.65f;
	}

	private void ConfigureDust()
	{
		Dust.Amount = Profile.ParticleCapacity;
		Dust.Lifetime = Profile.DustLifetime;
		Dust.OneShot = false;
		Dust.LocalCoords = false;
		Dust.Emitting = false;
		Dust.UseFixedSeed = true;
		Dust.Seed = _random.Randi();
	}

	private void OnGateChanged() => ApplySuppression();

	private void ApplySuppression()
	{
		bool suppressed = !Enabled || (Effects != null && Effects.IsSuppressed)
			|| (Actor != null && !Actor.IsVisibleInTree());
		if (_suppressed == suppressed) return;
		_suppressed = suppressed;
		if (suppressed) ResetFeedback();
	}

	private void ResetRunningState()
	{
		_wasRunning = false;
		_distanceSinceStep = 0;
		_lastTravelDirection = Vector3.Zero;
	}

	private void ResetDashState()
	{
		_dashPending = false;
		_dashTravelled = false;
		_dashEnded = false;
	}

	private void CancelDash(bool clearTrail)
	{
		ResetDashState();
		if (clearTrail) Streak?.Clear(); else Streak?.End();
	}

	/// <summary>Clears pending cues and visual tails after a reset, relocation or pool transition.</summary>
	public void ResetFeedback()
	{
		ResetRunningState();
		CancelDash(clearTrail: true);
		_stopCooldown.Start(0);
		_previousSpeed = 0;
		if (GodotObject.IsInstanceValid(Dust))
		{
			Dust.Restart();
			Dust.Emitting = false;
		}
		if (GodotObject.IsInstanceValid(Actor)) _previousPosition = Actor.GlobalPosition;
	}

	public override void _ExitTree() => ResetFeedback();
	public void OnSpawnedFromPool() => ResetFeedback();
	public void OnDespawnedToPool() => ResetFeedback();
}