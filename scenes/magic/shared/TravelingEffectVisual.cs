using Godot;

/// <summary>Visual-only traveling effect with a fading wake and an independently instanced impact.</summary>
public partial class TravelingEffectVisual : Node3D
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public MeshInstance3D Core { get; set; }
	[Export] public Node3D DirectionalVisual { get; set; }
	[Export] public MeshInstance3D Shell { get; set; }
	[Export] public MagicRibbonTrail[] Trails { get; set; } = [];
	[Export] public GpuParticles3D Sparks { get; set; }
	[Export] public PackedScene ImpactScene { get; set; }
	[Export] public float Speed { get; set; } = 11f;
	[Export] public bool GroundImpact { get; set; }
	public float ImpactGroundHeight { get; set; }
	private Vector3 _origin, _destination;
	private AreaVisibility _visibility;
	private float _flightTime, _elapsed, _size = 1;

	/// <summary>Captures the flight endpoints and visibility before this scene enters the tree.</summary>
	public void Configure(Vector3 origin, Vector3 destination, int level, AreaVisibility visibility)
	{
		_origin = origin; _destination = destination; _visibility = visibility;
		_size = Mathf.Lerp(.65f, 1.15f, (Mathf.Clamp(level, 1, 3) - 1) / 2f);
		_flightTime = Mathf.Max(.08f, origin.DistanceTo(destination) / Mathf.Max(Speed, .01f));
		Timeline.Duration = _flightTime + .6f;
		Timeline.FadeOutStart = _flightTime / Timeline.Duration;
		Core.Scale = Shell.Scale = Vector3.One * _size;
		foreach (var trail in Trails) trail.Width *= _size;
	}
	public override void _Ready()
	{
		GlobalPosition = _origin;
		if (DirectionalVisual != null && _origin.DistanceSquaredTo(_destination) > .001f) DirectionalVisual.LookAt(_destination, Vector3.Up);
		if (_visibility != null) Timeline.SetVisibility(_visibility);
	}
	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		float progress = Mathf.Clamp(_elapsed / _flightTime, 0, 1);
		GlobalPosition = _origin.Lerp(_destination, progress);
		if (progress < 1) return;
		SetProcess(false);
		if (Sparks != null) Sparks.Emitting = false;
		if (ImpactScene == null) return;
		var impact = ImpactScene.Instantiate<VisualEffectInstance>();
		GetParent().AddChild(impact);
		impact.GlobalPosition = GroundImpact ? new Vector3(_destination.X, ImpactGroundHeight, _destination.Z) : _destination;
		impact.Scale = Vector3.One * _size;
		if (_visibility != null) impact.SetVisibility(_visibility);
	}
}