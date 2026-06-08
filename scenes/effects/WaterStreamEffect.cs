using Godot;
using System.Collections.Generic;

/// <summary>
/// Builds layered hose-like water ribbons and falling droplets around a moving projectile.
/// </summary>
public partial class WaterStreamEffect : Node3D, IPooledNode
{
	private const float Tau = Mathf.Pi * 2.0f;

	[Export] public Node3D Target { get; set; }
	[Export] public int StrandCount { get; set; } = 5;
	[Export] public int MaxPoints { get; set; } = 22;
	[Export] public float PointSpacing { get; set; } = 0.08f;
	[Export] public float CoreWidth { get; set; } = 0.42f;
	[Export] public float SprayRadius { get; set; } = 0.32f;
	[Export] public int DropletCount { get; set; } = 34;
	[Export] public float DropletTrailLength { get; set; } = 3.8f;
	[Export] public float MeshUpdateInterval { get; set; } = 0.033f;
	[Export] public float DropletUpdateInterval { get; set; } = 0.033f;

	private readonly List<Vector3> _points = [];
	private readonly List<MeshInstance3D> _strands = [];
	private StandardMaterial3D _coreMaterial;
	private StandardMaterial3D _sprayMaterial;
	private StandardMaterial3D _dropletMaterial;
	private GpuParticles3D _dropletEmitter;
	private ParticleProcessMaterial _dropletProcessMaterial;
	private float _age;
	private Cooldown _meshUpdateCooldown;
	private Cooldown _dropletUpdateCooldown;
	private bool _meshDirty;

	public override void _Ready()
	{
		Target ??= GetParent<Node3D>();
		TopLevel = true;
		BuildMaterials();
		BuildStrands();
		BuildDropletEmitter();
	}

	public void OnSpawnedFromPool()
	{
		_age = 0.0f;
		_meshUpdateCooldown.Start(0.0f);
		_dropletUpdateCooldown.Start(0.0f);
		_meshDirty = true;
		_points.Clear();
		Target ??= GetParent<Node3D>();
		foreach (MeshInstance3D strand in _strands)
		{
			strand.Mesh = null;
		}
		RestartDropletEmitter();
	}

	public void OnDespawnedToPool()
	{
		_points.Clear();
		foreach (MeshInstance3D strand in _strands)
		{
			strand.Mesh = null;
		}
		if (_dropletEmitter != null)
		{
			_dropletEmitter.Emitting = false;
		}
	}

	public override void _Process(double delta)
	{
		if (Target == null || !GodotObject.IsInstanceValid(Target))
		{
			QueueFree();
			return;
		}

		_age += (float)delta;
		Vector3 position = Target.GlobalPosition;
		if (_points.Count == 0 || _points[0].DistanceTo(position) >= PointSpacing)
		{
			_points.Insert(0, position);
			_meshDirty = true;
			if (_points.Count > MaxPoints)
			{
				_points.RemoveAt(_points.Count - 1);
			}
		}

		bool meshReady = _meshUpdateCooldown.Tick(delta);
		if (_meshDirty && meshReady)
		{
			BuildMeshes();
			_meshDirty = false;
			_meshUpdateCooldown.Start(MeshUpdateInterval);
		}

		if (_dropletUpdateCooldown.Tick(delta))
		{
			UpdateDroplets();
			_dropletUpdateCooldown.Start(DropletUpdateInterval);
		}
	}

