using Godot;

/// <summary>Tags generated presentation meshes (x-ray silhouettes, door indicators) so material-swapping effects such
/// as invisibility and the discovery pulse leave them alone.</summary>
public static class VisualHelper
{
	public const string MetaKey = "visual_helper";
	public static void Mark(Node node) => node.SetMeta(MetaKey, true);
	public static bool IsHelper(Node node) => node.HasMeta(MetaKey);
}