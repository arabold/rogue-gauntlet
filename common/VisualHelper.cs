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
}