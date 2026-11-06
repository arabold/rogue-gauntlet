using Godot;
using System.Collections.Generic;

/// <summary>Builds a camera-facing ribbon from automatic target samples or manual world positions.</summary>
[GlobalClass]
public partial class RibbonTrail : MeshInstance3D, IPooledNode
{
	[Export] public Node3D Target { get; set; }
	[Export(PropertyHint.Range, "2,256,1")] public int MaxPoints { get; set; } = 18;
	[Export(PropertyHint.Range, "0.01,1,0.01")] public float PointSpacing { get; set; } = 0.12f;
	[Export(PropertyHint.Range, "0.01,5,0.01")] public float Width { get; set; } = 0.75f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float TailWidthScale { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0,1,0.005")] public float MeshUpdateInterval { get; set; } = 0.033f;
	[Export(PropertyHint.Range, "0,10,0.05")] public float PointLifetime { get; set; }
	[Export(PropertyHint.Range, "0,100,0.1")] public float MaxLength { get; set; }

	private readonly List<TrailPoint> _points = new();
	private Cooldown _meshUpdateCooldown;
	private bool _collecting;
	private bool _meshDirty;

	private struct TrailPoint(Vector3 position)
	{
		public Vector3 Position = position;
		public float Age;
	}

	public override void _Ready()
	{
		Target ??= GetParentOrNull<Node3D>();
		_collecting = Target != null;
		TopLevel = true;
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		GlobalTransform = Transform3D.Identity;
		ResetPhysicsInterpolation();
		CastShadow = ShadowCastingSetting.Off;
		SetProcess(_collecting);
	}

	/// <summary>Starts a new path and discards any previous trail.</summary>
	public void Begin()
	{
		Clear();
		_collecting = true;
		SetProcess(true);
	}

	/// <summary>Adds a world-space sample while collection is active.</summary>
	public void Sample(Vector3 worldPosition)
	{
		if (!_collecting) return;
		if (_points.Count > 0 && _points[0].Position.DistanceTo(worldPosition) < Mathf.Max(0.001f, PointSpacing)) return;
		_points.Insert(0, new TrailPoint(worldPosition));
		TrimTail();
		_meshDirty = true;
	}

	/// <summary>Stops adding points and lets the current tail fade and retire.</summary>
	public void End()
	{
		_collecting = false;
		if (_points.Count == 0) SetProcess(false);
	}

	/// <summary>Immediately removes every sample and rendered triangle.</summary>
	public void Clear()
	{
		_points.Clear();
		Mesh = null;
		_meshDirty = false;
		_meshUpdateCooldown.Start(0);
	}

	/// <summary>Clears prior trail data and resumes automatic target sampling when a target exists.</summary>
	public void OnSpawnedFromPool()
	{
		Clear();
		Target ??= GetParentOrNull<Node3D>();
		_collecting = Target != null;
		SetProcess(_collecting);
		GlobalTransform = Transform3D.Identity;
		ResetPhysicsInterpolation();
	}

	/// <summary>Stops sampling and clears all trail geometry before reuse.</summary>
	public void OnDespawnedToPool()
	{
		End();
		Clear();
	}

	public override void _Process(double delta)
	{
		if (_collecting && Target != null)
		{
			if (GodotObject.IsInstanceValid(Target)) Sample(Target.GlobalPosition);
			else End();
		}

		AgePoints((float)delta);
		if (!_collecting && _points.Count == 0)
		{
			Mesh = null;
			SetProcess(false);
			return;
		}
		bool meshReady = MeshUpdateInterval <= 0 || _meshUpdateCooldown.Tick(delta);
		if (!_meshDirty || !meshReady) return;
		BuildMesh();
		_meshDirty = false;
		_meshUpdateCooldown.Start(MeshUpdateInterval);
	}

	public override void _ExitTree()
	{
		End();
		Clear();
	}

	private void AgePoints(float delta)
	{
		if (_points.Count == 0) return;
		for (int i = 0; i < _points.Count; i++)
		{
			TrailPoint point = _points[i];
			point.Age += delta;
			_points[i] = point;
		}
		if (PointLifetime > 0)
			while (_points.Count > 0 && _points[^1].Age >= PointLifetime) _points.RemoveAt(_points.Count - 1);
		TrimTail();
		_meshDirty = true;
	}

	private void TrimTail()
	{
		while (_points.Count > Mathf.Max(2, MaxPoints)) _points.RemoveAt(_points.Count - 1);
		if (MaxLength <= 0) return;
		float length = 0;
		for (int i = 1; i < _points.Count; i++)
		{
			length += _points[i - 1].Position.DistanceTo(_points[i].Position);
			if (length > MaxLength)
			{
				_points.RemoveRange(i, _points.Count - i);
				break;
			}
		}
	}

	private void BuildMesh()
	{
		if (_points.Count < 2)
		{
			Mesh = null;
			return;
		}

		var vertices = new Vector3[_points.Count * 2];
		var uvs = new Vector2[_points.Count * 2];
		var colors = new Color[_points.Count * 2];
		var indices = new int[(_points.Count - 1) * 6];
		Camera3D camera = GetViewport().GetCamera3D();
		Vector3 cameraRight = camera?.GlobalTransform.Basis.X.Normalized() ?? Vector3.Right;
		Vector3 cameraPosition = camera?.GlobalPosition ?? Vector3.Zero;

		for (int i = 0; i < _points.Count; i++)
		{
			float tail = i / (float)(_points.Count - 1);
			float width = Width * Mathf.Lerp(1.0f, TailWidthScale, tail);
			Vector3 tangent = i == 0 ? _points[0].Position - _points[1].Position
				: i == _points.Count - 1 ? _points[i - 1].Position - _points[i].Position
				: _points[i - 1].Position - _points[i + 1].Position;
			Vector3 view = camera != null ? cameraPosition - _points[i].Position : Vector3.Back;
			Vector3 side = tangent.Cross(view).Normalized();
			if (side.LengthSquared() < 0.001f) side = cameraRight;
			Vector3 offset = side * width * 0.5f;
			vertices[i * 2] = _points[i].Position - offset;
			vertices[i * 2 + 1] = _points[i].Position + offset;
			uvs[i * 2] = new Vector2(tail, 0);
			uvs[i * 2 + 1] = new Vector2(tail, 1);
			float ageFade = PointLifetime > 0 ? Mathf.Clamp(1 - _points[i].Age / PointLifetime, 0, 1) : 1;
			colors[i * 2] = colors[i * 2 + 1] = new Color(1, 1, 1, ageFade);
		}

		int index = 0;
		for (int i = 0; i < _points.Count - 1; i++)
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
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		Mesh = mesh;
	}
}