	private void BuildMaterials()
	{
		_coreMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.62f, 0.94f, 1.0f, 0.55f),
			EmissionEnabled = true,
			Emission = new Color(0.38f, 0.86f, 1.0f),
			EmissionEnergyMultiplier = 2.8f,
		};

		_sprayMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.78f, 0.98f, 1.0f, 0.34f),
			EmissionEnabled = true,
			Emission = new Color(0.54f, 0.9f, 1.0f),
			EmissionEnergyMultiplier = 1.9f,
		};

		_dropletMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.76f, 0.98f, 1.0f, 0.72f),
			EmissionEnabled = true,
			Emission = new Color(0.45f, 0.88f, 1.0f),
			EmissionEnergyMultiplier = 2.4f,
		};

	}

	private void BuildStrands()
	{
		for (int i = 0; i < StrandCount; i++)
		{
			var strand = new MeshInstance3D
			{
				Name = i == 0 ? "WaterCoreStream" : "WaterSprayStrand",
				MaterialOverride = i == 0 ? _coreMaterial : _sprayMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};

			AddChild(strand);
			_strands.Add(strand);
		}
	}

	private void BuildDropletEmitter()
	{
		_dropletProcessMaterial = new ParticleProcessMaterial
		{
			Direction = Vector3.Back,
			Spread = 34.0f,
			InitialVelocityMin = 2.0f,
			InitialVelocityMax = 6.2f,
			Gravity = new Vector3(0.0f, -2.6f, 0.0f),
			ScaleMin = 0.28f,
			ScaleMax = 0.95f,
			ColorRamp = CreateDropletColorRamp(),
		};

		_dropletEmitter = new GpuParticles3D
		{
			Name = "WaterDropletSpray",
			Amount = Mathf.Max(1, DropletCount),
			Lifetime = Mathf.Max(0.12f, DropletTrailLength / 8.0f),
			Randomness = 0.9f,
			LocalCoords = false,
			ProcessMaterial = _dropletProcessMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_dropletEmitter.SetDrawPassMesh(0, CreateDropletMesh());
		AddChild(_dropletEmitter);
	}

	private void BuildMeshes()
	{
		if (_points.Count < 2)
		{
			return;
		}

		Vector3 cameraRight = GetViewport().GetCamera3D()?.GlobalTransform.Basis.X.Normalized() ?? Vector3.Right;
		for (int strandIndex = 0; strandIndex < _strands.Count; strandIndex++)
		{
			BuildStrandMesh(_strands[strandIndex], strandIndex, cameraRight);
		}
	}

	private void BuildStrandMesh(MeshInstance3D strand, int strandIndex, Vector3 cameraRight)
	{
		int pointCount = _points.Count;
		var vertices = new Vector3[pointCount * 2];
		var uvs = new Vector2[pointCount * 2];
		var indices = new int[(pointCount - 1) * 6];
		float phase = strandIndex / (float)Mathf.Max(1, StrandCount - 1) * Tau;

		for (int i = 0; i < pointCount; i++)
		{
			float t = i / (float)(pointCount - 1);
			Vector3 point = _points[i];
			Vector3 forward = i == 0 ? (_points[0] - _points[1]).Normalized() : (_points[i - 1] - _points[i]).Normalized();
			Vector3 side = forward.Cross(Vector3.Up).Normalized();
			if (side.LengthSquared() < 0.001f)
			{
				side = Vector3.Right;
			}

			float swirl = Mathf.Sin(_age * 18.0f - t * 8.0f + phase);
			float lift = Mathf.Cos(_age * 13.0f - t * 7.0f + phase) * SprayRadius * 0.45f;
			float strandSpread = strandIndex == 0 ? 0.0f : SprayRadius * (0.35f + 0.2f * strandIndex);
			Vector3 offset = side * swirl * strandSpread + Vector3.Up * lift * (strandIndex == 0 ? 0.25f : 1.0f);
			float width = (strandIndex == 0 ? CoreWidth : CoreWidth * 0.42f) * Mathf.Lerp(1.0f, 0.12f, t);
			Vector3 widthOffset = cameraRight * width * 0.5f;

			vertices[i * 2] = point + offset - widthOffset;
			vertices[i * 2 + 1] = point + offset + widthOffset;
			uvs[i * 2] = new Vector2(t, 0.0f);
			uvs[i * 2 + 1] = new Vector2(t, 1.0f);
		}

		int index = 0;
		for (int i = 0; i < pointCount - 1; i++)
		{
			int a = i * 2;
			int b = a + 1;
			int c = a + 2;
			int d = a + 3;
			indices[index++] = a;
			indices[index++] = b;
			indices[index++] = c;
			indices[index++] = b;
			indices[index++] = d;
			indices[index++] = c;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		strand.Mesh = mesh;
	}

	private void UpdateDroplets()
	{
		if (_points.Count < 2 || _dropletEmitter == null)
		{
			return;
		}

		Vector3 forward = (_points[0] - _points[^1]).Normalized();
		_dropletEmitter.GlobalPosition = _points[0];
		if (forward.LengthSquared() > 0.001f)
		{
			_dropletEmitter.LookAt(_points[0] - forward, GetLookAtUpVector(forward));
		}
	}

	private void RestartDropletEmitter()
	{
		if (_dropletEmitter == null)
		{
			return;
		}

		_dropletEmitter.Amount = Mathf.Max(1, DropletCount);
		_dropletEmitter.Lifetime = Mathf.Max(0.12f, DropletTrailLength / 8.0f);
		_dropletEmitter.Emitting = false;
		_dropletEmitter.Restart();
		_dropletEmitter.Emitting = true;
	}

	private Mesh CreateDropletMesh()
	{
		return new SphereMesh
		{
			Material = _dropletMaterial,
			Radius = 0.055f,
			Height = 0.11f,
			RadialSegments = 8,
			Rings = 4,
		};
	}

	private GradientTexture1D CreateDropletColorRamp()
	{
		var gradient = new Gradient
		{
			Offsets = [0.0f, 0.38f, 1.0f],
			Colors =
			[
				new Color(0.82f, 1.0f, 1.0f, 0.78f),
				new Color(0.48f, 0.9f, 1.0f, 0.52f),
				new Color(0.32f, 0.72f, 1.0f, 0.0f),
			],
		};

		return new GradientTexture1D
		{
			Gradient = gradient,
		};
	}

	private static Vector3 GetLookAtUpVector(Vector3 direction)
	{
		return Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
	}
}
