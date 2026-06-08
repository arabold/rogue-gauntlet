using Godot;
/// <summary>
/// Short configurable muzzle flash with a core orb, expanding rings, and GPU-driven sparks.
/// </summary>
public partial class MuzzleBurstEffect : Node3D, IPooledNode
{
	[Export] public float Lifetime { get; set; } = 0.38f;
	[Export] public Color CoreColor { get; set; } = new(1.0f, 0.8f, 0.25f, 0.82f);
	[Export] public Color AccentColor { get; set; } = new(1.0f, 0.25f, 0.04f, 0.65f);
	[Export] public float EmissionEnergy { get; set; } = 3.0f;
	[Export] public int SparkCount { get; set; } = 24;
	[Export] public float Radius { get; set; } = 0.42f;
	[Export] public float UpdateInterval { get; set; } = 0.025f;

	private readonly System.Collections.Generic.List<MeshInstance3D> _rings = [];
	private readonly System.Collections.Generic.List<StandardMaterial3D> _ringMaterials = [];
	private MeshInstance3D _core;
	private StandardMaterial3D _coreMaterial;
	private GpuParticles3D _sparks;
	private ParticleProcessMaterial _sparkProcessMaterial;
	private float _age;
	private Cooldown _updateCooldown;
	private bool _isActive;

	public override void _Ready()
	{
		BuildCore();
		BuildRings();
		BuildSparks();
		RestartSparks();
		StartLifetime();
	}

	public void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_updateCooldown.Start(0.0f);
		_isActive = true;
		RestartSparks();
	}

	public void OnDespawnedToPool()
	{
		_isActive = false;
		if (_sparks != null)
		{
			_sparks.Emitting = false;
		}
	}

	private void StartLifetime()
	{
		_isActive = true;
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		if (_updateCooldown.Tick(delta))
		{
			_updateCooldown.Start(UpdateInterval);
			float t = Mathf.Clamp(_age / Lifetime, 0.0f, 1.0f);
			UpdateCore(t);
			UpdateRings(t);
		}

		if (_age >= Lifetime)
		{
			if (ScenePool.IsTracked(this))
				ScenePool.Despawn(this);
			else
				QueueFree();
		}
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
		_sparkProcessMaterial = new ParticleProcessMaterial
		{
			Direction = Vector3.Forward,
			Spread = 58.0f,
			InitialVelocityMin = Radius * 4.0f,
			InitialVelocityMax = Radius * 8.5f,
			Gravity = Vector3.Zero,
			ScaleMin = 0.08f,
			ScaleMax = 0.2f,
			ColorRamp = CreateSparkColorRamp(),
		};

		_sparks = new GpuParticles3D
		{
			Name = "MuzzleSparks",
			Amount = Mathf.Max(1, SparkCount),
			Lifetime = Mathf.Max(0.05f, Lifetime * 0.74f),
			OneShot = true,
			Explosiveness = 0.92f,
			Randomness = 0.85f,
			LocalCoords = true,
			ProcessMaterial = _sparkProcessMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_sparks.SetDrawPassMesh(0, CreateSparkMesh());
		AddChild(_sparks);
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

	private void RestartSparks()
	{
		if (_sparks == null)
		{
			return;
		}

		_sparks.Amount = Mathf.Max(1, SparkCount);
		_sparks.Lifetime = Mathf.Max(0.05f, Lifetime * 0.74f);
		_sparks.Emitting = false;
		_sparks.Restart();
		_sparks.Emitting = true;
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

	private GradientTexture1D CreateSparkColorRamp()
	{
		var gradient = new Gradient
		{
			Offsets = [0.0f, 0.42f, 1.0f],
			Colors =
			[
				new Color(AccentColor.R, AccentColor.G, AccentColor.B, AccentColor.A),
				new Color(CoreColor.R, CoreColor.G, CoreColor.B, CoreColor.A * 0.75f),
				new Color(AccentColor.R, AccentColor.G, AccentColor.B, 0.0f),
			],
		};

		return new GradientTexture1D
		{
			Gradient = gradient,
		};
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
}
