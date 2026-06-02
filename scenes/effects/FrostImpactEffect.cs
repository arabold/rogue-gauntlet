using Godot;
using System.Collections.Generic;

/// <summary>
/// Creates a staged ice-bloom impact: jagged crystals erupt, frost spreads, then shards burst out.
/// </summary>
public partial class FrostImpactEffect : TimedEffect
{
	private const float Tau = Mathf.Pi * 2.0f;

	[Export] public float GrowDuration { get; set; } = 0.34f;
	[Export] public float HoldDuration { get; set; } = 0.32f;
	[Export] public float ShatterDuration { get; set; } = 0.18f;
	[Export] public int SpikeCount { get; set; } = 18;
	[Export] public int ShardCount { get; set; } = 46;
	[Export] public float UpdateInterval { get; set; } = 0.025f;

	private readonly List<IceSpike> _spikes = [];
	private MeshInstance3D _frostDisk;
	private OmniLight3D _light;
	private StandardMaterial3D _iceMaterial;
	private StandardMaterial3D _coreIceMaterial;
	private StandardMaterial3D _frostMaterial;
	private StandardMaterial3D _shardMaterial;
	private GpuParticles3D _shardEmitter;
	private ParticleProcessMaterial _shardProcessMaterial;
	private float _age;
	private float _updateCooldown;
	private bool _shattered;

	public override void _Ready()
	{
		BuildMaterials();
		BuildFrostDisk();
		BuildSpikes();
		BuildShardEmitter();
		_light = GetNodeOrNull<OmniLight3D>("IceLight");
		base._Ready();
	}

