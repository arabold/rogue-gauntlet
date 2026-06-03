using Godot;
using System.Collections.Generic;

/// <summary>
/// Adds reusable high-readability impact staging: expanding rings, ground glow, radial streaks, and cracks.
/// </summary>
public partial class ImpactLayerEffect : Node3D, IPooledNode
{
	private const float Tau = Mathf.Pi * 2.0f;

	[Export] public float Duration { get; set; } = 0.85f;
	[Export] public Color RingColor { get; set; } = new(0.6f, 0.95f, 1.0f, 0.75f);
	[Export] public Color DiskColor { get; set; } = new(0.25f, 0.75f, 1.0f, 0.22f);
	[Export] public Color StreakColor { get; set; } = new(0.8f, 1.0f, 1.0f, 0.65f);
	[Export] public Color CrackColor { get; set; } = new(0.08f, 0.12f, 0.16f, 0.8f);
	[Export] public float EmissionEnergy { get; set; } = 2.0f;
	[Export] public float MaxRadius { get; set; } = 2.7f;
	[Export] public float DiskRadius { get; set; } = 2.1f;
	[Export] public int RingCount { get; set; } = 2;
	[Export] public int StreakCount { get; set; } = 22;
	[Export] public int CrackCount { get; set; } = 0;
	[Export] public bool GroundDiskEnabled { get; set; } = true;
	[Export] public bool VerticalStreaks { get; set; } = false;
	[Export] public float UpdateInterval { get; set; } = 0.025f;

	private readonly List<RingPiece> _rings = [];
	private readonly List<StreakPiece> _streaks = [];
	private readonly List<CrackPiece> _cracks = [];
	private MeshInstance3D _disk;
	private StandardMaterial3D _diskMaterial;
	private MultiMeshInstance3D _ringMeshInstance;
	private MultiMeshInstance3D _streakMeshInstance;
	private MultiMeshInstance3D _crackMeshInstance;
	private StandardMaterial3D _ringMaterial;
	private StandardMaterial3D _streakMaterial;
	private StandardMaterial3D _crackMaterial;
	private float _age;
	private float _updateCooldown;

	public override void _Ready()
	{
		BuildDisk();
		BuildRings();
		BuildStreaks();
		BuildCracks();
	}

