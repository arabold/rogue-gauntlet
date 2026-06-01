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
	private readonly List<IceShard> _shards = [];
	private MeshInstance3D _frostDisk;
	private OmniLight3D _light;
	private StandardMaterial3D _iceMaterial;
	private StandardMaterial3D _coreIceMaterial;
	private StandardMaterial3D _frostMaterial;
	private StandardMaterial3D _shardMaterial;
	private float _age;
	private float _updateCooldown;
	private bool _shattered;

	public override void _Ready()
	{
		BuildMaterials();
		BuildFrostDisk();
		BuildSpikes();
		BuildShards();
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
		foreach (IceShard shard in _shards)
		{
			shard.Node.Visible = false;
		}
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
		UpdateSpikes();
		UpdateFrostDisk();
		UpdateShards();

		if (!_shattered && _age >= GrowDuration + HoldDuration)
		{
			_shattered = true;
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

	private void BuildShards()
	{
		var random = new RandomNumberGenerator
		{
			Seed = 12873,
		};

		for (int i = 0; i < ShardCount; i++)
		{
			float angle = i / (float)ShardCount * Tau + random.RandfRange(-0.12f, 0.12f);
			Vector3 horizontal = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)).Normalized();
			var shard = new MeshInstance3D
			{
				Name = "IceBurstShard",
				Mesh = new PrismMesh
				{
					Size = new Vector3(random.RandfRange(0.04f, 0.1f), random.RandfRange(0.18f, 0.42f), random.RandfRange(0.04f, 0.1f)),
				},
				MaterialOverride = _shardMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Visible = false,
			};

			AddChild(shard);
			_shards.Add(new IceShard(
				shard,
				horizontal * random.RandfRange(2.2f, 5.2f) + Vector3.Up * random.RandfRange(1.3f, 3.7f),
				new Vector3(random.RandfRange(240.0f, 720.0f), random.RandfRange(360.0f, 900.0f), random.RandfRange(240.0f, 720.0f)),
				random.RandfRange(0.65f, 1.35f)));
		}
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

		foreach (IceShard shard in _shards)
		{
			shard.Node.Visible = false;
			shard.Node.Position = Vector3.Zero;
			shard.Node.Scale = Vector3.One;
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

	private void UpdateShards()
	{
		if (!_shattered)
		{
			return;
		}

		float t = Mathf.Clamp((_age - GrowDuration - HoldDuration) / 0.55f, 0.0f, 1.0f);
		foreach (IceShard shard in _shards)
		{
			shard.Node.Visible = true;
			shard.Node.Position = shard.Velocity * t + Vector3.Down * 3.2f * t * t;
			shard.Node.RotationDegrees = shard.RotationDegreesPerSecond * t;
			float scale = (1.0f - t) * shard.ScaleMultiplier;
			shard.Node.Scale = Vector3.One * Mathf.Max(0.0f, scale);
		}
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

	private sealed record IceShard(
		MeshInstance3D Node,
		Vector3 Velocity,
		Vector3 RotationDegreesPerSecond,
		float ScaleMultiplier);
}
