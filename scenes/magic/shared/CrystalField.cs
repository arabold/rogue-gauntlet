using Godot;
using System.Collections.Generic;

public enum CrystalLayout { Ring, Sphere }

/// <summary>
/// Composes independently animated faceted crystals around an effect origin, drawn as one multimesh so even a
/// dense field costs a single draw. A ring stands crystal clusters around the origin, tilted by <see cref="Tilt"/>;
/// a sphere bristles single crystals outward from a shell of <see cref="InnerRadius"/> centred at
/// <see cref="CenterHeight"/>. Crystals spread in over <see cref="SpreadTime"/>, each growing for
/// <see cref="GrowTime"/>; a timed field sinks them again over its last <see cref="ShrinkTime"/>.
/// </summary>
public partial class CrystalField : Node3D
{
	[Export] public ShaderMaterial CrystalMaterial { get; set; }
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public CrystalLayout Layout { get; set; }
	/// <summary>Ring positions (clusters) or sphere crystals at level one.</summary>
	[Export] public int BaseCount { get; set; } = 10;
	[Export] public int AdditionalPerLevel { get; set; } = 4;
	/// <summary>Crystals per ring cluster: one tall crystal with smaller ones leaning out around its foot.</summary>
	[Export] public int ClusterSize { get; set; } = 1;
	[Export] public float Radius { get; set; } = 3f;
	[Export] public float InnerRadius { get; set; } = .5f;
	[Export] public float Height { get; set; } = 1.1f;
	[Export] public float HeightPerLevel { get; set; } = .15f;
	[Export] public float Width { get; set; } = .34f;
	[Export] public float CenterHeight { get; set; } = -.12f;
	[Export] public float Tilt { get; set; } = .22f;
	/// <summary>Sphere layouts spread crystals from the top down to this height on the unit sphere, keeping them off the floor.</summary>
	[Export] public float LowestDirection { get; set; } = -.45f;
	[Export] public float SpreadTime { get; set; } = .6f;
	[Export] public float GrowTime { get; set; } = .3f;
	[Export] public float ShrinkTime { get; set; } = .6f;
	[Export] public float RotationSpeed { get; set; } = .35f;
	private readonly List<Transform3D> _rest = [];
	private readonly List<float> _delays = [];
	private ShaderMaterial _material;
	private MultiMesh _crystals;
	private float _seed;
	private int _level = 1;

	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (CrystalMaterial?.Shader == null || Timeline == null) { GD.PushError("Crystal field requires an assigned crystal material and timeline."); SetProcess(false); return; }
		_material = (ShaderMaterial)CrystalMaterial.Duplicate();
		_seed = GetInstanceId() % 997;
		int groups = BaseCount + AdditionalPerLevel * (_level - 1);
		int cluster = Layout == CrystalLayout.Ring ? Mathf.Max(ClusterSize, 1) : 1;
		_crystals = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = FacetedSpikeMesh.Create(new Vector3(Width, 1, Width), 1.7f, _material), InstanceCount = groups * cluster };
		AddChild(new MultiMeshInstance3D { Multimesh = _crystals, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		float height = Height + HeightPerLevel * (_level - 1);
		for (int group = 0; group < groups; group++)
		{
			float order = (group * 7 % groups) / (float)Mathf.Max(groups - 1, 1);
			Transform3D origin = Layout == CrystalLayout.Sphere ? SphereCrystal(group, groups) : RingCrystal(group, groups, order);
			for (int member = 0; member < cluster; member++)
			{
				Transform3D rest = origin * ClusterMember(group, member, cluster, height);
				_rest.Add(rest); _delays.Add(order * SpreadTime + member * .04f);
				_crystals.SetInstanceTransform(_rest.Count - 1, new Transform3D(Basis.FromScale(Vector3.Zero), rest.Origin));
			}
		}
		_material.SetShaderParameter("effect_fade", 0f);
	}
	private Transform3D RingCrystal(int i, int count, float order)
	{
		float angle = Mathf.Tau * i / count + .25f;
		Vector3 radial = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
		return new Transform3D(new Basis(Vector3.Up.Cross(radial), Tilt) * new Basis(Vector3.Up, angle), radial * Mathf.Lerp(InnerRadius, Radius, .22f + .78f * order) + Vector3.Up * CenterHeight);
	}
	/// <summary>Spreads crystals evenly over the upper sphere along a golden-angle spiral, each pointing straight out.</summary>
	private Transform3D SphereCrystal(int i, int count)
	{
		float y = Mathf.Lerp(1f, LowestDirection, (i + .5f) / count), ring = Mathf.Sqrt(1 - y * y), angle = i * 2.3999632f;
		Vector3 normal = new(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
		Vector3 axis = Vector3.Up.Cross(normal);
		Basis outward = axis.LengthSquared() < .0001f ? Basis.Identity : new Basis(axis.Normalized(), Mathf.Acos(Mathf.Clamp(y, -1, 1)));
		return new Transform3D(outward * new Basis(Vector3.Up, angle * 3.1f), Vector3.Up * CenterHeight + normal * InnerRadius);
	}
	/// <summary>
	/// A crystal's place within its cluster, scaled to its height: the first stands tall at the centre (its height
	/// varying by cluster), the rest are shorter and lean out from its foot like a natural ice crystal cluster.
	/// </summary>
	private Transform3D ClusterMember(int group, int member, int size, float height)
	{
		if (member == 0) return new Transform3D(Basis.FromScale(new Vector3(1, height * Mathf.Lerp(.6f, 1.15f, Hash(group, 1)), 1)), Vector3.Zero);
		float around = Mathf.Tau * (member - 1) / (size - 1) + Hash(group, member + 7) * 1.4f;
		Vector3 side = new(Mathf.Cos(around), 0, Mathf.Sin(around));
		var lean = new Basis(Vector3.Up.Cross(side).Normalized(), Mathf.Lerp(.3f, .75f, Hash(group, member + 13)));
		var scale = Basis.FromScale(new Vector3(.75f, height * Mathf.Lerp(.3f, .65f, Hash(group, member + 19)), .75f));
		return new Transform3D(lean * new Basis(Vector3.Up, around * 2.3f) * scale, side * Width * 1.1f);
	}
	private float Hash(int group, int salt) => Mathf.PosMod(Mathf.Sin(group * 12.9898f + salt * 78.233f + _seed) * 43758.547f, 1f);
	public void SetVisibility(AreaVisibility visibility) { if (_material != null) visibility.Apply(_material); }
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		float elapsed = Timeline.Elapsed;
		bool sustained = Timeline.Sustained;
		_material.SetShaderParameter("effect_fade", Timeline.Opacity);
		if (sustained) RotateY((float)delta * RotationSpeed);
		float shrink = sustained ? 1f : 1f - Mathf.SmoothStep(Timeline.Duration - ShrinkTime, Timeline.Duration, elapsed);
		for (int i = 0; i < _rest.Count; i++)
		{
			float grow = Mathf.SmoothStep(_delays[i], _delays[i] + GrowTime, elapsed);
			float wobble = sustained ? 1f + .045f * Mathf.Sin(elapsed * 2f + i * 2.4f) : 1f;
			Vector3 bob = sustained ? Vector3.Up * (.06f * Mathf.Sin(elapsed * 1.6f + i * 2.1f)) : Vector3.Zero;
			Transform3D rest = _rest[i];
			_crystals.SetInstanceTransform(i, new Transform3D(rest.Basis * Basis.FromScale(new Vector3(wobble, grow * shrink * wobble, wobble)), rest.Origin + bob));
		}
	}
}