using Godot;

/// <summary>
/// Debug: a translucent overlay of a floor's clearance grid, drawn in world space. Each cell a body of the drawn
/// radius can stand in is tinted from orange (tight, routes avoid it) to blue (open); closed door gates show red.
/// Built from the current door states, so the debug menu rebuilds it each time it is shown; it lives in the level's
/// runtime world and goes away with regeneration.
/// </summary>
public partial class NavigationDebugOverlay : MeshInstance3D
{
	private const float Lift = .08f;
	private static readonly Color Tight = new(1f, .55f, .1f, .45f);
	private static readonly Color Open = new(.1f, .6f, 1f, .3f);
	private static readonly Color ClosedGate = new(1f, .1f, .1f, .55f);

	/// <summary>Builds the overlay for bodies of <paramref name="radius"/>; null without navigation or traversable cells.</summary>
	public static NavigationDebugOverlay Build(FloorNavigation navigation, float radius = .75f)
	{
		if (navigation == null) return null;
		var grid = navigation.Grid;
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
		bool hasGeometry = false;
		float half = grid.CellSize * .45f;
		float preferred = radius + navigation.Profile.PreferredMargin;
		for (int x = 0; x < grid.Size.X; x++)
		for (int z = 0; z < grid.Size.Y; z++)
		{
			var cell = new Vector2I(x, z);
			if (!grid.IsTraversable(cell, radius, null)) continue;
			var center = grid.CellCenter(cell);
			bool closed = !navigation.IsTraversable(new Vector3(center.X, 0, center.Y), radius);
			var color = closed ? ClosedGate : Tight.Lerp(Open, Mathf.Clamp((grid.ClearanceAt(cell) - radius) / Mathf.Max(.01f, preferred - radius), 0, 1));
			var a = new Vector3(center.X - half, Lift, center.Y - half);
			var b = new Vector3(center.X + half, Lift, center.Y - half);
			var c = new Vector3(center.X + half, Lift, center.Y + half);
			var d = new Vector3(center.X - half, Lift, center.Y + half);
			foreach (var vertex in new[] { a, b, c, a, c, d })
			{
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(vertex);
			}
			hasGeometry = true;
		}
		if (!hasGeometry) return null;
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
			// Vertices are in world space, so ignore the parent transform.
			TopLevel = true,
		};
	}
}