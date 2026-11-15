using Godot;
using System;
using System.Collections.Generic;

/// <summary>Opt-in ambient bending on an explicit prop subtree. Preserves source surfaces and owns only its private material overrides.</summary>
[Tool, GlobalClass]
public partial class AmbientMotionComponent : Node
{
	[Export]
	public Node3D Target
	{
		get;
		set { field = value; if (IsNodeReady() && !Engine.IsEditorHint()) ApplyEnabled(); }
	}
	[Export]
	public Airflow Airflow
	{
		get;
		set
		{
			_unwatchAirflow?.Invoke();
			_unwatchAirflow = null;
			field = value;
			if (IsNodeReady() && !Engine.IsEditorHint()) { WatchAirflow(); ApplyEnabled(); }
		}
	}
	[Export]
	public ShaderMaterial Material
	{
		get;
		set { field = value; if (IsNodeReady() && !Engine.IsEditorHint()) ApplyEnabled(); }
	}
	[Export]
	public AmbientMotionProfile Profile
	{
		get;
		set
		{
			_unwatchProfile?.Invoke();
			_unwatchProfile = null;
			field = value;
			if (IsNodeReady() && !Engine.IsEditorHint()) { WatchProfile(); ApplyProfile(); }
		}
	}
	[Export]
	public bool Enabled
	{
		get;
		set { field = value; if (IsNodeReady() && !Engine.IsEditorHint()) ApplyEnabled(); }
	} = true;

	private sealed record Surface(MeshMaterialSnapshot Snapshot, ShaderMaterial[] Materials, float CullMargin);
	private readonly List<Surface> _surfaces = [];
	private Aabb _bounds;
	private Vector2 _response, _velocity;
	private Action _unwatchProfile, _unwatchAirflow;
	private static readonly StringName MeshToTarget = "mesh_to_target", TargetToMesh = "target_to_mesh",
		Anchor = "motion_anchor", Axis = "free_axis", Support = "support_normal", Span = "free_span", Pinned = "pinned_fraction",
		Bend = "bend_amplitude", Flutter = "flutter_amplitude", FlutterFrequency = "flutter_frequency",
		Wind = "motion_wind", Time = "motion_time", Phase = "motion_phase", VertexColor = "source_vertex_color";

	public override void _Ready()
	{
		SetProcess(false);
		if (Engine.IsEditorHint()) return;
		if (Target == null || Profile == null || Material?.Shader == null)
		{
			GD.PushError($"{Name}: assign Target, Profile and a shader Material.");
			return;
		}
		WatchAirflow();
		WatchProfile();
		ApplyEnabled();
	}

	private void WatchProfile()
	{
		_unwatchProfile?.Invoke();
		_unwatchProfile = null;
		if (Profile != null) _unwatchProfile = this.SubscribeUntilExit(Profile,
			source => source.Changed += ApplyProfile, source => source.Changed -= ApplyProfile);
	}

	private void WatchAirflow()
	{
		_unwatchAirflow?.Invoke();
		_unwatchAirflow = null;
		if (Airflow != null) _unwatchAirflow = this.SubscribeUntilExit(Airflow,
			source => source.ActiveChanged += OnAirflowChanged, source => source.ActiveChanged -= OnAirflowChanged);
	}

	private void ApplyEnabled()
	{
		SetProcess(false);
		Restore();
		_response = _velocity = Vector2.Zero;
		if (!Enabled || Target == null || Profile == null || Material?.Shader == null || !IsInstanceValid(Airflow)) return;
		if (!SurfaceMaterialFactory.CanCapture(Target)) return;
		Capture(Target);
		ApplyProfile();
		SetProcess(_surfaces.Count > 0);
	}

