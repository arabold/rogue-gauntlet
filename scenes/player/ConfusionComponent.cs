using Godot;

/// <summary>
/// Scrambles the player's movement while confused, in the spirit of Rogue's potion of confusion: the
/// direction the player steers is turned by an angle that wanders to a new random target every
/// <see cref="RetargetInterval"/>, so the hero staggers instead of walking straight. It runs after
/// <see cref="InputComponent"/> has read the controls and before the player controller turns them into
/// intent, and only processes while the player is confused.
/// </summary>
[GlobalClass]
public partial class ConfusionComponent : Node
{
	private readonly RandomNumberGenerator _random = new();
	private float _angle;
	private float _targetAngle;
	private double _untilRetarget;

	/// <summary>Input to distort. Required; assign the player's sibling <see cref="InputComponent"/>.</summary>
	[Export] public InputComponent Input { get; set; }

	/// <summary>Buffs that may impose <see cref="Condition"/>. Required.</summary>
	[Export] public BuffController Buffs { get; set; }

	/// <summary>Condition that scrambles movement. Required; reference the shared <c>confused.tres</c>.</summary>
	[Export] public StatusCondition Condition { get; set; }

	/// <summary>Largest deflection from the intended direction, either way.</summary>
	[Export(PropertyHint.Range, "0,180,1,suffix:°")] public float MaxDeflectionDegrees { get; set; } = 135f;

	/// <summary>Seconds between picking a new deflection target.</summary>
	[Export(PropertyHint.Range, "0.1,3,0.05,suffix:s")] public float RetargetInterval { get; set; } = 0.7f;

	/// <summary>How quickly the deflection turns toward its target, in degrees per second.</summary>
	[Export(PropertyHint.Range, "10,720,5,suffix:°/s")] public float TurnRateDegrees { get; set; } = 240f;

	public void ConfigureRandom(ulong seed) => _random.Seed = seed;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.InputModifier;
		if (Input == null || Buffs == null || Condition == null)
		{
			GD.PushError($"{Name}: {nameof(ConfusionComponent)} requires {nameof(Input)}, {nameof(Buffs)} and {nameof(Condition)}.");
			SetPhysicsProcess(false);
			return;
		}

		this.SubscribeUntilExit(Buffs, buffs => buffs.BuffsChanged += OnBuffsChanged, buffs => buffs.BuffsChanged -= OnBuffsChanged);
		OnBuffsChanged();
	}

	/// <summary>Processes only while confused; the stagger starts afresh with every confusion.</summary>
	private void OnBuffsChanged()
	{
		bool confused = Buffs.HasCondition(Condition);
		if (!confused)
		{
			_angle = 0f;
			_targetAngle = 0f;
			_untilRetarget = 0.0;
		}

		SetPhysicsProcess(confused);
	}

	public override void _PhysicsProcess(double delta)
	{
		_untilRetarget -= delta;
		if (_untilRetarget <= 0.0)
		{
			float max = Mathf.DegToRad(MaxDeflectionDegrees);
			_targetAngle = _random.RandfRange(-max, max);
			_untilRetarget = RetargetInterval;
		}

		_angle = Mathf.MoveToward(_angle, _targetAngle, Mathf.DegToRad(TurnRateDegrees) * (float)delta);
		if (Input.InputDirection != Vector3.Zero)
		{
			Input.InputDirection = Input.InputDirection.Rotated(Vector3.Up, _angle);
		}
	}
}