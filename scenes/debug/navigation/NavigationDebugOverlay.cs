using Godot;

/// <summary>
/// Debug: a translucent overlay of a navigation region's baked mesh and every door's navigation link, drawn in world
/// space. Built from the current bake, so the debug menu rebuilds it each time it is shown; it lives in the level's
/// runtime world and goes away with regeneration.
/// </summary>
public partial class NavigationDebugOverlay : MeshInstance3D
{
	private static readonly Color OverlayColor = new(0.1f, 0.6f, 1f, 0.35f);

	/// <summary>Builds the overlay for <paramref name="region"/>; null when there is no baked geometry to draw.</summary>
	public static NavigationDebugOverlay Build(NavigationRegion3D region)
	{
		if (region?.NavigationMesh == null)
		{
			return null;
		}

		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		// Main baked region.
		bool hasGeometry = AppendRegionTriangles(surfaceTool, region, OverlayColor);

		// Per-door links drawn as flat strips between their endpoints, in the same blue as the main
		// mesh, so each doorway bridge is visible (whether the door is open or closed).
		foreach (Node node in region.GetTree().GetNodesInGroup("door"))
		{
			if (node is Door { NavigationLink: { } doorLink })
			{
				hasGeometry |= AppendLinkStrip(surfaceTool, doorLink, OverlayColor);
			}
		}

		if (!hasGeometry)
		{
			return null;
		}

		return new NavigationDebugOverlay
		{
			Name = "NavigationDebugOverlay",
			Mesh = surfaceTool.Commit(),
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				VertexColorUseAsAlbedo = true,
			},
			// Vertices are already baked in world space, so ignore the parent transform.
			TopLevel = true,
		};
	}

	/// <summary>
	/// Appends a navigation region's polygons to <paramref name="surfaceTool"/> in world space
	/// (lifted slightly to avoid z-fighting), tinted with <paramref name="color"/>. Returns false
	/// if the region has no navmesh geometry. Drawn together with each door's bridge patch so their
	/// relative height and alignment are visible.
	/// </summary>
	private static bool AppendRegionTriangles(SurfaceTool surfaceTool, NavigationRegion3D region, Color color)
	{
		NavigationMesh navigationMesh = region.NavigationMesh;
		Vector3[] vertices = navigationMesh.GetVertices();
		if (vertices.Length == 0)
		{
			return false;
		}

		Transform3D transform = region.GlobalTransform;
		transform.Origin += new Vector3(0, 0.05f, 0);

		for (int polygonIndex = 0; polygonIndex < navigationMesh.GetPolygonCount(); polygonIndex++)
		{
			int[] polygon = navigationMesh.GetPolygon(polygonIndex);
			// Fan-triangulate each convex navmesh polygon.
			for (int corner = 2; corner < polygon.Length; corner++)
			{
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[0]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner - 1]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner]]);
			}
		}

		return true;
	}

	/// <summary>
	/// Appends a flat quad strip following a navigation link from its start to end endpoint, tinted
	/// with <paramref name="color"/>. The strip's height is snapped to the baked navmesh (via the
	/// nearest navmesh point) so it sits co-planar with the main mesh overlay rather than at the
	/// link's authored floor-level endpoints. Returns false if the endpoints coincide.
	/// </summary>
	private static bool AppendLinkStrip(SurfaceTool surfaceTool, NavigationLink3D link, Color color)
	{
		Transform3D transform = link.GlobalTransform;
		Vector3 start = transform * link.StartPosition;
		Vector3 end = transform * link.EndPosition;

		// Lift to the navmesh surface (+ the same small z-fight offset the main mesh uses) so the
		// debug strip lines up with the blue overlay instead of hugging the floor.
		Rid navigationMap = link.GetWorld3D().NavigationMap;
		if (navigationMap.IsValid)
		{
			start.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, start).Y;
			end.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, end).Y;
		}
		start.Y += 0.05f;
		end.Y += 0.05f;

		Vector3 along = end - start;
		if (along.LengthSquared() <= 0.0001f)
		{
			return false;
		}

		// A half-metre-wide ribbon centred on the link line, lying flat on the floor plane.
		Vector3 side = along.Normalized().Cross(Vector3.Up);
		if (side.LengthSquared() <= 0.0001f)
		{
			side = Vector3.Right;
		}
		side = side.Normalized() * 0.25f;

		Vector3 a = start + side;
		Vector3 b = start - side;
		Vector3 c = end - side;
		Vector3 d = end + side;

		foreach (Vector3 vertex in new[] { a, b, c, a, c, d })
		{
			surfaceTool.SetColor(color);
			surfaceTool.AddVertex(vertex);
		}

		return true;
	}
}