	private void Capture(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			var snapshot = new MeshMaterialSnapshot(mesh);
			var materials = new ShaderMaterial[snapshot.SurfaceCount];
			Transform3D toTarget = Target.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
			Transform3D toMesh = toTarget.AffineInverse();
			Vector3 position = Target.GlobalPosition;
			float phase = Mathf.PosMod(position.X * .73f + position.Z * .37f, Mathf.Tau);
			Aabb bounds = toTarget * mesh.GetAabb();
			_bounds = _surfaces.Count == 0 ? bounds : _bounds.Merge(bounds);
			for (int i = 0; i < materials.Length; i++)
			{
				var material = materials[i] = (ShaderMaterial)Material.Duplicate();
				if (WallClippedMaterial.SourceOf(snapshot.ActiveMaterial(i)) is { } source)
				{
					SurfaceMaterialFactory.ApplySource(material, source);
					material.SetShaderParameter(VertexColor, source.VertexColorUseAsAlbedo);
				}
				material.SetShaderParameter(MeshToTarget, toTarget);
				material.SetShaderParameter(TargetToMesh, toMesh);
				material.SetShaderParameter(Phase, phase);
			}
			_surfaces.Add(new(snapshot, materials, mesh.ExtraCullMargin));
			snapshot.ApplySurfaces(materials);
		}
		foreach (Node child in node.GetChildren()) Capture(child);
	}

	private void ApplyProfile()
	{
		if (Profile == null) { Enabled = false; return; }
		Vector3 axis = Profile.FreeDirection.Normalized();
		Vector3 anchor = _bounds.Position + _bounds.Size * Profile.Anchor;
		Vector3 end = new(axis.X >= 0 ? _bounds.End.X : _bounds.Position.X,
			axis.Y >= 0 ? _bounds.End.Y : _bounds.Position.Y, axis.Z >= 0 ? _bounds.End.Z : _bounds.Position.Z);
		float span = Mathf.Max(.001f, (end - anchor).Dot(axis));
		foreach (var surface in _surfaces)
		{
			float maxForce = Airflow?.Profile == null ? 1 : Mathf.Max(1, Airflow.Profile.Strength + Airflow.Profile.GustStrength);
			surface.Snapshot.Mesh.ExtraCullMargin = Mathf.Max(surface.CullMargin,
				2 * maxForce * (Profile.BendAmplitude + 1.35f * Profile.FlutterAmplitude));
			foreach (var material in surface.Materials)
			{
				material.SetShaderParameter(Anchor, anchor);
				material.SetShaderParameter(Axis, axis);
				material.SetShaderParameter(Support, Profile.SupportNormal);
				material.SetShaderParameter(Span, span);
				material.SetShaderParameter(Pinned, Profile.PinnedFraction);
				material.SetShaderParameter(Bend, Profile.BendAmplitude);
				material.SetShaderParameter(Flutter, Profile.FlutterAmplitude);
				material.SetShaderParameter(FlutterFrequency, Profile.FlutterFrequency);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (!IsInstanceValid(Airflow) || !IsInstanceValid(Target)) { Enabled = false; return; }
		Vector2 target = Airflow.Sample(Target.GlobalPosition);
		float dt = (float)delta * Mathf.Clamp(Airflow.PlaybackSpeed, 0, 2);
		// Implicit spring integration stays stable through frame-rate changes; each prop retains its own inertia.
		float omega = Mathf.Tau * Profile.ResponseFrequency;
		float f = 1 + 2 * dt * Profile.Damping * omega, dtOmega = dt * omega * omega;
		float inverse = 1 / (f + dt * dtOmega);
		Vector2 previous = _response;
		_response = (f * previous + dt * _velocity + dt * dtOmega * target) * inverse;
		_velocity = (_velocity + dtOmega * (target - previous)) * inverse;
		Vector3 localWind = Target.GlobalBasis.Inverse() * new Vector3(_response.X, 0, _response.Y);
		foreach (var surface in _surfaces)
		foreach (var material in surface.Materials)
		{
			material.SetShaderParameter(Wind, localWind);
			material.SetShaderParameter(Time, Airflow.Elapsed);
		}
		if (!Airflow.IsActive && _response.LengthSquared() + _velocity.LengthSquared() < .000001f) SetProcess(false);
	}

	private void OnAirflowChanged(bool active)
	{
		if (!Enabled || _surfaces.Count == 0) return;
		ApplyProfile();
		SetProcess(true);
	}

	private void Restore()
	{
		foreach (var surface in _surfaces)
			if (surface.Snapshot.RestoreMaterials()) surface.Snapshot.Mesh.ExtraCullMargin = surface.CullMargin;
		_surfaces.Clear();
	}

	public override void _ExitTree()
	{
		SetProcess(false);
		Restore();
		RequestReady();
	}
	public override string[] _GetConfigurationWarnings() => Target == null || Airflow == null || Profile == null || Material?.Shader == null
		? ["Assign Target, Airflow, Profile and a shader Material."] : [];
}