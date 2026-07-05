using Godot;
using Godot.Collections;

/// <summary>
/// Shared helpers for instancing a character class's visual model outside its authored
/// scene. The in-game model swap and the menu preview both need the render-layer and
/// built-in-prop handling that player.tscn otherwise provides via editor overrides.
/// </summary>
public static class CharacterModel
{
	/// <summary>
	/// Player meshes render on layer 3 (bitmask 4); player.tscn overrides every
	/// MeshInstance3D to it and the FlickerLight/ObjectHighlight cull masks rely on it.
	/// </summary>
	public const uint PlayerRenderLayers = 4;

	/// <summary>
	/// Instantiates the class model ready for use: render layers applied to every mesh and
	/// built-in prop meshes stripped from every BoneAttachment3D except the class's hat and
	/// cape (the KayKit models ship with default weapons/shields/props attached).
	/// </summary>
	public static Node3D Instantiate(CharacterClass characterClass, uint renderLayers = PlayerRenderLayers)
	{
		var model = characterClass.CharacterScene.Instantiate<Node3D>();
		ApplyRenderLayers(model, renderLayers);
		StripBuiltInProps(model, characterClass);
		return model;
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
	/// Frees the default prop meshes the GLB ships attached (weapons, shields, spellbooks)
	/// while keeping the hat and cape the class actually wears. Runs before the model enters
	/// the tree, so immediate Free is safe.
	/// </summary>
	private static void StripBuiltInProps(Node3D modelRoot, CharacterClass characterClass)
	{
		Skeleton3D skeleton = FindSkeleton(modelRoot);
		if (skeleton == null)
		{
			return;
		}

		characterClass.AttachmentNodeNames.TryGetValue(AttachmentType.Hat, out string hatName);
		characterClass.AttachmentNodeNames.TryGetValue(AttachmentType.Cape, out string capeName);
		foreach (Node child in skeleton.GetChildren())
		{
			if (child is not BoneAttachment3D attachment)
			{
				continue;
			}

			if (attachment.Name == hatName || attachment.Name == capeName)
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
