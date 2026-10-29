using Godot;

/// <summary>Builds the faceted, three-dimensional ribbon used by the expanding flame burst.</summary>
public partial class RadialFlameRibbon : MeshInstance3D
{
	private static ArrayMesh _sharedMesh;
	public override void _Ready() => Mesh = GodotObject.IsInstanceValid(_sharedMesh) ? _sharedMesh : _sharedMesh = BuildMesh();

	private static ArrayMesh BuildMesh()
	{
		const int segments = 64, sides = 12;
		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < segments; i++)
			for (int j = 0; j < sides; j++)
			{
				AddVertex(surface, i, j); AddVertex(surface, i, j + 1); AddVertex(surface, i + 1, j);
				AddVertex(surface, i + 1, j); AddVertex(surface, i, j + 1); AddVertex(surface, i + 1, j + 1);
			}
		return surface.Commit();
	}

	private static void AddVertex(SurfaceTool surface, int segment, int side)
	{
		float u = segment / 64f, v = side / 12f;
		float around = u * Mathf.Tau, cross = v * Mathf.Tau;
		float radius = 3.7f + .6f * Mathf.Cos(cross);
		surface.SetUV(new Vector2(u, v));
		surface.AddVertex(new Vector3(Mathf.Cos(around) * radius, .6f * Mathf.Sin(cross), Mathf.Sin(around) * radius));
	}
}