	public override void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_updateCooldown = 0.0f;
		_shattered = false;
		ResetVisuals();
		base.OnSpawnedFromPool();
	}

	public override void OnDespawnedToPool()
	{
		base.OnDespawnedToPool();
		if (_shardEmitter != null)
		{
			_shardEmitter.Emitting = false;
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		_age += (float)delta;
		_updateCooldown -= (float)delta;
		if (_updateCooldown > 0.0f)
		{
			return;
		}

		_updateCooldown = UpdateInterval;
		UpdateSpikes();
		UpdateFrostDisk();

		if (!_shattered && _age >= GrowDuration + HoldDuration)
		{
			_shattered = true;
			BurstShards();
		}
	}

	private void BuildMaterials()
	{
		_coreIceMaterial = CreateIceMaterial(new Color(0.72f, 0.98f, 1.0f, 0.72f), new Color(0.35f, 0.9f, 1.0f), 3.0f);
		_iceMaterial = CreateIceMaterial(new Color(0.42f, 0.9f, 1.0f, 0.58f), new Color(0.16f, 0.74f, 1.0f), 2.1f);
		_frostMaterial = CreateIceMaterial(new Color(0.72f, 0.98f, 1.0f, 0.2f), new Color(0.18f, 0.62f, 1.0f), 1.25f);
		_shardMaterial = CreateIceMaterial(new Color(0.86f, 1.0f, 1.0f, 0.7f), new Color(0.42f, 0.92f, 1.0f), 2.5f);
	}

	private static StandardMaterial3D CreateIceMaterial(Color albedo, Color emission, float emissionEnergy)
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = albedo,
			Roughness = 0.08f,
			Metallic = 0.0f,
			EmissionEnabled = true,
			Emission = emission,
			EmissionEnergyMultiplier = emissionEnergy,
		};
	}

	private void BuildFrostDisk()
	{
		_frostDisk = new MeshInstance3D
		{
			Name = "FrostShockDisk",
			Mesh = new CylinderMesh
			{
				TopRadius = 1.0f,
				BottomRadius = 1.0f,
				Height = 0.025f,
				RadialSegments = 48,
			},
			MaterialOverride = _frostMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Scale = new Vector3(0.1f, 1.0f, 0.1f),
		};

		AddChild(_frostDisk);
	}

	private void BuildSpikes()
	{
		var random = new RandomNumberGenerator
		{
			Seed = 88421,
		};

		for (int i = 0; i < SpikeCount; i++)
		{
			float ring = i == 0 ? 0.0f : random.RandfRange(0.35f, 1.35f);
			float angle = i == 0 ? 0.0f : i / (float)(SpikeCount - 1) * Tau + random.RandfRange(-0.18f, 0.18f);
			float height = i == 0 ? 2.65f : random.RandfRange(0.55f, 1.8f) * Mathf.Lerp(1.0f, 0.7f, ring / 1.35f);
			float width = i == 0 ? 0.44f : random.RandfRange(0.12f, 0.34f);
			Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
			Vector3 position = direction * ring;

			var spike = new MeshInstance3D
			{
				Name = i == 0 ? "IceBloomCore" : "IceBloomSpike",
				Mesh = new PrismMesh
				{
					Size = new Vector3(width, height, width * random.RandfRange(0.75f, 1.35f)),
				},
				MaterialOverride = i == 0 ? _coreIceMaterial : _iceMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Scale = new Vector3(0.05f, 0.02f, 0.05f),
			};

			AddChild(spike);
			_spikes.Add(new IceSpike(
				spike,
				position,
				new Vector3(random.RandfRange(-18.0f, 18.0f), Mathf.RadToDeg(angle), random.RandfRange(-22.0f, 22.0f)),
				new Vector3(1.0f, 1.0f, 1.0f),
				random.RandfRange(0.0f, 0.16f),
				random.RandfRange(0.85f, 1.18f)));
		}
	}

	private void BuildShardEmitter()
	{
		_shardProcessMaterial = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.36f,
			Spread = 180.0f,
			InitialVelocityMin = 2.2f,
			InitialVelocityMax = 6.0f,
			Gravity = new Vector3(0.0f, -4.8f, 0.0f),
			ScaleMin = 0.55f,
			ScaleMax = 1.45f,
			ColorRamp = CreateShardColorRamp(),
		};

		_shardEmitter = new GpuParticles3D
		{
			Name = "IceShardBurst",
			Amount = Mathf.Max(1, ShardCount),
			Lifetime = 0.56f,
			OneShot = true,
			Explosiveness = 0.96f,
			Randomness = 1.0f,
			LocalCoords = true,
			ProcessMaterial = _shardProcessMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_shardEmitter.SetDrawPassMesh(0, CreateShardMesh());
		AddChild(_shardEmitter);
		_shardEmitter.Emitting = false;
	}

	private void UpdateSpikes()
	{
		foreach (IceSpike spike in _spikes)
		{
			float growT = Mathf.Clamp((_age - spike.Delay) / (GrowDuration * spike.SpeedMultiplier), 0.0f, 1.0f);
			float grow = EaseOutBack(growT);
			float shatterT = _shattered ? Mathf.Clamp((_age - GrowDuration - HoldDuration) / ShatterDuration, 0.0f, 1.0f) : 0.0f;
			float visible = 1.0f - shatterT;

			spike.Node.Position = spike.Position + Vector3.Up * Mathf.Lerp(-0.35f, 0.0f, grow);
			spike.Node.RotationDegrees = spike.RotationDegrees;
			spike.Node.Scale = new Vector3(
				Mathf.Lerp(0.08f, spike.TargetScale.X, grow) * visible,
				Mathf.Lerp(0.02f, spike.TargetScale.Y, grow) * visible,
				Mathf.Lerp(0.08f, spike.TargetScale.Z, grow) * visible);
		}
	}

	private void ResetVisuals()
	{
		foreach (IceSpike spike in _spikes)
		{
			spike.Node.Visible = true;
			spike.Node.Scale = new Vector3(0.05f, 0.02f, 0.05f);
		}

		if (_shardEmitter != null)
		{
			_shardEmitter.Emitting = false;
		}
	}

	private void UpdateFrostDisk()
	{
		float grow = EaseOutCubic(Mathf.Clamp(_age / 0.28f, 0.0f, 1.0f));
		float fade = _shattered ? 1.0f - Mathf.Clamp((_age - GrowDuration - HoldDuration) / 0.45f, 0.0f, 1.0f) : 1.0f;
		_frostDisk.Scale = new Vector3(2.2f * grow * fade, 1.0f, 2.2f * grow * fade);

		if (_light != null)
		{
			_light.LightEnergy = Mathf.Lerp(4.2f, 1.2f, Mathf.Clamp(_age / Lifetime, 0.0f, 1.0f));
		}
	}

	private void BurstShards()
	{
		if (_shardEmitter == null)
		{
			return;
		}

		_shardEmitter.Amount = Mathf.Max(1, ShardCount);
		_shardEmitter.Emitting = false;
		_shardEmitter.Restart();
		_shardEmitter.Emitting = true;
	}

	private Mesh CreateShardMesh()
	{
		return new PrismMesh
		{
			Material = _shardMaterial,
			Size = new Vector3(0.075f, 0.3f, 0.075f),
		};
	}

	private GradientTexture1D CreateShardColorRamp()
	{
		var gradient = new Gradient
		{
			Offsets = [0.0f, 0.48f, 1.0f],
			Colors =
			[
				new Color(0.9f, 1.0f, 1.0f, 0.78f),
				new Color(0.5f, 0.94f, 1.0f, 0.55f),
				new Color(0.5f, 0.94f, 1.0f, 0.0f),
			],
		};

		return new GradientTexture1D
		{
			Gradient = gradient,
		};
	}

	private static float EaseOutCubic(float t)
	{
		return 1.0f - Mathf.Pow(1.0f - t, 3.0f);
	}

	private static float EaseOutBack(float t)
	{
		const float overshoot = 1.70158f;
		return 1.0f + (overshoot + 1.0f) * Mathf.Pow(t - 1.0f, 3.0f) + overshoot * Mathf.Pow(t - 1.0f, 2.0f);
	}

	private sealed record IceSpike(
		MeshInstance3D Node,
		Vector3 Position,
		Vector3 RotationDegrees,
		Vector3 TargetScale,
		float Delay,
		float SpeedMultiplier);

}
