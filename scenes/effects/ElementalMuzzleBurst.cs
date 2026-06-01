using Godot;
using System.Collections.Generic;

/// <summary>
/// Short element-colored cast flash for staff projectiles: muzzle orb, expanding rings, and forward sparks.
/// </summary>
public partial class ElementalMuzzleBurst : Node3D, IPooledNode
{
	private const float Tau = Mathf.Pi * 2.0f;

	[Export] public float Lifetime { get; set; } = 0.38f;
	[Export] public Color CoreColor { get; set; } = new(1.0f, 0.8f, 0.25f, 0.82f);
	[Export] public Color AccentColor { get; set; } = new(1.0f, 0.25f, 0.04f, 0.65f);
	[Export] public float EmissionEnergy { get; set; } = 3.0f;
	[Export] public int SparkCount { get; set; } = 24;
	[Export] public float Radius { get; set; } = 0.42f;
	[Export] public float UpdateInterval { get; set; } = 0.025f;

	private readonly List<MuzzleSpark> _sparks = [];
	private readonly List<MeshInstance3D> _rings = [];
	private readonly List<StandardMaterial3D> _ringMaterials = [];
	private MeshInstance3D _core;
	private StandardMaterial3D _coreMaterial;
	private float _age;
	private float _updateCooldown;
	private bool _isActive;
	private int _lifetimeVersion;

	public override void _Ready()
	{
		BuildCore();
		BuildRings();
		BuildSparks();
		StartLifetime();
	}

	public void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_updateCooldown = 0.0f;
		_isActive = true;
		StartLifetime();
	}

	public void OnDespawnedToPool()
	{
		_isActive = false;
	}

	private void StartLifetime()
	{
		_isActive = true;
		int lifetimeVersion = ++_lifetimeVersion;
		GetTree().CreateTimer(Lifetime).Timeout += () => OnLifetimeExpired(lifetimeVersion);
	}

	private void OnLifetimeExpired(int lifetimeVersion)
	{
		if (!_isActive || lifetimeVersion != _lifetimeVersion)
		{
			return;
		}

		if (ScenePool.IsTracked(this))
		{
			ScenePool.Despawn(this);
			return;
		}

		QueueFree();
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
		float t = Mathf.Clamp(_age / Lifetime, 0.0f, 1.0f);
		UpdateCore(t);
		UpdateRings(t);
		UpdateSparks(t);
	}

	private void BuildCore()
	{
		_coreMaterial = CreateMaterial(CoreColor, EmissionEnergy);
		_core = new MeshInstance3D
		{
			Name = "MuzzleCoreFlash",
			Mesh = new SphereMesh
			{
				Radius = Radius,
				Height = Radius * 2.0f,
				RadialSegments = 16,
				Rings = 8,
			},
			MaterialOverride = _coreMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};

		AddChild(_core);
	}

	private void BuildRings()
	{
		for (int i = 0; i < 2; i++)
		{
			var material = CreateMaterial(i == 0 ? CoreColor : AccentColor, EmissionEnergy * 0.9f);
			var ring = new MeshInstance3D
			{
				Name = "MuzzleFlashRing",
				Mesh = new TorusMesh
				{
					InnerRadius = 0.86f,
					OuterRadius = 1.02f,
					Rings = 40,
					RingSegments = 8,
				},
				MaterialOverride = material,
				RotationDegrees = new Vector3(0.0f, 0.0f, i * 90.0f),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};

			AddChild(ring);
			_rings.Add(ring);
			_ringMaterials.Add(material);
		}
	}

	private void BuildSparks()
	{
		var random = new RandomNumberGenerator
		{
			Seed = 25057,
		};

		ArrayMesh mesh = CreateSparkMesh();

		for (int i = 0; i < SparkCount; i++)
		{
			float angle = i / (float)SparkCount * Tau + random.RandfRange(-0.16f, 0.16f);
			Vector3 side = new(Mathf.Cos(angle), Mathf.Sin(angle), 0.0f);
			var material = CreateMaterial(i % 2 == 0 ? CoreColor : AccentColor, EmissionEnergy * 1.15f);
			var spark = new MeshInstance3D
			{
				Name = "MuzzleSpark",
				Mesh = mesh,
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};

			AddChild(spark);
			_sparks.Add(new MuzzleSpark(
				spark,
				material,
				side,
				random.RandfRange(0.45f, 1.1f),
				random.RandfRange(0.65f, 1.25f)));
		}
	}

	private void UpdateCore(float t)
	{
		float pulse = 1.0f - EaseInCubic(t);
		_core.Scale = Vector3.One * Mathf.Lerp(1.35f, 0.15f, t);
		SetAlpha(_coreMaterial, CoreColor, CoreColor.A * pulse);
	}

	private void UpdateRings(float t)
	{
		for (int i = 0; i < _rings.Count; i++)
		{
			float localT = Mathf.Clamp((t - i * 0.12f) / 0.78f, 0.0f, 1.0f);
			float radius = Mathf.Lerp(Radius * 0.4f, Radius * 2.25f, EaseOutCubic(localT));
			_rings[i].Scale = Vector3.One * radius;
			SetAlpha(_ringMaterials[i], i == 0 ? CoreColor : AccentColor, (i == 0 ? CoreColor.A : AccentColor.A) * (1.0f - localT));
		}
	}

	private void UpdateSparks(float t)
	{
		foreach (MuzzleSpark spark in _sparks)
		{
			float distance = Mathf.Lerp(0.05f, Radius * 2.6f * spark.Speed, EaseOutCubic(t));
			Vector3 direction = (spark.Side * 0.72f + Vector3.Forward * 0.95f).Normalized();
			Vector3 position = direction * distance;
			Vector3 tangent = direction.Normalized();
			Vector3 right = tangent.Cross(Vector3.Up).Normalized();
			if (right.LengthSquared() < 0.001f)
			{
				right = Vector3.Right;
			}

			spark.Node.Transform = new Transform3D(
				new Basis(right * 0.05f * spark.Size, Vector3.Up * 0.05f * spark.Size, tangent * 0.56f * spark.Size),
				position);
			SetAlpha(spark.Material, AccentColor, AccentColor.A * (1.0f - EaseInCubic(t)));
		}
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

	private static ArrayMesh CreateSparkMesh()
	{
		Vector3[] vertices =
		[
			new(0.0f, 0.0f, 0.75f),
			new(-0.45f, 0.0f, -0.15f),
			new(0.0f, 0.28f, -0.35f),
			new(0.45f, 0.0f, -0.15f),
			new(0.0f, -0.28f, -0.35f),
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

	private sealed record MuzzleSpark(MeshInstance3D Node, StandardMaterial3D Material, Vector3 Side, float Speed, float Size);
}
