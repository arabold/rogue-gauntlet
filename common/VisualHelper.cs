using Godot;

/// <summary>Tags generated presentation meshes (x-ray silhouettes, door indicators) so material-swapping effects such
/// as invisibility and the discovery pulse leave them alone.</summary>
public static class VisualHelper
{
	// Existing 3D render layer 10 is reserved for debug presentation, outside world-only probes.
	public const uint DebugRenderLayer = 1u << 9;
	public const string MetaKey = "visual_helper";
	public static void Mark(Node node) => node.SetMeta(MetaKey, true);
	public static bool IsHelper(Node node) => node.HasMeta(MetaKey);
	public static bool IsInHelperSubtree(Node node)
	{
		for (var current = node; current != null; current = current.GetParent())
			if (IsHelper(current)) return true;
		return false;
	}

	/// <summary>
	/// Bounds of the meshes under <paramref name="root"/> in <paramref name="space"/>'s local space, skipping helper
	/// subtrees. Meshes only: particle visibility bounds are generous. Null when there are no meshes.
	/// </summary>
	public static Aabb? MeshBounds(Node root, Node3D space)
	{
		Aabb? bounds = null;
		MergeMeshBounds(root, space.GlobalTransform.AffineInverse(), ref bounds);
		return bounds;
	}

	private static void MergeMeshBounds(Node node, Transform3D toSpace, ref Aabb? bounds)
	{
		if (IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			Aabb box = toSpace * mesh.GlobalTransform * mesh.GetAabb();
			bounds = bounds?.Merge(box) ?? box;
		}
		foreach (Node child in node.GetChildren()) MergeMeshBounds(child, toSpace, ref bounds);
	}
}