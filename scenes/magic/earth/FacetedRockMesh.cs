using Godot;

/// <summary>Creates irregular faceted rock meshes: coarse, jittered lumps sheared flat along random fracture planes.</summary>
public static class FacetedRockMesh
{
	private const int Segments = 6, Rings = 4;

	/// <summary>Builds a rock around the given half-extents, stretched per seed, with smaller chunks fused onto its sides so a tumbling rock never reads as a ball.</summary>
	public static Mesh Create(Vector3 size, ulong seed, Material material, int chunks = 0)
	{
		var random = new RandomNumberGenerator { Seed = seed };
		var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); st.SetSmoothGroup(uint.MaxValue);
		size *= new Vector3(random.RandfRange(.8f, 1.25f), random.RandfRange(.8f, 1.25f), random.RandfRange(.8f, 1.25f));
		AddLump(st, random, Vector3.Zero, size, Basis.Identity);
		for (int i = 0; i < chunks; i++)
		{
			// Chunks sit around the waist, half buried, so they break the outline without floating off the body.
			Vector3 side = new Vector3(random.RandfRange(-1, 1), random.RandfRange(-.5f, .5f), random.RandfRange(-1, 1)).Normalized();
			AddLump(st, random, side * size * .42f, size * random.RandfRange(.45f, .62f), new Basis(RandomDirection(random), random.RandfRange(0, Mathf.Tau)));
		}
		st.GenerateNormals(); var mesh = st.Commit(); mesh.SurfaceSetMaterial(0, material); return mesh;
	}

	private static void AddLump(SurfaceTool st, RandomNumberGenerator random, Vector3 center, Vector3 size, Basis basis)
	{
		var points = new Vector3[(Rings - 1) * Segments + 2];
		points[0] = (Vector3.Up + RandomDirection(random) * .2f).Normalized() * random.RandfRange(.8f, 1.1f);
		points[^1] = (Vector3.Down + RandomDirection(random) * .2f).Normalized() * random.RandfRange(.8f, 1.1f);
		for (int ring = 1; ring < Rings; ring++)
			for (int segment = 0; segment < Segments; segment++)
			{
				float polar = Mathf.Pi * (ring + random.RandfRange(-.22f, .22f)) / Rings;
				float azimuth = Mathf.Tau * (segment + ring * .5f + random.RandfRange(-.3f, .3f)) / Segments;
				points[Index(ring, segment)] = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth)) * random.RandfRange(.75f, 1.12f);
			}
		// Projecting everything beyond a plane onto it cuts the broad, flat breaks that make a lump read as split stone.
		for (int i = 0; i < 4; i++)
		{
			var fracture = new Plane(RandomDirection(random), random.RandfRange(.38f, .66f));
			for (int p = 0; p < points.Length; p++)
			{
				float excess = fracture.DistanceTo(points[p]);
				if (excess > 0) points[p] -= fracture.Normal * excess;
			}
		}
		// A lean shears the top over the base, so the lump is lopsided rather than mirrored about its center.
		Vector2 lean = new(random.RandfRange(-.4f, .4f), random.RandfRange(-.4f, .4f));
		for (int p = 0; p < points.Length; p++) points[p] += new Vector3(lean.X, 0, lean.Y) * points[p].Y;
		for (int segment = 0; segment < Segments; segment++)
		{
			AddTriangle(st, points, center, size, basis, 0, Index(1, segment), Index(1, segment + 1));
			for (int ring = 1; ring < Rings - 1; ring++)
			{
				AddTriangle(st, points, center, size, basis, Index(ring, segment), Index(ring + 1, segment), Index(ring, segment + 1));
				AddTriangle(st, points, center, size, basis, Index(ring, segment + 1), Index(ring + 1, segment), Index(ring + 1, segment + 1));
			}
			AddTriangle(st, points, center, size, basis, points.Length - 1, Index(Rings - 1, segment + 1), Index(Rings - 1, segment));
		}
	}

	private static int Index(int ring, int segment) => 1 + (ring - 1) * Segments + segment % Segments;

	/// <summary>Emits a triangle wound to face away from the lump's center, which the jitter can otherwise flip.</summary>
	private static void AddTriangle(SurfaceTool st, Vector3[] points, Vector3 center, Vector3 size, Basis basis, int a, int b, int c)
	{
		Vector3 pa = points[a], pb = points[b], pc = points[c];
		// Godot front faces are clockwise, so (c - a) x (b - a) is the outward normal.
		if ((pc - pa).Cross(pb - pa).Dot(pa + pb + pc) < 0) (pb, pc) = (pc, pb);
		foreach (Vector3 point in new[] { pa, pb, pc }) st.AddVertex(center + basis * (point * size));
	}

	private static Vector3 RandomDirection(RandomNumberGenerator random) => new Vector3(random.Randfn(), random.Randfn(), random.Randfn()).Normalized();
}
