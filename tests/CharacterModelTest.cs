namespace RogueGauntlet.Tests;

using GdUnit4;
using Godot;
using System.Linq;
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

	/// <summary>Heroes are bare: the template's props (weapons, shields, hats, capes) are stripped; gear is equipment.</summary>
	[TestCase]
	public void InstantiateStripsEveryBuiltInProp()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		foreach (CharacterClass characterClass in catalog.Classes)
		{
			Node3D model = AutoFree(CharacterModel.Instantiate(characterClass));
			foreach (BoneAttachment3D attachment in CharacterModel.FindSkeleton(model).GetChildren().OfType<BoneAttachment3D>())
			{
				AssertInt(attachment.GetChildCount())
					.OverrideFailureMessage($"{characterClass.Id}: built-in prop left on '{attachment.Name}'")
					.IsEqual(0);
			}
		}
	}
}