	public void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_updateCooldown = 0.0f;
		UpdateVisuals(0.0f);
	}

	public void OnDespawnedToPool()
	{
		_age = 0.0f;
		_updateCooldown = 0.0f;
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		_updateCooldown -= (float)delta;
		if (_updateCooldown > 0.0f)
		{
			return;
		}

		_updateCooldown = UpdateInterval;
		UpdateVisuals(Mathf.Clamp(_age / Duration, 0.0f, 1.0f));
	}

	private void UpdateVisuals(float t)
	{
		UpdateDisk(t);
		UpdateRings(t);
		UpdateStreaks(t);
		UpdateCracks(t);
	}

	private void BuildDisk()
	{
		if (!GroundDiskEnabled)
		{
			return;
		}

		_diskMaterial = CreateMaterial(DiskColor, EmissionEnergy * 0.55f);
		_disk = new MeshInstance3D
		{
			Name = "ImpactGroundAftermath",
			Mesh = new CylinderMesh
			{
				TopRadius = 1.0f,
				BottomRadius = 1.0f,
				Height = 0.018f,
				RadialSegments = 56,
			},
			MaterialOverride = _diskMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};

		AddChild(_disk);
	}

	private void BuildRings()
	{
		_ringMaterial = CreateMaterial(RingColor, EmissionEnergy);
		_ringMeshInstance = CreateMultiMeshInstance(
			"ImpactShockRings",
			new TorusMesh
			{
				InnerRadius = 0.92f,
				OuterRadius = 1.08f,
				Rings = 64,
				RingSegments = 8,
			},
			_ringMaterial,
			RingCount);

		for (int i = 0; i < RingCount; i++)
		{
			_rings.Add(new RingPiece(i * 0.16f));
		}
	}

	private void BuildStreaks()
	{
		var random = new RandomNumberGenerator
		{
			Seed = 33191,
		};

		_streakMaterial = CreateMaterial(StreakColor, EmissionEnergy * 1.15f);
		_streakMeshInstance = CreateMultiMeshInstance("ImpactStreaks", CreateTaperedShardMesh(), _streakMaterial, StreakCount);

		for (int i = 0; i < StreakCount; i++)
		{
			float angle = i / (float)Mathf.Max(1, StreakCount) * Tau + random.RandfRange(-0.12f, 0.12f);
			Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
			_streaks.Add(new StreakPiece(
				direction,
				random.RandfRange(0.65f, 1.25f),
				random.RandfRange(0.75f, 1.35f),
				random.RandfRange(0.0f, 0.12f)));
		}
	}

	private void BuildCracks()
	{
		if (CrackCount <= 0)
		{
			return;
		}

		var random = new RandomNumberGenerator
		{
			Seed = 70913,
		};

		_crackMaterial = CreateMaterial(CrackColor, EmissionEnergy * 0.35f);
		_crackMeshInstance = CreateMultiMeshInstance("ImpactGroundCracks", CreateGroundCrackMesh(), _crackMaterial, CrackCount);

		for (int i = 0; i < CrackCount; i++)
		{
			float angle = i / (float)CrackCount * Tau + random.RandfRange(-0.2f, 0.2f);
			Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
			_cracks.Add(new CrackPiece(
				direction,
				random.RandfRange(0.65f, 1.35f),
				random.RandfRange(0.02f, 0.065f)));
		}
	}

	private void UpdateDisk(float t)
	{
		if (_disk == null)
		{
			return;
		}

		float grow = EaseOutCubic(Mathf.Clamp(t * 2.1f, 0.0f, 1.0f));
		float fade = 1.0f - EaseInCubic(Mathf.Clamp((t - 0.35f) / 0.65f, 0.0f, 1.0f));
		_disk.Scale = new Vector3(DiskRadius * grow, 1.0f, DiskRadius * grow);
		SetAlpha(_diskMaterial, DiskColor, DiskColor.A * fade);
	}

	private void UpdateRings(float t)
	{
		if (_ringMeshInstance == null)
		{
			return;
		}

		for (int i = 0; i < _rings.Count; i++)
		{
			RingPiece ring = _rings[i];
			float localT = Mathf.Clamp((t - ring.Delay) / 0.74f, 0.0f, 1.0f);
			float radius = Mathf.Lerp(0.16f, MaxRadius, EaseOutCubic(localT));
			float visible = localT <= 0.0f ? 0.0f : 1.0f;
			Transform3D transform = new(
				new Basis(Vector3.Right * radius * visible, Vector3.Forward * radius * visible, Vector3.Down * radius * visible),
				Vector3.Zero);
			_ringMeshInstance.Multimesh.SetInstanceTransform(i, transform);
		}

		SetAlpha(_ringMaterial, RingColor, RingColor.A * (1.0f - EaseInCubic(t)));
	}

	private void UpdateStreaks(float t)
	{
		if (_streakMeshInstance == null)
		{
			return;
		}

		for (int i = 0; i < _streaks.Count; i++)
		{
			StreakPiece streak = _streaks[i];
			float localT = Mathf.Clamp((t - streak.Delay) / 0.62f, 0.0f, 1.0f);
			float distance = Mathf.Lerp(0.2f, MaxRadius * 0.95f * streak.Speed, EaseOutCubic(localT));
			float length = Mathf.Lerp(0.95f, 0.12f, localT) * streak.Size;
			float width = Mathf.Lerp(0.1f, 0.015f, localT) * streak.Size;
			float height = VerticalStreaks ? Mathf.Lerp(0.34f, 1.45f, 1.0f - localT) : 0.035f;
			Vector3 position = streak.Direction * distance + Vector3.Up * (VerticalStreaks ? Mathf.Lerp(0.25f, 1.25f, localT) : 0.04f);
			float visible = localT <= 0.0f ? 0.0f : 1.0f;

			_streakMeshInstance.Multimesh.SetInstanceTransform(i, new Transform3D(
				new Basis(
					streak.Direction.Cross(Vector3.Up).Normalized() * width * visible,
					Vector3.Up * height * visible,
					streak.Direction * length * visible),
				position));
		}

		SetAlpha(_streakMaterial, StreakColor, StreakColor.A * (1.0f - EaseInCubic(t)));
	}

	private void UpdateCracks(float t)
	{
		if (_crackMeshInstance == null)
		{
			return;
		}

		for (int i = 0; i < _cracks.Count; i++)
		{
			CrackPiece crack = _cracks[i];
			float grow = EaseOutCubic(Mathf.Clamp(t * 2.0f, 0.0f, 1.0f));
			float length = MaxRadius * 0.8f * crack.LengthMultiplier * grow;
			Vector3 side = crack.Direction.Cross(Vector3.Up).Normalized();
			_crackMeshInstance.Multimesh.SetInstanceTransform(i, new Transform3D(
				new Basis(side * crack.Width, Vector3.Up * 0.01f, crack.Direction * length),
				crack.Direction * length * 0.5f + Vector3.Up * 0.018f));
		}

		float fade = 1.0f - EaseInCubic(Mathf.Clamp((t - 0.45f) / 0.55f, 0.0f, 1.0f));
		SetAlpha(_crackMaterial, CrackColor, CrackColor.A * fade);
	}

	private MultiMeshInstance3D CreateMultiMeshInstance(string nodeName, Mesh mesh, Material material, int instanceCount)
	{
		var multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = mesh,
			InstanceCount = Mathf.Max(0, instanceCount),
		};

		var node = new MultiMeshInstance3D
		{
			Name = nodeName,
			Multimesh = multiMesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		node.Set("physics_interpolation_mode", 2);

		AddChild(node);
		return node;
	}

	private static StandardMaterial3D CreateMaterial(Color color, float emissionEnergy)
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(color.R, color.G, color.B),
			EmissionEnergyMultiplier = emissionEnergy,
		};
	}

	private static ArrayMesh CreateTaperedShardMesh()
	{
		Vector3[] vertices =
		[
			new(0.0f, 0.0f, 0.65f),
			new(-0.55f, 0.0f, -0.25f),
			new(0.0f, 0.2f, -0.5f),
			new(0.55f, 0.0f, -0.25f),
			new(0.0f, -0.2f, -0.5f),
		];

		int[] indices =
		[
			0, 1, 2,
			0, 2, 3,
			0, 3, 4,
			0, 4, 1,
			1, 4, 2,
			2, 4, 3,
		];

		return CreateArrayMesh(vertices, indices);
	}

	private static ArrayMesh CreateGroundCrackMesh()
	{
		Vector3[] vertices =
		[
			new(-0.35f, 0.0f, 0.0f),
			new(0.22f, 0.0f, 0.06f),
			new(0.5f, 0.0f, 0.0f),
			new(0.18f, 0.0f, -0.05f),
		];

		int[] indices = [0, 1, 3, 1, 2, 3];
		return CreateArrayMesh(vertices, indices);
	}

	private static ArrayMesh CreateArrayMesh(Vector3[] vertices, int[] indices)
	{
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static void SetAlpha(StandardMaterial3D material, Color baseColor, float alpha)
	{
		material.AlbedoColor = new Color(baseColor.R, baseColor.G, baseColor.B, Mathf.Max(0.0f, alpha));
	}

	private static float EaseOutCubic(float t)
	{
		return 1.0f - Mathf.Pow(1.0f - t, 3.0f);
	}

	private static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	private sealed record RingPiece(float Delay);
	private sealed record StreakPiece(Vector3 Direction, float Speed, float Size, float Delay);
	private sealed record CrackPiece(Vector3 Direction, float LengthMultiplier, float Width);
}
