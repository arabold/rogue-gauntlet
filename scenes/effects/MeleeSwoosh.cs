using Godot;
using System;
using System.Collections.Generic;

/// <summary>Cosmetic attack ribbon; driven by contact progress and never participates in collision.</summary>
public partial class MeleeSwoosh : MeshInstance3D, IPooledNode
{
	[Export] public bool Vertical { get; set; }
	[Export] public Color Tint { get; set; } = new(0.65f, 0.9f, 1, 0.7f);
	[Export] public float StartDegrees { get; set; } = -70;
	[Export] public float EndDegrees { get; set; } = 70;
	[Export] public float InnerRadiusScale { get; set; } = 0.65f;
	[Export] public float RadiusScale { get; set; } = 1;
	[Export] public float FadeDuration { get; set; } = 0.16f;
	private StandardMaterial3D _material;
	private ImmediateMesh _mesh;
	private float _radius;
	private bool _fading;
	private float _fade;
	private Marker3D _bladeBase, _bladeTip;
	private BoneAttachment3D _attachment;
	private readonly List<(Vector3 Base, Vector3 Tip)> _samples = new();
	public bool FollowsWeapon { get; private set; }
	public int TrailSampleCount => _samples.Count;
	public Vector3 LastBladeBase => _samples.Count > 0 ? _samples[^1].Base : Vector3.Zero;
	public Vector3 LastBladeTip => _samples.Count > 0 ? _samples[^1].Tip : Vector3.Zero;

	public override void _Ready() => EnsureVisuals();

	private void EnsureVisuals()
	{
		if (_mesh != null) return;
		TopLevel = true;
		CastShadow = ShadowCastingSetting.Off;
		_mesh = new ImmediateMesh(); Mesh = _mesh;
		_material = new StandardMaterial3D {
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true
		};
		MaterialOverride = _material;
	}

	public void Initialize(Transform3D frame, float reach, Node3D weapon = null)
	{
		EnsureVisuals();
		_bladeBase = weapon?.FindChild("TrailBase", true, false) as Marker3D;
		_bladeTip = weapon?.FindChild("TrailTip", true, false) as Marker3D;
		_attachment = weapon?.GetAncestorOrNull<BoneAttachment3D>();
		FollowsWeapon = _bladeBase != null && _bladeTip != null;
		_samples.Clear();
		GlobalTransform = FollowsWeapon ? Transform3D.Identity : frame;
		_radius = reach * RadiusScale;
		_fading = false; _fade = 0;
		_material.AlbedoColor = Colors.White;
		_mesh.ClearSurfaces();
	}

	/// <summary>Capture animated blade endpoints, or build a generic arc when no blade markers exist.</summary>
	public void Sample(Transform3D frame, float progress)
	{
		if (_fading) return;
		if (FollowsWeapon)
		{
			SampleBlade();
			return;
		}
		GlobalTransform = frame;
		progress = Mathf.Clamp(progress, 0.01f, 1);
		_mesh.ClearSurfaces();
		_mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		Vector3 Point(float angle, float radius) => Vertical
			? new Vector3(0, Mathf.Sin(angle) * radius, -Mathf.Cos(angle) * radius)
			: new Vector3(Mathf.Sin(angle) * radius, 0, -Mathf.Cos(angle) * radius);
		void Vertex(float t, bool outer)
		{
			float angle = Mathf.DegToRad(Mathf.Lerp(StartDegrees, EndDegrees, t));
			float alpha = Tint.A * (0.15f + 0.85f * t / progress) * (outer ? 0.15f : 1);
			_mesh.SurfaceSetColor(new Color(Tint.R, Tint.G, Tint.B, alpha));
			_mesh.SurfaceAddVertex(Point(angle, _radius * (outer ? 1 : InnerRadiusScale)));
		}
		int segments = Math.Max(2, (int)(24 * progress));
		for (int i = 0; i < segments; i++)
		{
			float a = progress * i / segments, b = progress * (i + 1) / segments;
			Vertex(a, false); Vertex(a, true); Vertex(b, true);
			Vertex(a, false); Vertex(b, true); Vertex(b, false);
		}
		_mesh.SurfaceEnd();
	}

	/// <summary>Seek updates bone poses immediately; refresh this mount before reading marker positions.</summary>
	public void RefreshWeaponPose()
	{
		if (GodotObject.IsInstanceValid(_attachment)) _attachment.OnSkeletonUpdate();
	}

	private void SampleBlade()
	{
		if (!GodotObject.IsInstanceValid(_bladeBase) || !GodotObject.IsInstanceValid(_bladeTip))
		{
			Finish();
			return;
		}
		RefreshWeaponPose();
		var pair = (_bladeBase.GlobalPosition, _bladeTip.GlobalPosition);
		if (_samples.Count > 0 && _samples[^1].Base.IsEqualApprox(pair.Item1)
			&& _samples[^1].Tip.IsEqualApprox(pair.Item2)) return;
		_samples.Add(pair);
		if (_samples.Count > 96) _samples.RemoveAt(0);
		_mesh.ClearSurfaces();
		if (_samples.Count < 2) return;
		_mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		void Vertex(int index, bool tip)
		{
			float age = (float)index / (_samples.Count - 1);
			_mesh.SurfaceSetColor(new Color(Tint.R, Tint.G, Tint.B, Tint.A * (.1f + .9f * age) * (tip ? .15f : 1)));
			_mesh.SurfaceAddVertex(tip ? _samples[index].Tip : _samples[index].Base);
		}
		for (int i = 1; i < _samples.Count; i++)
		{
			Vertex(i - 1, false); Vertex(i - 1, true); Vertex(i, true);
			Vertex(i - 1, false); Vertex(i, true); Vertex(i, false);
		}
		_mesh.SurfaceEnd();
	}

	public void Finish()
	{
		_fading = true; _fade = 0;
		_bladeBase = _bladeTip = null;
		_attachment = null;
	}
	public override void _Process(double delta)
	{
		if (!_fading) return;
		_fade += (float)delta;
		_material.AlbedoColor = new Color(1, 1, 1, Mathf.Max(0, 1 - _fade / Mathf.Max(.01f, FadeDuration)));
		if (_fade >= FadeDuration) ScenePool.Despawn(this);
	}
	public void OnSpawnedFromPool() { _fading = false; _fade = 0; }
	public void OnDespawnedToPool()
	{
		_fading = false; FollowsWeapon = false;
		_bladeBase = _bladeTip = null; _attachment = null;
		_samples.Clear(); _mesh.ClearSurfaces();
	}
}