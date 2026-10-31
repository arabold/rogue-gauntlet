using System.Collections.Generic;
using Godot;

/// <summary>
/// Measured shape of one prop variant, in prop-local space after its <see cref="Prop.QuarterTurns"/>: the static
/// envelope (visual and collision, used for spacing and overlap) and the solid boxes actors cannot pass.
/// Mesh-library variants also carry the item's collision shapes for emission.
/// </summary>
public sealed class PropGeometry
{
	public Aabb Envelope { get; init; }
	public IReadOnlyList<Aabb> Solids { get; init; } = [];
	/// <summary>Collision shapes of a mesh-library item, in prop-local space; empty for scenes.</summary>
	public IReadOnlyList<(Shape3D Shape, Transform3D Transform)> Shapes { get; init; } = [];

	public static PropGeometry FromLibraryItem(MeshLibrary library, int item, Basis turn, bool blocking)
	{
		var mesh = library.GetItemMesh(item);
		var turnTransform = new Transform3D(turn, Vector3.Zero);
		var envelope = turnTransform * library.GetItemMeshTransform(item) * (mesh?.GetAabb() ?? new Aabb());
		var shapes = new List<(Shape3D, Transform3D)>();
		var solids = new List<Aabb>();
		var itemShapes = library.GetItemShapes(item);
		for (int index = 0; index + 1 < itemShapes.Count; index += 2)
		{
			if (itemShapes[index].AsGodotObject() is not Shape3D shape) continue;
			var transform = turnTransform * itemShapes[index + 1].AsTransform3D();
			shapes.Add((shape, transform));
			var box = transform * shape.GetDebugMesh().GetAabb();
			solids.Add(box);
			envelope = envelope.Merge(box);
		}
		// A blocking model without collision still blocks: emission gives it a box collider over its envelope.
		if (blocking && solids.Count == 0) solids.Add(envelope);
		return new PropGeometry { Envelope = envelope, Solids = blocking ? solids : [], Shapes = shapes };
	}

	public static PropGeometry FromScene(PackedScene scene, Basis turn, bool blocking)
	{
		var root = scene.Instantiate();
		try
		{
			var turnTransform = new Transform3D(turn, Vector3.Zero);
			var solids = new List<Aabb>();
			Aabb? envelope = null;
			// Arrangement scenes declare a static envelope that excludes light and particle reach.
			if (root is RoomArrangement arrangement)
			{
				envelope = turnTransform * arrangement.PlacementBounds;
				foreach (var box in arrangement.GetBlockingBounds()) solids.Add(turnTransform * box);
			}
			else
			{
				Collect(root, turnTransform);
			}
			var bounds = envelope ?? new Aabb();
			if (blocking && solids.Count == 0 && bounds.Size != Vector3.Zero) solids.Add(bounds);
			return new PropGeometry { Envelope = bounds, Solids = blocking ? solids : [] };

			void Collect(Node node, Transform3D transform)
			{
				if (node is MeshInstance3D { Mesh: not null } mesh) Include(transform * mesh.GetAabb());
				if (node is CollisionShape3D { Disabled: false, Shape: not null } collision && collision.GetParent() is StaticBody3D)
				{
					var box = transform * collision.Shape.GetDebugMesh().GetAabb();
					solids.Add(box);
					Include(box);
				}
				foreach (Node child in node.GetChildren())
					Collect(child, child is Node3D spatial ? transform * spatial.Transform : transform);
			}

			void Include(Aabb box) => envelope = envelope?.Merge(box) ?? box;
		}
		finally
		{
			root.Free();
		}
	}
}