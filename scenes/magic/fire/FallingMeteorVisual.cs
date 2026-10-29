using Godot;

/// <summary>Moves and fades a falling meteor, then creates its optional impact visuals once.</summary>
public partial class FallingMeteorVisual : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	[Export] public Node3D Meteor { get; set; }
	[Export] public FallingBodyMotion Fall { get; set; }
	[Export] public MeshInstance3D Rock { get; set; }
	[Export] public MeshInstance3D Shell { get; set; }
	[Export] public GpuParticles3D[] Emitters { get; set; } = [];
	[Export] public PackedScene LandingImpact { get; set; }
	[Export] public PackedScene GroundCracks { get; set; }
	[Export] public float FadeInTime { get; set; } = .12f;
	[Export] public float FadeAfterImpact { get; set; } = .55f;
	private BaseMaterial3D _rockMaterial;
	private ShaderMaterial _shellMaterial;
	private float _rockOpacity;
	private bool _started;
	public override void _Ready()
	{
		if (Timeline == null || VisualRoot == null || Meteor == null || Fall == null || System.Array.Exists(Emitters, emitter => emitter == null))
		{ GD.PushError("Meteor requires a timeline, falling motion and valid assigned visuals."); SetProcess(false); return; }
		if (Rock?.GetActiveMaterial(0) is BaseMaterial3D stone)
		{
			_rockMaterial = (BaseMaterial3D)stone.Duplicate();
			_rockMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			_rockOpacity = _rockMaterial.AlbedoColor.A; Rock.MaterialOverride = _rockMaterial;
		}
		if (Shell?.MaterialOverride is ShaderMaterial shell)
		{
			_shellMaterial = (ShaderMaterial)shell.Duplicate(); Shell.MaterialOverride = _shellMaterial;
		}
		Fall.Impact += OnImpact;
		SetOpacity(0);
		Meteor.Visible = Timeline.StartDelay <= 0;
		if (!Meteor.Visible) foreach (var emitter in Emitters) emitter.Emitting = false;
	}
	private void SetOpacity(float opacity)
	{
		if (_rockMaterial != null) { Color color = _rockMaterial.AlbedoColor; color.A = _rockOpacity * opacity; _rockMaterial.AlbedoColor = color; }
		_shellMaterial?.SetShaderParameter("effect_fade", opacity);
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		if (!_started)
		{
			_started = true; Meteor.Visible = true;
			foreach (var emitter in Emitters) { emitter.Restart(); emitter.Emitting = true; }
		}
		float time = Timeline.Elapsed;
		SetOpacity(Mathf.SmoothStep(0, Mathf.Max(FadeInTime, .001f), time) * (1 - Mathf.SmoothStep(Fall.FlightTime, Fall.FlightTime + Mathf.Max(FadeAfterImpact, .001f), time)));
	}
	private void OnImpact()
	{
		if (LandingImpact != null)
		{
			var impact = LandingImpact.Instantiate<Node3D>(); VisualRoot.GetParent().AddChild(impact);
			impact.GlobalPosition = VisualRoot.GlobalPosition + Vector3.Up * .08f;
			impact.Scale = VisualRoot.Scale;
		}
		if (GroundCracks != null)
		{
			var cracks = GroundCracks.Instantiate<Node3D>(); VisualRoot.GetParent().AddChild(cracks);
			cracks.GlobalPosition = VisualRoot.GlobalPosition; cracks.Scale = VisualRoot.Scale;
		}
		foreach (var emitter in Emitters) emitter.Emitting = false;
	}
}