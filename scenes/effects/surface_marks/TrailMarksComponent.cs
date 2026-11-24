using Godot;

/// <summary>
/// Leaves a fading path of surface marks behind a moving actor, such as a slime's trail. A fixed ring of marks is
/// recycled oldest first, so a long chase never grows the scene. Marks are placed only where the floor is revealed and
/// respect the world's surface-mark switch through <see cref="SurfaceMark"/>.
/// </summary>
[GlobalClass]
public partial class TrailMarksComponent : Node3D
{
	[Export] public MovementComponent Movement { get; set; }
	/// <summary>A scene whose root is a <see cref="SurfaceMark"/>; its profile's tint is a fresh mark's opacity.</summary>
	[Export] public PackedScene MarkScene { get; set; }
	/// <summary>Ground distance between two marks.</summary>
	[Export(PropertyHint.Range, "0.05,3,0.05")] public float Spacing { get; set; } = .45f;
	/// <summary>Seconds a mark stays at full opacity before it starts to fade.</summary>
	[Export(PropertyHint.Range, "0,30,0.1")] public float Lifetime { get; set; } = 4f;
	[Export(PropertyHint.Range, "0.05,10,0.05")] public float FadeDuration { get; set; } = 1.5f;
	/// <summary>Marks in the ring; once all are in use the oldest moves to the newest position.</summary>
	[Export(PropertyHint.Range, "1,64,1")] public int Capacity { get; set; } = 16;
	/// <summary>Random scale applied to the mark scene's size, per placement.</summary>
	[Export] public Vector2 SizeRange { get; set; } = new(.85f, 1.15f);
	[Export]
	public bool Enabled
	{
		get;
		set
		{
			field = value;
			if (!value && IsNodeReady()) Clear();
		}
	} = true;

	private SurfaceMark[] _marks = [];
	private float[] _ages = [];
	private Vector3 _baseSize;
	private int _next;
	private float _travelled;
	private Vector3 _previous;
	private ExplorationVisibility _exploration;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (Movement?.Actor == null || MarkScene == null)
		{
			GD.PushWarning($"{Name}: a trail needs its actor's Movement and a MarkScene.");
			SetProcess(false);
			SetPhysicsProcess(false);
			return;
		}
		_exploration = new ExplorationVisibility(this);
		var level = this.GetAncestorOrNull<Level>();
		_random.Seed = GenerationRandom.DeriveSeed(level?.Seed ?? RunState.DefaultSeed, "vfx.trail", Movement.Actor.GetPath());
		_marks = new SurfaceMark[Capacity];
		_ages = new float[Capacity];
		for (int i = 0; i < Capacity; i++)
		{
			if (MarkScene.Instantiate() is not SurfaceMark mark)
			{
				GD.PushError($"{Name}: MarkScene needs a SurfaceMark root.");
				SetProcess(false);
				SetPhysicsProcess(false);
				return;
			}
			mark.TopLevel = true;
			mark.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
			VisualHelper.Mark(mark);
			AddChild(mark);
			_marks[i] = mark;
		}
		_baseSize = _marks[0].Size;
		Clear();
		_previous = Movement.Actor.GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 position = Movement.Actor.GlobalPosition;
		Vector3 step = position - _previous;
		_previous = position;
		step.Y = 0;
		if (!Enabled || !Movement.IsMoving || !Movement.Actor.IsOnFloor()) return;
		_travelled += step.Length();
		if (_travelled < Spacing) return;
		_travelled = 0;
		if (_exploration.IsRevealed(position)) Place(position, step);
	}

	/// <summary>Ages the live marks and fades them out; runs only while one is live (<see cref="Place"/> starts it).</summary>
	public override void _Process(double delta)
	{
		bool live = false;
		for (int i = 0; i < _marks.Length; i++)
		{
			if (_ages[i] < 0) continue;
			_ages[i] += (float)delta;
			live = true;
			if (_ages[i] <= Lifetime) continue;
			float fade = 1 - Mathf.SmoothStep(Lifetime, Lifetime + FadeDuration, _ages[i]);
			Show(i, fade);
			if (fade <= 0) _ages[i] = -1;
		}
		if (!live) SetProcess(false);
	}

	/// <summary>Removes every mark at once, for an actor that leaves or turns its trail off.</summary>
	public void Clear()
	{
		for (int i = 0; i < _marks.Length; i++)
		{
			_ages[i] = -1;
			Show(i, 0);
		}
		_travelled = 0;
		SetProcess(false);
	}

	private void Place(Vector3 position, Vector3 heading)
	{
		var mark = _marks[_next];
		_ages[_next] = 0;
		mark.GlobalPosition = position;
		float yaw = heading.LengthSquared() > 1e-6f ? Mathf.Atan2(heading.X, heading.Z) : _random.RandfRange(0, Mathf.Tau);
		mark.GlobalRotation = new Vector3(0, yaw + _random.RandfRange(-.35f, .35f), 0);
		mark.Size = _baseSize * _random.RandfRange(Mathf.Min(SizeRange.X, SizeRange.Y), Mathf.Max(SizeRange.X, SizeRange.Y));
		mark.ResetPhysicsInterpolation();
		Show(_next, 1);
		_next = (_next + 1) % _marks.Length;
		SetProcess(true);
	}

	private void Show(int index, float opacity)
	{
		var mark = _marks[index];
		Color tint = mark.Profile?.Tint ?? Colors.White;
		mark.Modulate = tint with { A = tint.A * opacity };
	}

	public override void _ExitTree() => Clear();
}
