using Godot;
using System.Collections.Generic;

/// <summary>Editable scene composition with explicit placement bounds and independently measured solid collision.</summary>
[Tool]
public partial class RoomArrangement : Node3D
{
	[Export] public Node3D Content { get; set; }
	/// <summary>Authored static composition envelope. Particle emission and light ranges are deliberately excluded.</summary>
	[Export] public Aabb PlacementBounds { get; set; }

	public IReadOnlyList<Aabb> GetLocalBounds() => [PlacementBounds];

	/// <summary>Static rendered geometry and collision, used to validate the authored envelope without ticking scripts.</summary>
	public IReadOnlyList<Aabb> GetContentBounds() => CollectBounds(Content, Content?.Transform ?? Transform3D.Identity, true);

	/// <summary>Only solid static geometry restricts actor passage; lights, particles and areas have no floor footprint.</summary>
	public IReadOnlyList<Aabb> GetBlockingBounds() => CollectBounds(Content, Content?.Transform ?? Transform3D.Identity, false);

	private static IReadOnlyList<Aabb> CollectBounds(Node root, Transform3D transform, bool includeVisuals)
	{
		var boxes = new List<Aabb>();
		if (root == null) return boxes;
		if (root is GridMap grid)
			boxes.AddRange(GetGridBounds(grid, transform, includeVisuals));
		if (includeVisuals && root is MeshInstance3D { Mesh: not null } mesh)
			boxes.Add(transform * mesh.GetAabb());
		if (root is CollisionShape3D { Disabled: false, Shape: not null } collision
			&& collision.GetParent() is StaticBody3D body && (body.CollisionLayer & 19) != 0)
			boxes.Add(transform * collision.Shape.GetDebugMesh().GetAabb());
		foreach (Node child in root.GetChildren())
			boxes.AddRange(CollectBounds(child, transform * (child is Node3D spatial ? spatial.Transform : Transform3D.Identity), includeVisuals));
		return boxes;
	}

	/// <summary>Shared extraction for existing columns and proposed furniture, including authored collision extents.</summary>
	public static IReadOnlyList<Aabb> GetGridBounds(GridMap grid, Transform3D toParent, bool includeVisuals = true)
	{
		var boxes = new List<Aabb>();
		if (grid?.MeshLibrary == null) return boxes;
		foreach (Vector3I cell in grid.GetUsedCells())
		{
			int item = grid.GetCellItem(cell);
			var transform = toParent * new Transform3D(grid.GetBasisWithOrthogonalIndex(
				grid.GetCellItemOrientation(cell)).Scaled(Vector3.One * grid.CellScale), grid.MapToLocal(cell));
			var mesh = grid.MeshLibrary.GetItemMesh(item);
			if (includeVisuals && mesh != null) boxes.Add(transform * grid.MeshLibrary.GetItemMeshTransform(item) * mesh.GetAabb());
			if ((grid.CollisionLayer & 19) == 0) continue;
			var shapes = grid.MeshLibrary.GetItemShapes(item);
			for (int i = 0; i + 1 < shapes.Count; i += 2)
				if (shapes[i].AsGodotObject() is Shape3D shape)
					boxes.Add(transform * shapes[i + 1].AsTransform3D() * shape.GetDebugMesh().GetAabb());
		}
		return boxes;
	}

	/// <summary>Collect solid props in a local hierarchy, excluding operable doors and nonblocking areas.</summary>
	public static IReadOnlyList<Aabb> GetStaticPropBounds(Node root, Transform3D toParent)
	{
		var result = new List<Aabb>();
		Collect(root, toParent);
		return result;

		void Collect(Node node, Transform3D transform)
		{
			if (node is Door) return;
			if (node is RoomArrangement arrangement)
			{
				foreach (var box in arrangement.GetBlockingBounds()) result.Add(transform * box);
				return;
			}
			if (node is CollisionShape3D { Disabled: false, Shape: not null } collision
				&& collision.GetParent() is StaticBody3D body && (body.CollisionLayer & 19) != 0)
				result.Add(transform * collision.Shape.GetDebugMesh().GetAabb());
			foreach (Node child in node.GetChildren())
				Collect(child, transform * (child is Node3D spatial ? spatial.Transform : Transform3D.Identity));
		}
	}

}