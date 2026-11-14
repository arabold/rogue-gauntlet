using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Puts worn armor on a character model, shared by the in-game <see cref="BoneAttachmentManager"/> and the menu's
/// <see cref="CharacterPreview"/>.
/// <list type="bullet">
/// <item>Skinned armor (exported on the hero rig) has its meshes moved onto the model's own skeleton, so it bends with
/// the body; its skin binds by bone name.</item>
/// <item>Head parts a worn piece covers (<see cref="Armor.HiddenModelParts"/>) are hidden; the scalp under the hair shows
/// only while the hair is hidden.</item>
/// </list>
/// </summary>
public static class WornEquipment
{
	/// <summary>The model node suffix of the scalp that lies under a hero's hair.</summary>
	private const string Scalp = "Scalp";

	/// <summary>
	/// Moves <paramref name="scene"/>'s skinned meshes onto <paramref name="skeleton"/> on the player render
	/// <paramref name="layers"/> and returns them, or returns an empty list (and leaves nothing behind) when the scene has
	/// no skinned mesh, so the caller mounts it rigidly instead.
	/// </summary>
	public static List<MeshInstance3D> Wear(Skeleton3D skeleton, PackedScene scene, uint layers, StringName meta = null)
	{
		var instance = scene.Instantiate<Node>();
		var worn = instance.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
			.Where(mesh => mesh.Skin != null).ToList();
		foreach (MeshInstance3D mesh in worn)
		{
			mesh.GetParent().RemoveChild(mesh);
			mesh.Owner = null;
			mesh.Layers = layers;
			if (meta != null) mesh.SetMeta(meta, true);
			skeleton.AddChild(mesh);
			mesh.Skeleton = "..";
			mesh.Transform = Transform3D.Identity;
		}
		instance.Free();
		return worn;
	}

	/// <summary>
	/// Shows every hero model part except those <paramref name="hidden"/> names (by node-name suffix: <c>_Hair</c>,
	/// <c>_Ears</c>, the <c>_Arms</c> chest armor replaces with its own); the scalp shows exactly while the hair is hidden.
	/// </summary>
	public static void ShowModelParts(Node model, IEnumerable<string> hidden)
	{
		var covered = hidden.ToHashSet();
		foreach (MeshInstance3D mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
		{
			string name = mesh.Name;
			int split = name.LastIndexOf('_');
			if (split < 0) continue;
			string part = name[(split + 1)..];
			if (part == Scalp) mesh.Visible = covered.Contains("Hair");
			else if (covered.Contains(part)) mesh.Visible = false;
			else if (part is "Hair" or "Ears" or "Beard" or "Arms") mesh.Visible = true;
		}
	}

	/// <summary>The head parts all of <paramref name="items"/> cover together.</summary>
	public static IEnumerable<string> CoveredParts(IEnumerable<Item> items) =>
		items.OfType<Armor>().SelectMany(armor => armor.HiddenModelParts ?? []);
}