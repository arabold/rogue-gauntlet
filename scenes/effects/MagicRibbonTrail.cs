using Godot;
using System.Collections.Generic;

/// <summary>
/// Builds a camera-facing ribbon from recent world positions for high-readability magic trails.
/// </summary>
public partial class MagicRibbonTrail : MeshInstance3D, IPooledNode
{
	[Export] public Node3D Target { get; set; }
	[Export] public int MaxPoints { get; set; } = 18;
	[Export] public float PointSpacing { get; set; } = 0.12f;
	[Export] public float Width { get; set; } = 0.75f;
	[Export] public float TailWidthScale { get; set; } = 0.2f;
	[Export] public float MeshUpdateInterval { get; set; } = 0.033f;

	private readonly List<Vector3> _points = new();
	private float _meshUpdateCooldown;
	private bool _meshDirty;

	public override void _Ready()
	{
		Target ??= GetParent<Node3D>();
		TopLevel = true;
		CastShadow = ShadowCastingSetting.Off;
	}

	public void OnSpawnedFromPool()
	{
		_points.Clear();
		Mesh = null;
		_meshUpdateCooldown = 0.0f;
		_meshDirty = false;
		Target ??= GetParent<Node3D>();
	}

	public void OnDespawnedToPool()
	{
		_points.Clear();
		Mesh = null;
		_meshDirty = false;
	}

	public override void _Process(double delta)
	{
		if (Target == null || !GodotObject.IsInstanceValid(Target))
		{
			QueueFree();
			return;
		}

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

		_meshUpdateCooldown -= (float)delta;
		if (_meshDirty && _meshUpdateCooldown <= 0.0f)
		{
			BuildMesh();
			_meshDirty = false;
			_meshUpdateCooldown = MeshUpdateInterval;
		}
	}

	private void BuildMesh()
	{
		if (_points.Count < 2)
		{
			return;
		}

		var vertices = new Vector3[_points.Count * 2];
		var uvs = new Vector2[_points.Count * 2];
		var indices = new int[(_points.Count - 1) * 6];

		Vector3 cameraRight = GetViewport().GetCamera3D()?.GlobalTransform.Basis.X.Normalized() ?? Vector3.Right;

		for (int i = 0; i < _points.Count; i++)
		{
			float t = i / (float)(_points.Count - 1);
			float width = Width * Mathf.Lerp(1.0f, TailWidthScale, t);
			Vector3 offset = cameraRight * width * 0.5f;

			vertices[i * 2] = _points[i] - offset;
			vertices[i * 2 + 1] = _points[i] + offset;
			uvs[i * 2] = new Vector2(t, 0.0f);
			uvs[i * 2 + 1] = new Vector2(t, 1.0f);
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
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		Mesh = mesh;
	}
}
