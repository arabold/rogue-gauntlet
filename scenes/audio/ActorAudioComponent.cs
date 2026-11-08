using Godot;

/// <summary>Turns actor movement, attack wind-ups/releases and guard raises into positional sound cues.</summary>
[GlobalClass]
public partial class ActorAudioComponent : Node
{
	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public AttackController Attack { get; set; }
	[Export] public DodgeComponent Dodge { get; set; }
	[Export] public GuardComponent Guard { get; set; }
	[Export] public SoundCueProfile FootstepCue { get; set; }
	[Export] public SoundCueProfile RushCue { get; set; }
	[Export] public SoundCueProfile AttackReleaseCue { get; set; }
	[Export] public SoundCueProfile GuardCue { get; set; }
	[Export(PropertyHint.Range, "0.1,4,0.05")] public float StepDistance { get; set; } = 2.5f;
	[Export(PropertyHint.Range, "0,12,0.1")] public float StepSpeed { get; set; } = 2.5f;

	private Vector3 _previousPosition;
	private float _distanceSinceStep;
	private bool _rushPending;
	private bool _wasGuarding;

	public override void _Ready()
	{
		if (Actor == null || Movement == null || !float.IsFinite(StepDistance) || StepDistance <= 0
			|| !float.IsFinite(StepSpeed) || StepSpeed < 0)
		{
			GD.PushError("ActorAudioComponent requires an Actor, Movement and valid step cadence.");
			SetPhysicsProcess(false);
			return;
		}

		_previousPosition = Actor.GlobalPosition;
		_wasGuarding = Guard?.IsGuarding == true;
		SetPhysicsProcess(FootstepCue != null || RushCue != null || GuardCue != null);
			if (Attack != null) this.SubscribeUntilExit(Attack,
				attack => { attack.AttackStarted += OnAttackStarted; attack.AttackReleased += OnAttackReleased; },
				attack => { attack.AttackStarted -= OnAttackStarted; attack.AttackReleased -= OnAttackReleased; });
		if (Dodge != null) this.SubscribeUntilExit(Dodge,
			dodge => { dodge.DodgeStarted += OnDodgeStarted; dodge.DodgeEnded += OnDodgeEnded; },
			dodge => { dodge.DodgeStarted -= OnDodgeStarted; dodge.DodgeEnded -= OnDodgeEnded; });
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!GodotObject.IsInstanceValid(Actor) || !GodotObject.IsInstanceValid(Movement)) return;
		Vector3 position = Actor.GlobalPosition;
		Vector3 startPosition = position - Actor.GetPositionDelta();
		if (startPosition.DistanceTo(_previousPosition) > 0.05f)
		{
			ResetTravel(position);
			return;
		}

		Vector3 displacement = position - _previousPosition;
		displacement.Y = 0;
		bool moved = displacement.LengthSquared() > 0.0001f;
		bool locomoting = moved && Actor.IsOnFloor() && !Movement.IsFalling
			&& Movement.LastMotionKind == MovementMotionKind.Locomotion
			&& Movement.GroundSpeed >= StepSpeed
			&& ActionGate?.Disabled != true && ActionGate?.IsStatusLocked != true && ActionGate?.IsStaggered != true;
		if (locomoting)
		{
			_distanceSinceStep += displacement.Length();
			if (_distanceSinceStep >= StepDistance)
			{
				EmitCue(FootstepCue, position);
				_distanceSinceStep %= StepDistance;
			}
		}
		else if (Movement.LastMotionKind != MovementMotionKind.Locomotion)
		{
			_distanceSinceStep = 0;
		}

		if (_rushPending && Dodge?.IsDodging == true && moved && Movement.LastMotionKind == MovementMotionKind.Override)
		{
			EmitCue(RushCue, position);
			_rushPending = false;
		}

		bool guarding = Guard?.IsGuarding == true;
		if (guarding && !_wasGuarding) EmitCue(GuardCue, position);
		_wasGuarding = guarding;
		_previousPosition = position;
	}

	public override void _ExitTree()
	{
		_rushPending = false;
		_distanceSinceStep = 0;
		base._ExitTree();
	}

	private void OnAttackStarted() => EmitCue(Attack?.CurrentAttack?.WindUpCue, Actor.GlobalPosition);
	private void OnAttackReleased(AttackDefinition definition) =>
		EmitCue(definition?.ReleaseCue ?? AttackReleaseCue, Actor.GlobalPosition);
	private void OnDodgeStarted(Vector3 direction) => _rushPending = true;
	private void OnDodgeEnded() => _rushPending = false;

	private void EmitCue(SoundCueProfile cue, Vector3 position)
	{
		if (cue != null) SignalBus.EmitWorldSoundEmitted(cue, Actor, position);
	}

	private void ResetTravel(Vector3 position)
	{
		_distanceSinceStep = 0;
		_previousPosition = position;
	}
}