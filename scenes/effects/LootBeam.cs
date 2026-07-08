using Godot;

/// <summary>
/// The pickup cue for dropped loot: a radial floor glow and rising sparks — both additive so the
/// world's glow blooms them. Everything is tinted to a single <see cref="BeamColor"/> (resolved
/// from item rarity/type by <see cref="LootVisuals"/>). The floor circle identifies the item and
/// the sparks add life; the item's own model provides the central visual.
///
/// Built entirely in code so each instance owns its own materials (per-item color) without
/// resource-local-to-scene bookkeeping.
/// </summary>
[GlobalClass]
public partial class LootBeam : Node3D
{
	private static Shader _glowShader;
	private static Shader _additiveShader;
	private static Shader GlowShader => _glowShader ??= GD.Load<Shader>("res://scenes/effects/shaders/loot_glow.gdshader");
	private static Shader AdditiveShader => _additiveShader ??= GD.Load<Shader>("res://scenes/effects/shaders/loot_additive.gdshader");

	/// <summary>Tint applied to every part of the beam.</summary>
	[Export]
	public Color BeamColor
	{
		get;
		set
		{
			field = value;
			ApplyColor();
		}
	} = Colors.White;

	/// <summary>Adds a small real-time light so the beam spills onto nearby geometry.</summary>
	[Export] public bool EmitLight { get; set; } = true;

	private ShaderMaterial _glowMat;
	private ShaderMaterial _sparkMat;
	private OmniLight3D _light;

	public override void _Ready()
	{
		BuildGlow();
		BuildSparks();
		if (EmitLight)
		{
			BuildLight();
		}

		ApplyColor();
	}

	private void BuildGlow()
	{
		_glowMat = new ShaderMaterial { Shader = GlowShader };
		var glow = new MeshInstance3D
		{
			Name = "Glow",
			Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f) },
			MaterialOverride = _glowMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			RotationDegrees = new Vector3(-90f, 0f, 0f), // lay the quad flat on the floor
			Position = new Vector3(0f, 0.03f, 0f),
		};
		AddChild(glow);
	}

	private void BuildSparks()
	{
		_sparkMat = new ShaderMaterial { Shader = AdditiveShader };

		var process = new ParticleProcessMaterial
		{
			Direction = new Vector3(0f, 1f, 0f),
			Spread = 12f,
			InitialVelocityMin = 1.3f,
			InitialVelocityMax = 2.4f,
			Gravity = new Vector3(0f, 0.6f, 0f), // upward acceleration so sparks stream up and speed away
			ScaleMin = 0.6f,
			ScaleMax = 1.2f,
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(0.16f, 0.02f, 0.16f),
		};

		var sparks = new GpuParticles3D
		{
			Name = "Sparks",
			Amount = 26,
			Lifetime = 1.8,
			ProcessMaterial = process,
			DrawPass1 = new SphereMesh { Radius = 0.055f, Height = 0.11f, RadialSegments = 6, Rings = 3 },
			MaterialOverride = _sparkMat,
			Explosiveness = 0f,
			Emitting = true,
		};
		AddChild(sparks);
	}

	private void BuildLight()
	{
		_light = new OmniLight3D
		{
			Name = "Light",
			OmniRange = 3.0f,
			LightEnergy = 0.8f,
			ShadowEnabled = false,
			Position = new Vector3(0f, 0.6f, 0f),
		};
		AddChild(_light);
	}

	private void ApplyColor()
	{
		Vector3 rgb = new(BeamColor.R, BeamColor.G, BeamColor.B);
		_glowMat?.SetShaderParameter("beam_color", rgb);
		_sparkMat?.SetShaderParameter("color", rgb);
		if (_light != null)
		{
			_light.LightColor = BeamColor;
		}
	}
}
