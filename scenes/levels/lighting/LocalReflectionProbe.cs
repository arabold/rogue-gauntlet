using Godot;

/// <summary>One throttled world capture near an injected focus; static geometry is recaptured only after meaningful travel.</summary>
[GlobalClass]
public partial class LocalReflectionProbe : ReflectionProbe
{
	[Export]
	public Node3D Focus
	{
		get;
		set
		{
			field = value;
			_captured = false;
			if (!IsNodeReady()) return;
			Visible = false;
			SetProcess(IsInstanceValid(Focus) && ReflectionsActive);
		}
	}
	[Export] public WorldLighting Lighting { get; set; }
	[Export(PropertyHint.Range, "1,16,0.5")] public float FollowDistance { get; set; } = 6;
	[Export(PropertyHint.Range, "0.25,10,0.25")] public float MinimumInterval { get; set; } = 1.5f;
	[Export] public Vector3 CaptureOffset { get; set; } = new(0, 1.5f, 0);
	private bool _captured;
	private float _cooldown;
	private bool ReflectionsActive => Lighting is { IsDark: false } && Lighting.IsEffectEnabled(EnvironmentalEffect.Reflections);
	public override void _Ready()
	{
		Visible = false;
		if (Lighting == null) { GD.PushError($"{Name}: assign Lighting."); SetProcess(false); return; }
		this.SubscribeUntilExit(Lighting, world => world.DarknessChanged += OnDarknessChanged,
			world => world.DarknessChanged -= OnDarknessChanged);
		this.SubscribeUntilExit(Lighting, world => world.EffectsChanged += ApplyEnabled, world => world.EffectsChanged -= ApplyEnabled);
		ApplyEnabled();
	}
	private void OnDarknessChanged(bool dark) => ApplyEnabled();
	private void ApplyEnabled()
	{
		if (!ReflectionsActive) _captured = false;
		Visible = ReflectionsActive && _captured;
		SetProcess(ReflectionsActive && IsInstanceValid(Focus));
	}
	public override void _Process(double delta)
	{
		if (!IsInstanceValid(Focus)) { Visible = false; SetProcess(false); return; }
		_cooldown = Mathf.Max(0, _cooldown - (float)delta);
		Vector3 point = Focus.GlobalPosition + CaptureOffset;
		if (_captured && (_cooldown > 0 || GlobalPosition.DistanceSquaredTo(point) < FollowDistance * FollowDistance)) return;
		// Once-mode probes recapture when their transform changes, spreading capture over six frames.
		GlobalPosition = point;
		ResetPhysicsInterpolation();
		Visible = true;
		_captured = true;
		_cooldown = Mathf.Max(.25f, MinimumInterval);
	}
}