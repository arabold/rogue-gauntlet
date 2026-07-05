namespace RogueGauntlet.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>
/// Regression net for GLB drift: the class .tres files reference model attachment nodes
/// and animations by name, which a re-export or importer change can silently break (gear
/// would equip without a visual, previews would freeze). Instantiates every catalog model
/// and checks the names still resolve. Needs the Godot runtime for scene instantiation.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CharacterModelTest
{
	[TestCase]
	public void EveryClassModelResolvesItsAttachmentsAndPreviewAnimation()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		foreach (CharacterClass characterClass in catalog.Classes)
		{
			Node3D model = AutoFree(CharacterModel.Instantiate(characterClass));

			AssertObject(CharacterModel.FindSkeleton(model))
				.OverrideFailureMessage($"{characterClass.Id}: model has no Skeleton3D")
				.IsNotNull();

			var attachments = CharacterModel.ResolveAttachments(model, characterClass);
			AssertInt(attachments.Count)
				.OverrideFailureMessage($"{characterClass.Id}: not every attachment node name resolved")
				.IsEqual(characterClass.AttachmentNodeNames.Count);

			var animationPlayer = model.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
			AssertObject(animationPlayer)
				.OverrideFailureMessage($"{characterClass.Id}: model has no AnimationPlayer")
				.IsNotNull();
			AssertBool(animationPlayer.HasAnimation(characterClass.PreviewAnimation))
				.OverrideFailureMessage($"{characterClass.Id}: missing preview animation '{characterClass.PreviewAnimation}'")
				.IsTrue();
		}
	}

	[TestCase]
	public void InstantiateStripsBuiltInPropsButKeepsHatAndCape()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		foreach (CharacterClass characterClass in catalog.Classes)
		{
			Node3D model = AutoFree(CharacterModel.Instantiate(characterClass));
			Skeleton3D skeleton = CharacterModel.FindSkeleton(model);
			var attachments = CharacterModel.ResolveAttachments(model, characterClass);

			foreach (Node child in skeleton.GetChildren())
			{
				if (child is not BoneAttachment3D attachment)
				{
					continue;
				}

				bool isWorn = (attachments.TryGetValue(AttachmentType.Hat, out BoneAttachment3D hat) && attachment == hat)
					|| (attachments.TryGetValue(AttachmentType.Cape, out BoneAttachment3D cape) && attachment == cape);
				if (!isWorn)
				{
					AssertInt(attachment.GetChildCount())
						.OverrideFailureMessage($"{characterClass.Id}: built-in prop left on '{attachment.Name}'")
						.IsEqual(0);
				}
			}
		}
	}
}
