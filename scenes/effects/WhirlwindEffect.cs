using Godot;
using System.Collections.Generic;

/// <summary>
/// Builds a short-lived funnel tornado from authored mesh streaks that orbit upward around the actor.
/// </summary>
public partial class WhirlwindEffect : TimedEffect
{
	private const float Tau = Mathf.Pi * 2.0f;

	[Export] public int WindStreakCount { get; set; } = 118;
	[Export] public int DebrisCount { get; set; } = 34;
	[Export] public float Height { get; set; } = 3.6f;
	[Export] public float BottomRadius { get; set; } = 0.28f;
	[Export] public float TopRadius { get; set; } = 2.35f;
	[Export] public float RiseSpeed { get; set; } = 2.0f;
	[Export] public float RotationSpeedDegrees { get; set; } = 920.0f;
	[Export] public float SpiralTurns { get; set; } = 1.35f;
	[Export] public float UpdateInterval { get; set; } = 0.025f;

	private readonly List<FunnelPiece> _windPieces = [];
	private readonly List<FunnelPiece> _debrisPieces = [];
	private MultiMeshInstance3D _windMeshInstance;
	private MultiMeshInstance3D _debrisMeshInstance;
	private float _age;
	private float _updateCooldown;

	public override void _Ready()
	{
		base._Ready();
		BuildFunnel();
	}

	public override void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_updateCooldown = 0.0f;
		base.OnSpawnedFromPool();
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

		for (int i = 0; i < _windPieces.Count; i++)
		{
			_windMeshInstance.Multimesh.SetInstanceTransform(i, GetPieceTransform(_windPieces[i]));
		}

		for (int i = 0; i < _debrisPieces.Count; i++)
		{
			_debrisMeshInstance.Multimesh.SetInstanceTransform(i, GetPieceTransform(_debrisPieces[i]));
		}
	}

	private void BuildFunnel()
	{
		var random = new RandomNumberGenerator
		{
			Seed = 48117,
		};

		var windMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.72f, 0.96f, 1.0f, 0.58f),
			EmissionEnabled = true,
			Emission = new Color(0.52f, 0.9f, 1.0f),
			EmissionEnergyMultiplier = 3.6f,
		};

		var debrisMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.42f, 0.3f, 0.16f),
			Roughness = 0.9f,
		};

		var streakMesh = new QuadMesh
		{
			Size = Vector2.One,
		};

		var debrisMesh = new BoxMesh
		{
			Size = Vector3.One,
		};

		_windMeshInstance = CreateMultiMeshInstance("FunnelWindStreaks", streakMesh, windMaterial, WindStreakCount);
		_debrisMeshInstance = CreateMultiMeshInstance("FunnelDebris", debrisMesh, debrisMaterial, DebrisCount);

		for (int i = 0; i < WindStreakCount; i++)
		{
			_windPieces.Add(new FunnelPiece(
				random.RandfRange(0.0f, Tau),
				random.RandfRange(0.0f, Height),
				random.RandfRange(0.88f, 1.12f),
				random.RandfRange(0.85f, 1.18f),
				random.RandfRange(0.75f, 1.25f),
				random.RandfRange(0.75f, 1.35f),
				false));
		}

		for (int i = 0; i < DebrisCount; i++)
		{
			_debrisPieces.Add(new FunnelPiece(
				random.RandfRange(0.0f, Tau),
				random.RandfRange(0.0f, Height),
				random.RandfRange(0.9f, 1.2f),
				random.RandfRange(0.75f, 1.1f),
				random.RandfRange(0.55f, 1.05f),
				random.RandfRange(0.65f, 1.15f),
				true));
		}
	}

	private MultiMeshInstance3D CreateMultiMeshInstance(string nodeName, Mesh mesh, Material material, int instanceCount)
	{
		var multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = mesh,
			InstanceCount = instanceCount,
		};

		var node = new MultiMeshInstance3D
		{
			Name = nodeName,
			Multimesh = multiMesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};

		AddChild(node);
		return node;
	}

	private Transform3D GetPieceTransform(FunnelPiece piece)
	{
		float normalizedHeight = PositiveModulo(piece.HeightOffset + _age * RiseSpeed * piece.RiseMultiplier, Height) / Height;
		float radiusProgress = Mathf.Pow(normalizedHeight, 1.45f);
		float radius = Mathf.Lerp(BottomRadius, TopRadius, radiusProgress) * piece.RadiusMultiplier;
		float angle = piece.Phase
			+ Mathf.DegToRad(RotationSpeedDegrees * piece.RotationMultiplier) * _age
			+ normalizedHeight * SpiralTurns * Tau;
		float verticalWobble = Mathf.Sin(_age * 11.0f + piece.Phase) * 0.04f;

		Vector3 position = new(
			Mathf.Cos(angle) * radius,
			normalizedHeight * Height + verticalWobble,
			Mathf.Sin(angle) * radius);

		if (piece.IsDebris)
		{
			float scale = Mathf.Lerp(0.08f, 0.18f, radiusProgress) * piece.SizeMultiplier;
			Basis basis = Basis.FromEuler(new Vector3(angle * 0.7f, angle * 1.5f, angle)).Scaled(Vector3.One * scale);
			return new Transform3D(basis, position);
		}

		Vector3 tangent = new(-Mathf.Sin(angle), 0.32f, Mathf.Cos(angle));
		tangent = tangent.Normalized();
		Vector3 up = (Vector3.Up - tangent * Vector3.Up.Dot(tangent)).Normalized();
		Vector3 normal = tangent.Cross(up).Normalized();
		float length = Mathf.Lerp(0.42f, 1.35f, radiusProgress) * piece.SizeMultiplier;
		float thickness = Mathf.Lerp(0.07f, 0.16f, radiusProgress) * piece.SizeMultiplier;

		return new Transform3D(
			new Basis(tangent * length, up * thickness, normal * thickness),
			position);
	}

	private static float PositiveModulo(float value, float divisor)
	{
		float result = value % divisor;
		return result < 0.0f ? result + divisor : result;
	}

	private sealed record FunnelPiece(
		float Phase,
		float HeightOffset,
		float RadiusMultiplier,
		float RotationMultiplier,
		float RiseMultiplier,
		float SizeMultiplier,
		bool IsDebris)
;
}
