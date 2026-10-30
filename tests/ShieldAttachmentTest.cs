namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Only the Knight and Barbarian models ship a shield prop bone, so shields mount on a shared left-hand socket that
/// every class model has, placed where those models hold their own shield. Needs the Godot runtime for the class
/// models and the player scene.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShieldAttachmentTest
{
	private const string ShieldGrip = "res://scenes/items/grips/kaykit_shield.tres";

	[TestCase]
	public void EveryClassModelHasTheShieldSocketAtItsNativeShieldPose()
	{
		var grip = TestFactories.Load<WeaponGrip>(ShieldGrip);
		foreach (CharacterClass characterClass in CharacterCatalog.LoadDefault().Classes)
		{
			Node3D model = AutoFree(CharacterModel.Instantiate(characterClass));
			Skeleton3D skeleton = CharacterModel.FindSkeleton(model);
			AssertInt(skeleton.FindBone(grip.Bone))
				.OverrideFailureMessage($"{characterClass.Id}: no '{grip.Bone}' bone for shields")
				.IsGreaterEqual(0);

			if (!CharacterModel.ResolveAttachments(model, characterClass).TryGetValue(AttachmentType.Shield, out var native))
			{
				continue;
			}
			int nativeBone = skeleton.FindBone(native.BoneName);
			Transform3D rest = skeleton.GetBoneRest(nativeBone);
			AssertString(skeleton.GetBoneName(skeleton.GetBoneParent(nativeBone))).IsEqual(grip.Bone.ToString());
			AssertFloat(rest.Origin.DistanceTo(grip.Transform.Origin))
				.OverrideFailureMessage($"{characterClass.Id}: shield grip is off its native shield position")
				.IsLess(1e-4f);
			AssertFloat(rest.Basis.GetRotationQuaternion().AngleTo(grip.Transform.Basis.GetRotationQuaternion()))
				.OverrideFailureMessage($"{characterClass.Id}: shield grip is rotated off its native shield")
				.IsLess(1e-3f);
		}
	}

	[TestCase]
	public async Task EquippedShieldMountsOnTheSharedHandSocket()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner, 3);
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		await runner.SimulateFrames(2);
		var shield = ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/armor/shield_common.tres"));
		AssertBool(player.Inventory.TryAdd(shield).Success).IsTrue();
		var entry = player.Inventory.Items.Single(slot => slot.Item == shield);
		AssertBool(player.InventoryController.TryEquip(entry, EquipmentSlot.ShieldHand).Success).IsTrue();
		await runner.SimulateFrames(1);

		var mount = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[AttachmentType.Shield];
		AssertString(mount.BoneName).IsEqual("handslot.l");
		AssertBool(mount.FindChildren("*", "MeshInstance3D", true, false).Count > 0).IsTrue();
		// The authored player is the Barbarian, whose own (stripped) shield bone shows where a shield belongs.
		var native = CharacterModel.FindSkeleton(player).GetNode<Node3D>("Barbarian_Round_Shield");
		AssertFloat(mount.GetNode<Node3D>("Grip").GlobalPosition.DistanceTo(native.GlobalPosition)).IsLess(1e-3f);

		AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
		AssertInt(mount.GetChildCount()).IsEqual(0);
	}
}
