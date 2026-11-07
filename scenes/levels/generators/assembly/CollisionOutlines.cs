using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Solid collision projected onto the floor: a convex XZ outline and the height range it occupies.</summary>
public readonly record struct SolidOutline(Vector2[] Polygon, float Bottom, float Top)
{
	public Aabb Bounds
	{
		get
		{
			var area = new Rect2(Polygon[0], Vector2.Zero);
			foreach (var point in Polygon) area = area.Expand(point);
			return new Aabb(new Vector3(area.Position.X, Bottom, area.Position.Y), new Vector3(area.Size.X, Top - Bottom, area.Size.Y));
		}
	}
}

/// <summary>
/// Floor outlines of actual collision shapes. Convex shapes keep their true outline: a corner wall's L-shaped hull
/// does not fill its whole bounding square, so corners and corridor turns stay walkable where actors really fit.
/// </summary>
public static class CollisionOutlines
{
	/// <summary>Actor-only terrain barriers; projectiles and sight ignore this layer.</summary>
	public const uint WalkingTerrainLayer = 1 << 9;
	/// <summary>Collision layers that block actors: world, walls, props and walking terrain.</summary>
	public const uint BlockingLayers = 19 | WalkingTerrainLayer;

	public static SolidOutline FromShape(Shape3D shape, Transform3D transform)
	{
		IEnumerable<Vector3> points = shape switch
		{
			ConvexPolygonShape3D convex => convex.Points,
			BoxShape3D box => Corners(new Aabb(-box.Size / 2, box.Size)),
			_ => Corners(shape.GetDebugMesh().GetAabb()),
		};
		var world = points.Select(point => transform * point).ToArray();
		var hull = Geometry2D.ConvexHull(world.Select(point => new Vector2(point.X, point.Z)).ToArray());
		return new SolidOutline(hull, world.Min(point => point.Y), world.Max(point => point.Y));
	}

	public static SolidOutline FromBox(Aabb box) => new(
		[new(box.Position.X, box.Position.Z), new(box.End.X, box.Position.Z), new(box.End.X, box.End.Z), new(box.Position.X, box.End.Z)],
		box.Position.Y, box.End.Y);

	/// <summary>Every collision shape of a GridMap's cells, in the GridMap parent's space.</summary>
	public static IEnumerable<SolidOutline> FromGridMap(GridMap grid, Transform3D toParent)
	{
		if (grid?.MeshLibrary == null || (grid.CollisionLayer & BlockingLayers) == 0) yield break;
		foreach (Vector3I cell in grid.GetUsedCells())
		{
			int item = grid.GetCellItem(cell);
			var transform = toParent * grid.Transform * grid.GetCellTransform(cell);
			var shapes = grid.MeshLibrary.GetItemShapes(item);
			for (int index = 0; index + 1 < shapes.Count; index += 2)
				if (shapes[index].AsGodotObject() is Shape3D shape) yield return FromShape(shape, transform * shapes[index + 1].AsTransform3D());
		}
	}

	/// <summary>Static collision under <paramref name="root"/> (bodies and GridMaps), skipping doors, which can open.</summary>
	public static IEnumerable<SolidOutline> FromStaticBodies(Node root, Transform3D toParent)
	{
		var result = new List<SolidOutline>();
		Collect(root, toParent);
		return result;

		void Collect(Node node, Transform3D transform)
		{
			if (node is Door || node.IsQueuedForDeletion()) return;
			if (node is GridMap grid)
			{
				// GridMap.Transform is already part of transform here.
				result.AddRange(FromGridMap(grid, transform * grid.Transform.AffineInverse()));
				return;
			}
			if (node is CollisionShape3D { Disabled: false, Shape: not null } collision
				&& collision.GetParent() is StaticBody3D body && (body.CollisionLayer & BlockingLayers) != 0)
				result.Add(FromShape(collision.Shape, transform));
			foreach (Node child in node.GetChildren())
				Collect(child, child is Node3D spatial ? transform * spatial.Transform : transform);
		}
	}

	/// <summary>Zero inside a convex polygon (either winding), else the distance to its nearest edge.</summary>
	public static float DistanceTo(Vector2[] polygon, Vector2 point)
	{
		if (polygon.Length == 1) return point.DistanceTo(polygon[0]);
		bool inside = true;
		float sign = 0, best = float.MaxValue;
		for (int index = 0; index < polygon.Length; index++)
		{
			var a = polygon[index];
			var b = polygon[(index + 1) % polygon.Length];
			if (a == b) continue;
			float cross = (b - a).Cross(point - a);
			if (cross != 0)
			{
				if (sign == 0) sign = Mathf.Sign(cross);
				else if (Mathf.Sign(cross) != sign) inside = false;
			}
			best = Mathf.Min(best, point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, a, b)));
		}
		return inside && polygon.Length >= 3 ? 0 : best;
	}

	private static IEnumerable<Vector3> Corners(Aabb box)
	{
		for (int corner = 0; corner < 8; corner++) yield return box.GetEndpoint(corner);
	}
}