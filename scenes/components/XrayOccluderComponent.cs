using System;
using Godot;

/// <summary>
/// Keeps x-ray silhouettes off an actor that has none of its own, such as the hero. Every mesh under <see cref="Target"/>
/// (body, worn and held gear, and anything attached later) draws <see cref="Mask"/> as its material overlay: the stencil
/// pass that monsters and loot already use to mark the pixels where they are in view (<c>occlusion_xray_mask.tres</c>).
/// Door, chest, monster and loot x-rays skip those pixels, so a silhouette hidden behind this actor never paints over it.
/// Surface effects that replace the overlay (cloak, skins) drop the mask while they run and restore it afterwards.
/// </summary>
[GlobalClass]
public partial class XrayOccluderComponent : Node
{
	/// <summary>The actor's visual root.</summary>
	[Export] public Node3D Target { get; set; }
	/// <summary><c>occlusion_xray_mask.tres</c>. Unassigned leaves x-rays free to draw over the actor.</summary>
	[Export] public Material Mask { get; set; }

	private Action _unwatch;

	/// <summary>Masks before any sibling's <c>_Ready</c>, so a surface effect started there captures the overlay.</summary>
	public override void _EnterTree()
	{
		if (_unwatch != null || Engine.IsEditorHint() || Target == null || Mask == null) return;
		Apply(Target);
		_unwatch = this.SubscribeUntilExit(GetTree(), tree => tree.NodeAdded += OnNodeAdded, tree => tree.NodeAdded -= OnNodeAdded);
	}

	private void OnNodeAdded(Node node)
	{
		if (node is MeshInstance3D mesh && Target.IsAncestorOf(mesh) && !VisualHelper.IsInHelperSubtree(mesh)) mesh.MaterialOverlay = Mask;
	}

	private void Apply(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D mesh) mesh.MaterialOverlay = Mask;
		foreach (Node child in node.GetChildren()) Apply(child);
	}
}