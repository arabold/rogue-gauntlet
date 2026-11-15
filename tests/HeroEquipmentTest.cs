namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Class heroes start a run wearing their own helmet and body armor. Skinned armor is worn on the hero's own skeleton,
/// helmets hide the head parts they cover (hair gives way to the scalp), and taking either off restores the bare hero.
/// Needs the Godot runtime for the class models and the player scene.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroEquipmentTest
{
	private const string PlayerScene = "res://scenes/player/player.tscn";

	/// <summary>Each class's starting helmet and the head parts it hides.</summary>
	private static readonly (string Class, string Helmet, string Armor, bool HidesHair)[] Kits =
	[
		("barbarian", "Bear Hood", "Barbarian Harness", true),
		("knight", "Knight Helmet", "Knight Armor", true),
		("mage", "Wizard Hat", "Mage Coat", false),
		("rogue", "Rogue Hood", "Ranger Coat", true),
	];

	[TestCase]
	public async Task EveryClassStartsInItsOwnHelmetAndArmor()
	{
		foreach (var kit in Kits)
		{
			var player = await Spawn(kit.Class);
			try
			{
				AssertString(player.Inventory.EquippedItems[EquipmentSlot.Head]?.Item?.Name).IsEqual(kit.Helmet);
				AssertString(player.Inventory.EquippedItems[EquipmentSlot.Chest]?.Item?.Name).IsEqual(kit.Armor);
				Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
				var armor = WornMeshes(skeleton);
				AssertBool(armor.Length > 0).OverrideFailureMessage($"{kit.Class}: no armor on the skeleton").IsTrue();
				foreach (MeshInstance3D mesh in armor)
				{
					AssertObject(mesh.GetNode(mesh.Skeleton)).IsSame(skeleton);
					for (int bind = 0; bind < mesh.Skin.GetBindCount(); bind++)
						AssertInt(skeleton.FindBone(mesh.Skin.GetBindName(bind)))
							.OverrideFailureMessage($"{kit.Class}: armor bone '{mesh.Skin.GetBindName(bind)}' is missing")
							.IsGreaterEqual(0);
				}
				MeshInstance3D hair = Part(skeleton, "Hair");
				if (hair != null)
				{
					AssertBool(hair.Visible).OverrideFailureMessage($"{kit.Class}: hair visibility").IsEqual(!kit.HidesHair);
					AssertBool(Part(skeleton, "Scalp").Visible).IsEqual(kit.HidesHair);
				}
			}
			finally
			{
				Despawn(player);
			}
		}
	}

	[TestCase]
	public async Task TakingOffTheHelmetAndArmorShowsTheBareHero()
	{
		var player = await Spawn("knight");
		try
		{
			Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
			AssertBool(Part(skeleton, "Ears").Visible).IsFalse();

			var helmet = player.Inventory.EquippedItems[EquipmentSlot.Head];
			AssertBool(player.InventoryController.TryUnequip(helmet).Success).IsTrue();
			AssertBool(Part(skeleton, "Hair").Visible).IsTrue();
			AssertBool(Part(skeleton, "Ears").Visible).IsTrue();
			AssertBool(Part(skeleton, "Scalp").Visible).IsFalse();

			// The knight's suit brings the knight's own plated arms and greaves in place of the hero's sleeves, gloves,
			// pants and boots.
			AssertBool(Part(skeleton, "Arms").Visible).IsFalse();
			AssertBool(Part(skeleton, "Legs").Visible).IsFalse();
			var armor = player.Inventory.EquippedItems[EquipmentSlot.Chest];
			AssertBool(player.InventoryController.TryUnequip(armor).Success).IsTrue();
			await player.ToSignal(player.GetTree(), SceneTree.SignalName.ProcessFrame);
			AssertInt(WornMeshes(skeleton).Length).IsEqual(0);
			AssertBool(Part(skeleton, "Arms").Visible).IsTrue();
			AssertBool(Part(skeleton, "Legs").Visible).IsTrue();
		}
		finally
		{
			Despawn(player);
		}
	}

	[TestCase]
	public void TheMenuPreviewDressesEachClassInItsStartingGear()
	{
		foreach (CharacterClass characterClass in CharacterCatalog.LoadDefault().Classes)
		{
			Node3D model = AutoFree(CharacterModel.Instantiate(characterClass));
			CharacterModel.Dress(model, characterClass);
			Skeleton3D skeleton = CharacterModel.FindSkeleton(model);
			AssertBool(skeleton.GetChildren().OfType<MeshInstance3D>().Any(mesh => mesh.Skin != null && mesh.Name.ToString().EndsWith("_Armor")))
				.OverrideFailureMessage($"{characterClass.Id}: the preview wears no body armor")
				.IsTrue();
			AssertBool(skeleton.GetNodeOrNull<BoneAttachment3D>("Worn_head")?.FindChildren("*", "MeshInstance3D", true, false).Count > 0)
				.OverrideFailureMessage($"{characterClass.Id}: the preview wears no helmet")
				.IsTrue();
		}
	}

	private static async Task<Player> Spawn(string classId)
	{
		GameSession.Instance.BeginRun(1, classId);
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>(PlayerScene).Instantiate<Player>());
		await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
		return player;
	}

	private static void Despawn(Player player)
	{
		player.QueueFree();
		GameSession.Instance.ClearSession();
	}

	/// <summary>Skinned armor worn on <paramref name="skeleton"/>: its meshes the equipment manager added.</summary>
	private static MeshInstance3D[] WornMeshes(Skeleton3D skeleton) => skeleton.GetChildren().OfType<MeshInstance3D>()
		.Where(mesh => mesh.Skin != null && mesh.HasMeta("equipped_attachment") && !mesh.IsQueuedForDeletion()).ToArray();

	/// <summary>The hero model's <paramref name="part"/> node (<c>Knight_Hair</c>), or null when it has none.</summary>
	private static MeshInstance3D Part(Skeleton3D skeleton, string part) =>
		skeleton.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(mesh => mesh.Name.ToString().EndsWith("_" + part));
}