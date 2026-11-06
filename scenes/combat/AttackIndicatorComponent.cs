using Godot;

/// <summary>Optional actor capability that renders attack footprints and projectile aim cues.</summary>
[Tool, GlobalClass]
public partial class AttackIndicatorComponent : MeshInstance3D
{
	private AttackIndicatorProfile _profile;
	private StandardMaterial3D _material;
	private AttackIdentity _execution;
	private AttackIndicatorStage _lastStage;
	private float _releaseRemaining;
	private bool _shown;
	private bool _enabled = true;
	private bool _editorPreview;
	public bool IsCommitted => _lastStage is AttackIndicatorStage.Committed or AttackIndicatorStage.Attacking or AttackIndicatorStage.Released;

	[Export]
	public AttackIndicatorProfile Profile
	{
		get => _profile;
		set { _profile = value; if (IsNodeReady()) ApplyProfile(); }
	}
	[Export]
	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; if (!value && IsNodeReady()) Clear(); }
	}
	[Export]
	public bool EditorPreview
	{
		get => _editorPreview;
		set
		{
			_editorPreview = value;
			if (IsNodeReady() && !value) Clear();
		}
	}
	[Export] public float PreviewReach { get; set; } = 1.5f;
	[Export] public float PreviewWidth { get; set; } = 0.8f;
	[Export] public AttackThreatTier PreviewThreat { get; set; } = AttackThreatTier.Normal;
	[Export] public bool PreviewProjectileAiming { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float PreviewAimProgress { get; set; }
	[Export(PropertyHint.Range, "0,80,1")] public float PreviewAimHalfAngleDegrees { get; set; } = 35;

	public override void _Ready()
	{
		EnsureMaterial();
		Visible = false;
		ApplyProfile();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			if (EditorPreview && PreviewProjectileAiming)
				Present(new(default, AttackIndicatorStage.Aiming, PreviewThreat, true,
					GlobalPosition, -GlobalBasis.Z, PreviewReach, [], AimProgress: PreviewAimProgress,
					AimHalfAngleDegrees: PreviewAimHalfAngleDegrees));
			else if (EditorPreview) Present(new(default, AttackIndicatorStage.Aiming, PreviewThreat, false,
				GlobalPosition, -GlobalBasis.Z, PreviewReach, CreatePreviewFootprint()));
			else Clear();
			return;
		}
		if (_releaseRemaining <= 0 || !_shown || _profile == null) return;
		_releaseRemaining = Mathf.Max(0, _releaseRemaining - (float)delta);
		if (_releaseRemaining <= 0) Clear();
		else SetOpacity(_profile.AttackingOpacity * Mathf.Clamp(_releaseRemaining / Mathf.Max(0.001f, _profile.ReleaseFadeDuration), 0, 1));
	}

	/// <summary>Display one immutable attack frame without consulting or changing gameplay state.</summary>
	public void Present(in AttackIndicatorFrame frame)
	{
		if (!Enabled || _profile == null || !IsInsideTree()) { Clear(); return; }
		_execution = frame.Execution;
		_lastStage = frame.Stage;
		_releaseRemaining = 0;
		_shown = true;
		var direction = frame.Direction; direction.Y = 0;
		if (direction.LengthSquared() < 0.0001f) direction = Vector3.Forward;
		Basis basis = Basis.LookingAt(direction.Normalized(), Vector3.Up);
		GlobalTransform = new Transform3D(basis, frame.Origin + Vector3.Up * _profile.GroundOffset);
		Mesh = BuildMesh(frame);
		EnsureMaterial();
		_material.AlbedoColor = Tint(frame.ThreatTier);
		float easedAimProgress = Mathf.Pow(Mathf.Clamp(frame.AimProgress, 0, 1), Mathf.Max(0.01f, _profile.AimConeGrowthExponent));
		SetOpacity(frame.Stage switch
		{
			AttackIndicatorStage.Aiming => Mathf.Lerp(_profile.AimingStartOpacity, _profile.AimingOpacity, easedAimProgress),
			AttackIndicatorStage.Committed => _profile.CommittedOpacity,
			_ => _profile.AttackingOpacity
		});
		Visible = true;
	}

	/// <summary>End only the execution that owns the current cue; a released projectile may leave a short tail.</summary>
	public void End(AttackIdentity execution, bool interrupted)
	{
		if (_execution != execution) return;
		if (!interrupted && _lastStage == AttackIndicatorStage.Released && _profile != null && _profile.ReleaseFadeDuration > 0)
		{
			_releaseRemaining = _profile.ReleaseFadeDuration;
			return;
		}
		Clear();
	}

	public void Clear()
	{
		_execution = default;
		_lastStage = default;
		_releaseRemaining = 0;
		_shown = false;
		Visible = false;
		Mesh = null;
	}

	public override string[] _GetConfigurationWarnings()
	{
		return Profile == null ? ["Assign an AttackIndicatorProfile to render attack warnings."] : [];
	}

	private void ApplyProfile()
	{
		if (_profile == null) { Clear(); return; }
		if (_material != null) _material.AlbedoColor = _profile.NormalTint;
	}

	private void EnsureMaterial()
	{
		_material ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = false, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly };
		MaterialOverride = _material;
	}

	private Color Tint(AttackThreatTier tier) => tier switch
	{
		AttackThreatTier.Heavy => _profile.HeavyTint,
		AttackThreatTier.Unblockable => _profile.UnblockableTint,
		_ => _profile.NormalTint
	};

	private void SetOpacity(float opacity)
	{
		if (_material == null) return;
		var color = _material.AlbedoColor; color.A = Mathf.Clamp(opacity, 0, 1); _material.AlbedoColor = color;
	}

	private ArrayMesh BuildMesh(in AttackIndicatorFrame frame)
	{
		var mesh = new ArrayMesh();
		if (frame.IsProjectile)
		{
			if (frame.Stage == AttackIndicatorStage.Aiming && frame.AimHalfAngleDegrees > 0)
			{
				AddSurface(mesh, BuildAimCone(frame.Length, _profile.ProjectileWidth,
					frame.AimHalfAngleDegrees, frame.AimProgress, _profile));
			}
			else
			{
				var basis = Basis.LookingAt(frame.Direction.Normalized(), Vector3.Up);
				var directions = frame.Directions is { Length: > 0 } ? frame.Directions : [frame.Direction];
				foreach (var lane in directions)
				{
					Vector3 local = basis.Inverse() * lane; local.Y = 0;
					float angle = Mathf.Atan2(local.X, -local.Z);
					Vector2[] points = BuildBar(frame.Length, _profile.ProjectileWidth);
					for (int i = 0; i < points.Length; i++) points[i] = points[i].Rotated(angle);
					AddSurface(mesh, points);
				}
			}
		}
		else AddSurface(mesh, frame.Footprint);
		return mesh;
	}

	private static void AddSurface(ArrayMesh mesh, Vector2[] points)
	{
		if (points == null || points.Length < 3) return;
		var vertices = new Vector3[points.Length];
		for (int i = 0; i < points.Length; i++) vertices[i] = new Vector3(points[i].X, 0, points[i].Y);
		var indices = new int[(points.Length - 2) * 3];
		for (int i = 1; i < points.Length - 1; i++)
		{
			int at = (i - 1) * 3; indices[at] = 0; indices[at + 1] = i; indices[at + 2] = i + 1;
		}
		var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Index] = indices;
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	private static Vector2[] BuildBar(float length, float width) =>
		[new(-width / 2, 0), new(width / 2, 0), new(width / 2, -length), new(-width / 2, -length)];

	/// <summary>Start with the attack's authored aim cone and taper its far edge to the projectile lane at lock.</summary>
	private static Vector2[] BuildAimCone(float length, float laneWidth, float halfAngleDegrees, float progress,
		AttackIndicatorProfile profile)
	{
		float normalizedProgress = Mathf.Clamp(progress, 0, 1);
		float growth = Mathf.Pow(normalizedProgress, Mathf.Max(0.01f, profile.AimConeGrowthExponent));
		float shownLength = Mathf.Lerp(length * Mathf.Clamp(profile.AimConeStartLengthScale, 0.1f, 1), length, growth);
		float angle = Mathf.DegToRad(Mathf.Clamp(halfAngleDegrees, 0, 80));
		float coneWidth = 2 * Mathf.Max(0, shownLength) * Mathf.Tan(angle);
		float endWidth = Mathf.Lerp(Mathf.Max(laneWidth, coneWidth), laneWidth, normalizedProgress);
		return [new(-laneWidth / 2, 0), new(laneWidth / 2, 0), new(endWidth / 2, -shownLength), new(-endWidth / 2, -shownLength)];
	}

	private Vector2[] CreatePreviewFootprint() => BuildBar(PreviewReach, PreviewWidth);
}
