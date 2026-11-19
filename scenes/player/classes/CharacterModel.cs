using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Shared helpers for instancing a character class's visual model outside its authored
/// scene. The in-game model swap and the menu preview both need the render-layer and
/// built-in-prop handling that player.tscn otherwise provides via editor overrides.
/// </summary>
public static class CharacterModel
{
	/// <summary>
	/// Player meshes render on layer 3 (bitmask 4); player.tscn overrides every
	/// MeshInstance3D to it and the PlayerLight's light and highlight cull masks rely on it.
	/// </summary>
	public const uint PlayerRenderLayers = 4;

	/// <summary>The bone a rigid helmet mounts on.</summary>
	public const string HelmetBone = "head";

	/// <summary>
	/// Instantiates the class model ready for use: render layers applied to every mesh and the
	/// prop meshes the KayKit template ships on its BoneAttachment3D nodes stripped (held items
	/// and worn armor are equipment).
	/// </summary>
	public static Node3D Instantiate(CharacterClass characterClass, uint renderLayers = PlayerRenderLayers) =>
		Instantiate(characterClass.CharacterScene, renderLayers);

	public static Node3D Instantiate(PackedScene scene, uint renderLayers)
	{
		var model = scene.Instantiate<Node3D>();
		ApplyRenderLayers(model, renderLayers);
		StripBuiltInProps(model);
		ConfigureAnimationLoops(model);
		return model;
	}

	/// <summary>
	/// Corrects non-looping locomotion clips on private copies. Unchanged libraries and clips remain shared.
	/// </summary>
	public static void ConfigureAnimationLoops(Node modelRoot)
	{
		AnimationPlayer animPlayer = FindAnimationPlayer(modelRoot);
		if (animPlayer == null)
		{
			return;
		}

		foreach (StringName libraryName in animPlayer.GetAnimationLibraryList())
		{
			var shared = animPlayer.GetAnimationLibrary(libraryName);
			AnimationLibrary local = null;
			foreach (StringName animName in shared.GetAnimationList())
			{
				string name = animName.ToString();
				if (!name.StartsWith("Run") && !name.StartsWith("Walk")) continue;
				Animation animation = shared.GetAnimation(animName);
				if (animation.LoopMode != Animation.LoopModeEnum.None) continue;
				local ??= (AnimationLibrary)shared.Duplicate();
				var loop = (Animation)animation.Duplicate();
				loop.LoopMode = Animation.LoopModeEnum.Linear;
				local.RemoveAnimation(animName);
				local.AddAnimation(animName, loop);
			}
			if (local == null) continue;
			animPlayer.RemoveAnimationLibrary(libraryName);
			animPlayer.AddAnimationLibrary(libraryName, local);
		}
	}

	/// <summary>Depth-first search for the model's AnimationPlayer.</summary>
	public static AnimationPlayer FindAnimationPlayer(Node root)
	{
		if (root is AnimationPlayer ap)
		{
			return ap;
		}

		foreach (Node child in root.GetChildren())
		{
			AnimationPlayer found = FindAnimationPlayer(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Dresses a class <paramref name="model"/> outside the game, as the menu preview shows it: in the helmet and body
	/// armor among the class's starting items. Skinned armor goes onto the model's skeleton, a rigid helmet on its head
	/// bone, and the model parts they cover are hidden.
	/// </summary>
	public static void Dress(Node3D model, CharacterClass characterClass) =>
		Dress(model, characterClass.StartingItems.Select(slot => slot?.Item).OfType<Armor>(), PlayerRenderLayers);

	public static void Dress(Node3D model, System.Collections.Generic.IEnumerable<Armor> outfit, uint renderLayers)
	{
		Skeleton3D skeleton = FindSkeleton(model);
		if (skeleton == null)
		{
			return;
		}
		var worn = new System.Collections.Generic.List<Item>();
		foreach (Armor armor in outfit)
		{
			if (armor?.WornScene is not { } scene) continue;
			if (!armor.ValidSlots.HasFlag(ValidSlots.Head) && !armor.ValidSlots.HasFlag(ValidSlots.Chest)) continue;
			worn.Add(armor);
			if (WornEquipment.Wear(skeleton, scene, renderLayers).Count > 0 || !armor.ValidSlots.HasFlag(ValidSlots.Head)) continue;
			var mount = new BoneAttachment3D { Name = $"Worn_{HelmetBone}", BoneName = HelmetBone };
			skeleton.AddChild(mount);
			var piece = scene.Instantiate<Node>();
			ApplyRenderLayers(piece, renderLayers);
			mount.AddChild(piece);
		}
		WornEquipment.ShowModelParts(skeleton, WornEquipment.CoveredParts(worn));
	}

	/// <summary>Depth-first search for the model's Skeleton3D.</summary>
	public static Skeleton3D FindSkeleton(Node root)
	{
		if (root is Skeleton3D skeleton)
		{
			return skeleton;
		}

		foreach (Node child in root.GetChildren())
		{
			Skeleton3D found = FindSkeleton(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Resolves the class's attachment node names to BoneAttachment3D nodes under the
	/// skeleton. Missing nodes log a warning and are omitted, so such gear still equips
	/// (stats apply) but shows no mesh.
	/// </summary>
	public static Dictionary<AttachmentType, BoneAttachment3D> ResolveAttachments(Node3D modelRoot, CharacterClass characterClass)
	{
		var attachments = new Dictionary<AttachmentType, BoneAttachment3D>();
		Skeleton3D skeleton = FindSkeleton(modelRoot);
		if (skeleton == null)
		{
			GD.PrintErr($"Character model for '{characterClass.Id}' has no Skeleton3D.");
			return attachments;
		}

		foreach ((AttachmentType type, string nodeName) in characterClass.AttachmentNodeNames)
		{
			var attachment = skeleton.GetNodeOrNull<BoneAttachment3D>(nodeName);
			if (attachment != null)
			{
				attachments[type] = attachment;
			}
			else
			{
				GD.PushWarning($"Character '{characterClass.Id}' has no attachment node '{nodeName}' ({type}).");
			}
		}

		return attachments;
	}

	private static void ApplyRenderLayers(Node node, uint renderLayers)
	{
		if (node is MeshInstance3D mesh)
		{
			mesh.Layers = renderLayers;
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyRenderLayers(child, renderLayers);
		}
	}

	/// <summary>
	/// Frees the prop meshes the GLB ships attached (weapons, shields, hats, capes). Runs before
	/// the model enters the tree, so immediate Free is safe.
	/// </summary>
	private static void StripBuiltInProps(Node3D modelRoot)
	{
		Skeleton3D skeleton = FindSkeleton(modelRoot);
		if (skeleton == null)
		{
			return;
		}

		foreach (Node child in skeleton.GetChildren())
		{
			if (child is not BoneAttachment3D attachment)
			{
				continue;
			}

			foreach (Node prop in attachment.GetChildren())
			{
				attachment.RemoveChild(prop);
				prop.Free();
			}
		}
	}
}