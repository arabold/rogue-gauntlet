using System;
using Godot;

/// <summary>Tags generated presentation meshes (x-ray silhouettes, door indicators) so material-swapping effects such
/// as invisibility and the discovery pulse leave them alone, and finds an actor's own meshes around them.</summary>
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
	/// Calls <paramref name="apply"/> on every mesh under <paramref name="root"/> outside helper subtrees, now and for each
	/// one added later (gear, swapped models) while <paramref name="owner"/> is in the tree. New meshes are handled the
	/// moment they enter the tree, before a running surface effect captures them. Returns the action ending the watch.
	/// </summary>
	public static Action WatchMeshes(Node owner, Node root, Action<MeshInstance3D> apply)
	{
		ForEachMesh(root, apply);
		return owner.SubscribeUntilExit(owner.GetTree(), tree => tree.NodeAdded += OnAdded, tree => tree.NodeAdded -= OnAdded);

		void OnAdded(Node node)
		{
			if (node is MeshInstance3D mesh && root.IsAncestorOf(mesh) && !IsInHelperSubtree(mesh)) apply(mesh);
		}
	}

	private static void ForEachMesh(Node node, Action<MeshInstance3D> apply)
	{
		if (IsHelper(node)) return;
		if (node is MeshInstance3D mesh) apply(mesh);
		foreach (Node child in node.GetChildren()) ForEachMesh(child, apply);
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