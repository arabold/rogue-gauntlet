using Godot;

/// <summary>Turns accepted actor actions and resolved movement into positional cues, independently of visual effects.</summary>
[GlobalClass]
public partial class ActorAudioComponent : Node
{
	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public AttackController Attack { get; set; }
	[Export] public DodgeComponent Dodge { get; set; }
	[Export] public DashComponent Dash { get; set; }
	[Export] public GuardComponent Guard { get; set; }
	[Export] public SoundCueProfile FootstepCue { get; set; }
	[Export] public SoundCueProfile RushCue { get; set; }
	[Export] public SoundCueProfile GuardCue { get; set; }
	[Export(PropertyHint.Range, "0.1,4,0.05")] public float StepDistance { get; set; } = 2.5f;
	[Export(PropertyHint.Range, "0,12,0.1")] public float StepSpeed { get; set; } = 2.5f;

	private Vector3 _previousPosition;
	private float _distanceSinceStep;
	private bool _stepsEnabled;
	private bool _rushPending;
	private SoundCueProfile _pendingDashCue;
	private bool _wasGuarding;
	private Level _level;

	public override void _EnterTree()
	{
		_level = this.GetAncestorOrNull<Level>();
		if (IsInstanceValid(Actor)) _previousPosition = Actor.GlobalPosition;
		_wasGuarding = IsInstanceValid(Guard) && Guard.IsGuarding;
	}

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Commit;
		if (!IsInstanceValid(Actor))
		{
			GD.PushError("ActorAudioComponent requires an Actor.");
			SetPhysicsProcess(false);
			return;
		}

		_stepsEnabled = Movement != null && FootstepCue != null && float.IsFinite(StepDistance) && StepDistance > 0
			&& float.IsFinite(StepSpeed) && StepSpeed >= 0;
		if (Movement != null && FootstepCue != null && !_stepsEnabled)
			GD.PushWarning("ActorAudioComponent disabled footsteps: assign a valid step distance and speed.");
		SetPhysicsProcess((Movement != null && (_stepsEnabled || (Dodge != null && RushCue != null) || Dash != null))
			|| (Guard != null && GuardCue != null));
		if (Attack != null) this.SubscribeUntilExit(Attack,
			attack => { attack.AttackStarted += OnAttackStarted; attack.AttackReleased += OnAttackReleased; },
			attack => { attack.AttackStarted -= OnAttackStarted; attack.AttackReleased -= OnAttackReleased; });
		if (Dodge != null) this.SubscribeUntilExit(Dodge,
			dodge => { dodge.DodgeStarted += OnDodgeStarted; dodge.DodgeEnded += OnDodgeEnded; },
			dodge => { dodge.DodgeStarted -= OnDodgeStarted; dodge.DodgeEnded -= OnDodgeEnded; });
		if (Dash != null) this.SubscribeUntilExit(Dash,
			dash => { dash.DashStarted += OnDashStarted; dash.DashEnded += OnDashEnded; },
			dash => { dash.DashStarted -= OnDashStarted; dash.DashEnded -= OnDashEnded; });
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsInstanceValid(Actor)) return;
		Vector3 position = Actor.GlobalPosition;
		if (IsInstanceValid(Movement)) SampleTravel(position);
		bool guarding = IsInstanceValid(Guard) && Guard.IsGuarding;
		if (guarding && !_wasGuarding) EmitCue(GuardCue);
		_wasGuarding = guarding;
		_previousPosition = position;
	}

	/// <summary>Only resolved locomotion earns steps; teleports and blocked movement cannot sound like travel.</summary>
	private void SampleTravel(Vector3 position)
	{
		Vector3 startPosition = position - Actor.GetPositionDelta();
		if (startPosition.DistanceTo(_previousPosition) > 0.05f)
		{
			ClearTravel();
			return;
		}

		Vector3 displacement = position - _previousPosition;
		displacement.Y = 0;
		bool moved = displacement.LengthSquared() > 0.0001f;
		bool locomoting = _stepsEnabled && moved && Actor.IsOnFloor() && !Movement.IsFalling
			&& Movement.LastMotionKind == MovementMotionKind.Locomotion && Movement.GroundSpeed >= StepSpeed
			&& ActionGate?.Disabled != true && ActionGate?.IsStatusLocked != true && ActionGate?.IsStaggered != true;
		if (locomoting)
		{
			_distanceSinceStep += displacement.Length();
			if (_distanceSinceStep >= StepDistance)
			{
				EmitCue((IsInstanceValid(_level) ? _level.Floor?.FootstepCueAt(position) : null) ?? FootstepCue);
				_distanceSinceStep %= StepDistance;
			}
		}
		else if (Movement.LastMotionKind != MovementMotionKind.Locomotion) _distanceSinceStep = 0;

		if (!moved || Movement.LastMotionKind != MovementMotionKind.Override) return;
		if (_rushPending && IsInstanceValid(Dodge) && Dodge.IsDodging)
		{
			EmitCue(RushCue);
			_rushPending = false;
		}
		if (_pendingDashCue != null && IsInstanceValid(Dash) && Dash.IsDashing)
		{
			EmitCue(_pendingDashCue);
			_pendingDashCue = null;
		}
	}

	public override void _ExitTree() => ClearTravel();

	private void OnAttackStarted(AttackDefinition definition, SoundCueProfile voiceCue)
	{
		EmitCue(definition?.WindUpCue);
		EmitCue(voiceCue);
	}
	private void OnAttackReleased(AttackDefinition definition) => EmitCue(definition?.ReleaseCue);
	private void OnDodgeStarted(Vector3 direction) => _rushPending = true;
	private void OnDodgeEnded() => _rushPending = false;
	private void OnDashStarted(DashProfile profile) => _pendingDashCue = profile?.TravelCue;
	private void OnDashEnded() => _pendingDashCue = null;

	private void EmitCue(SoundCueProfile cue)
	{
		if (cue != null && IsInstanceValid(Actor) && Actor.IsInsideTree())
			SignalBus.EmitWorldSoundEmitted(cue, Actor, Actor.GlobalPosition);
	}

	private void ClearTravel()
	{
		_distanceSinceStep = 0;
		_rushPending = false;
		_pendingDashCue = null;
	}
}