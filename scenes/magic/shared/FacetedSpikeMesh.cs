using Godot;

/// <summary>Creates the faceted pointed mesh shared by stone and ice visual scenes.</summary>
public static class FacetedSpikeMesh
{
	public static Mesh Create(Vector3 size, float seed, Material material)
	{
		const int sides = 6;
		var lower = new Vector3[sides]; var shoulder = new Vector3[sides];
		for (int i = 0; i < sides; i++)
		{
			float a = Mathf.Tau * i / sides, variation = .7f + .25f * Mathf.Sin(i * 4.7f + seed);
			lower[i] = new Vector3(Mathf.Cos(a) * size.X * variation, 0, Mathf.Sin(a) * size.Z * variation);
			shoulder[i] = new Vector3(Mathf.Cos(a + .13f) * size.X * variation * .65f, size.Y * (.38f + .09f * Mathf.Sin(i * 3 + seed)), Mathf.Sin(a + .13f) * size.Z * variation * .65f);
		}
		var tip = new Vector3(0, size.Y, 0);
		var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); st.SetSmoothGroup(uint.MaxValue);
		for (int i = 0; i < sides; i++)
		{
			int j = (i + 1) % sides;
			st.AddVertex(lower[i]); st.AddVertex(lower[j]); st.AddVertex(shoulder[i]);
			st.AddVertex(lower[j]); st.AddVertex(shoulder[j]); st.AddVertex(shoulder[i]);
			st.AddVertex(shoulder[i]); st.AddVertex(shoulder[j]); st.AddVertex(tip);
		}
		st.GenerateNormals(); var mesh = st.Commit(); mesh.SurfaceSetMaterial(0, material); return mesh;
	}
}
