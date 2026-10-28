using Godot;
using System.Collections.Generic;

/// <summary>Composes independently animated faceted crystals around an effect origin.</summary>
public partial class CrystalField : Node3D
{
	[Export] public ShaderMaterial CrystalMaterial { get; set; }
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public int BaseCount { get; set; } = 10;
	[Export] public int AdditionalPerLevel { get; set; } = 4;
	[Export] public float Radius { get; set; } = 3f;
	[Export] public float InnerRadius { get; set; } = .5f;
	[Export] public float Height { get; set; } = 1.1f;
	[Export] public float HeightPerLevel { get; set; } = .15f;
	[Export] public float Width { get; set; } = .34f;
	[Export] public float CenterHeight { get; set; } = -.12f;
	[Export] public float Tilt { get; set; } = .22f;
	[Export] public float RotationSpeed { get; set; } = .35f;
	private readonly List<MeshInstance3D> _crystals = [];
	private readonly List<Vector3> _positions = [];
	private readonly List<float> _delays = [];
	private ShaderMaterial _material;
	private int _level = 1;

	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (CrystalMaterial?.Shader == null || Timeline == null) { GD.PushError("Crystal field requires an assigned crystal material and timeline."); SetProcess(false); return; }
		_material = (ShaderMaterial)CrystalMaterial.Duplicate();
		int count = BaseCount + AdditionalPerLevel * (_level - 1);
		for (int i = 0; i < count; i++)
		{
			float angle = Mathf.Tau * i / count + .25f;
			float fraction = Timeline.Sustained ? 1 : .22f + .78f * ((i * 7 % count) / (float)Mathf.Max(count - 1, 1));
			float distance = Mathf.Lerp(InnerRadius, Radius, fraction);
			Vector3 radial = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
			float height = (Height + HeightPerLevel * (_level - 1)) * (.75f + .25f * Mathf.Sin(i * 4.3f + 1));
			var crystal = new MeshInstance3D { Mesh = FacetedSpikeMesh.Create(new Vector3(Width, height, Width), angle, _material), Position = radial * distance + Vector3.Up * CenterHeight, Basis = new Basis(Vector3.Up.Cross(radial), Tilt), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(crystal); _crystals.Add(crystal); _positions.Add(crystal.Position); _delays.Add(fraction * .28f);
			crystal.Scale = Vector3.Zero;
		}
		_material.SetShaderParameter("effect_fade", 0f);
	}
	public void SetVisibility(AreaVisibility visibility) { if (_material != null) visibility.Apply(_material); }
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		float elapsed = Timeline.Elapsed;
		float progress = Timeline.Progress;
		bool sustained = Timeline.Sustained;
		float fade = sustained ? Mathf.SmoothStep(0, .2f, elapsed) : EffectEnvelope.Evaluate(progress, .08f, .65f);
		_material.SetShaderParameter("effect_fade", fade);
		if (sustained) RotateY((float)delta * RotationSpeed);
		for (int i = 0; i < _crystals.Count; i++)
		{
			float grow = sustained ? Mathf.SmoothStep(0, .3f, elapsed) : Mathf.SmoothStep(_delays[i], _delays[i] + .15f, progress);
			float shrink = sustained ? 1f : 1f - Mathf.SmoothStep(.7f, 1f, progress);
			float wobble = sustained ? 1f + .045f * Mathf.Sin(elapsed * 2f + i * 2.4f) : 1f;
			_crystals[i].Scale = new Vector3(wobble, grow * shrink * wobble, wobble);
			if (sustained) _crystals[i].Position = _positions[i] + Vector3.Up * (.06f * Mathf.Sin(elapsed * 1.6f + i * 2.1f));
		}
	}
}