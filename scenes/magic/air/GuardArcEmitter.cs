using Godot;

/// <summary>Emits short decorative arcs from an assigned shield surface.</summary>
public partial class GuardArcEmitter : Node3D
{
	[Export] public Node3D Shell { get; set; }
	[Export] public PackedScene ArcScene { get; set; }
	[Export] public float Radius { get; set; } = 1.6f;
	[Export] public float ArcLengthMin { get; set; } = .45f;
	[Export] public float ArcLengthMax { get; set; } = 1.1f;
	private readonly RandomNumberGenerator _random = new();
	private AreaVisibility _visibility;
	private int _level = 1;
	private float _untilNext = .12f;

	/// <summary>Sets the visual density and an independent cosmetic seed.</summary>
	public void Configure(int level, ulong seed) { _level = Mathf.Clamp(level, 1, 3); _random.Seed = seed; }
	public void SetVisibility(AreaVisibility visibility) => _visibility = visibility;
	public override void _Ready()
	{
		if (Shell == null || ArcScene == null) { GD.PushError("Guard arc emitter requires a shell and arc scene."); SetProcess(false); }
	}
	public override void _Process(double delta)
	{
		_untilNext -= (float)delta;
		if (_untilNext > 0) return;
		_untilNext = (.6f - .17f * (_level - 1)) * _random.RandfRange(.85f, 1.15f);
		float angle = _random.RandfRange(0, Mathf.Tau);
		float y = _random.RandfRange(-.5f, .8f);
		float horizontal = Mathf.Sqrt(1f - y * y);
		Vector3 direction = new(Mathf.Cos(angle) * horizontal, y, Mathf.Sin(angle) * horizontal);
		Vector3 start = Shell.GlobalPosition + direction * Radius;
		Vector3 end = start + direction * _random.RandfRange(ArcLengthMin, ArcLengthMax);
		if (_visibility != null && !_visibility.Contains(start)) return;
		var arc = ArcScene.Instantiate<LightningArc>();
		arc.Configure(start, end, _random.Randi());
		arc.Width = .13f; arc.Jaggedness = .12f;
		arc.Timeline.Duration = .4f; arc.Timeline.FadeOutStart = .32f;
		arc.Impact.Visible = false;
		AddChild(arc);
		if (_visibility != null) arc.Timeline.SetVisibility(_visibility);
	}
}
