namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Portable lights are off-hand only and use the real equipment, hand attachment, player light and save paths.</summary>
[TestSuite, RequireGodotRuntime]
public class PortableLightTest
{
	private static readonly string[] Lights = ["tool_candelabrum", "tool_candle_tallow", "tool_lamp_oil", "tool_lantern_brass", "tool_torch_iron", "tool_torch_wooden"];

	private static InventoryItemSlot Add(Inventory inventory, string path)
	{
		var item = ItemFactory.Create(TestFactories.Load<Item>(path));
		AssertBool(inventory.TryAdd(item).Success).IsTrue();
		return inventory.Items.Single(entry => entry.Item == item);
	}

	/// <summary>The real player, carrying nothing, inside an ungenerated level so the level's darkness reaches its light.</summary>
	private static (Level Level, Player Player) LevelWithPlayer()
	{
		var (level, _) = TestFactories.UngeneratedLevel();
		var player = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		level.AddChild(player);
		TestFactories.AddToSceneTree(level);
		player.MovementComponent.SetPhysicsProcess(false);
		player.Inventory.ReleaseAll();
		player.Inventory.Items.Clear();
		return (level, player);
	}

	/// <summary>The off-hand mount; the first held item replaces the authored one with the grip's own.</summary>
	private static BoneAttachment3D Offhand(Player player) =>
		player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[AttachmentType.OffhandWeapon];

	[TestCase]
	public void LightsTakeOnlyTheOffHandAndHandConflictsAreAtomic()
	{
		var inventory = new Inventory();
		var weapon = Add(inventory, "res://scenes/items/weapons/sword_common.tres");
		var shield = Add(inventory, "res://scenes/items/armor/shield/shield_round_wood.tres");
		var lamp = Add(inventory, "res://scenes/items/tools/lamp/tool_lamp_oil.tres");
		var twoHanded = Add(inventory, "res://scenes/items/weapons/crossbow_common.tres");
		inventory.Equip(weapon);
		inventory.Equip(shield);
		((EquipableItem)shield.Item).CursePenalty = [new StatModifier { Stat = StatType.Strength, Value = -1 }];
		AssertBool(inventory.Equip(lamp).Failure == ItemFailure.Cursed).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsSame(shield);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsSame(weapon);
		((EquipableItem)shield.Item).LiftCurse();
		AssertBool(inventory.Equip(lamp).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsSame(lamp);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsSame(weapon);
		AssertBool(inventory.Items.Contains(shield)).IsTrue();
		// The weapon hand never takes a light, and refusing it changes nothing.
		AssertBool(inventory.Equip(lamp, EquipmentSlot.WeaponHand).Failure == ItemFailure.InvalidTarget).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsSame(lamp);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsSame(weapon);
		// A two-handed weapon displaces the light, and the light displaces it in turn.
		AssertBool(inventory.Equip(twoHanded).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
		AssertBool(inventory.Equip(lamp).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsSame(lamp);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsNull();
	}

	/// <summary>
	/// The player's own light burns broad on normal floors and dims on dark ones; holding any light keeps it broad there,
	/// held lights add no light of their own, and putting one away, saving or dropping it behaves like the real hand.
	/// </summary>
	[TestCase]
	public async Task EveryHeldLightAttachesAndKeepsThePlayerLightBroadOnDarkFloors()
	{
		var (level, player) = LevelWithPlayer();
		var inventory = player.Inventory;
		var playerLight = player.GetNode<PlayerLight>("Pivot/PlayerLight");
		var (lit, dark) = (playerLight.Lit, playerLight.Dark);
		// The modes must differ for the checks below to tell which one burns.
		AssertFloat(dark.Range).IsLess(lit.Range);
		void AssertBurns(PlayerLightProfile profile, string context)
		{
			AssertFloat(playerLight.Light.OmniRange).OverrideFailureMessage($"{context}: light range").IsEqual(profile.Range);
			AssertFloat(playerLight.Light.Energy).OverrideFailureMessage($"{context}: light energy").IsEqual(profile.Energy);
			AssertObject(playerLight.Light.Profile).OverrideFailureMessage($"{context}: flicker").IsSame(profile.Flicker);
			AssertFloat(playerLight.Highlight.OmniRange).OverrideFailureMessage($"{context}: highlight range").IsEqual(profile.HighlightRange);
		}
		AssertBurns(lit, "normal floor");
		level.Lighting.IsDark = true;
		AssertBurns(dark, "dark floor");

		foreach (string name in Lights)
		{
			var entry = Add(inventory, $"res://scenes/items/tools/lamp/{name}.tres");
			var definition = (PortableLightItem)entry.Item;
			var pickupModel = definition.Scene.Instantiate();
			AssertInt(pickupModel.FindChildren("*", "Light3D", true, false).Count).OverrideFailureMessage($"{name} pickup is lit").IsEqual(0);
			pickupModel.Free();

			AssertBool(player.InventoryController.TryEquip(entry, EquipmentSlot.ShieldHand).Success).IsTrue();
			var offhand = Offhand(player);
			AssertString(offhand.BoneName).IsEqual(definition.GripFor(EquipmentSlot.ShieldHand).Bone.ToString());
			AssertInt(offhand.GetChildCount()).IsGreater(0);
			AssertInt(offhand.FindChildren("*", "Light3D", true, false).Count).OverrideFailureMessage($"{name} carries its own light").IsEqual(0);
			AssertBurns(lit, $"{name} on a dark floor");
			level.Lighting.IsDark = false;
			AssertBurns(lit, $"{name} on a normal floor");
			level.Lighting.IsDark = true;
			AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
			AssertInt(offhand.GetChildCount()).IsEqual(0);
			AssertBurns(dark, $"{name} put away on a dark floor");

			// A held light survives a save and burns again once restored.
			AssertBool(player.InventoryController.TryEquip(entry).Success).IsTrue();
			var saved = JsonSerializer.Deserialize<InventorySaveData>(JsonSerializer.Serialize(InventorySaveCodec.Capture(inventory)));
			var restored = InventorySaveCodec.Restore(saved);
			AssertBool(restored.Items.Single().Item is PortableLightItem).IsTrue();
			inventory.ReleaseAll();
			inventory.Items.Clear();
			foreach (var restoredEntry in restored.Items) inventory.Items.Add(restoredEntry);
			foreach (var equipment in saved.EquippedItems)
				AssertBool(inventory.Equip(inventory.Items.Single(i => i.Item.InstanceId == equipment.ItemInstanceId),
					Enum.Parse<EquipmentSlot>(equipment.Slot)).Success).IsTrue();
			offhand = Offhand(player);
			AssertInt(offhand.GetChildCount()).IsGreater(0);
			AssertBurns(lit, $"{name} restored on a dark floor");
			// Dropping equipped gear uses the same unequip events and must leave no carried light.
			AssertBool(player.InventoryController.TryDrop(inventory.Items.Single(), 1).Success).IsTrue();
			AssertInt(offhand.GetChildCount()).IsEqual(0);
			AssertBurns(dark, $"{name} dropped on a dark floor");
		}
		await FreeRemovedModels(player);
	}

	/// <summary>The off hand hangs at the actor's side in locomotion; light tops and flame emitters must still rise.</summary>
	[TestCase]
	public async Task LightModelsAndFlamesPointUpInIdleAndWalking()
	{
		var (_, player) = LevelWithPlayer();
		player.GetNode<AnimationTree>("AnimationTree").Active = false;
		var animation = player.GetNode<AnimationPlayer>("Pivot/Character/AnimationPlayer");
		var tree = player.GetTree();
		foreach (string name in Lights)
		{
			var entry = Add(player.Inventory, $"res://scenes/items/tools/lamp/{name}.tres");
			AssertBool(player.InventoryController.TryEquip(entry, EquipmentSlot.ShieldHand).Success).IsTrue();
			var offhand = Offhand(player);
			var mesh = offhand.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First();
			var flame = offhand.FindChildren("Flame", "Node3D", true, false).OfType<Node3D>().Single();
			foreach (string clip in new[] { "Idle", "Walking_B" })
			{
				animation.Play(clip);
				foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f })
				{
					animation.Seek(animation.GetAnimation(clip).Length * phase, true);
					animation.Advance(0);
					// The held model follows the posed bone once the skeleton publishes the pose.
					await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
					AssertFloat(mesh.GlobalBasis.Y.Normalized().Dot(Vector3.Up))
						.OverrideFailureMessage($"{name} {clip} {phase}: model points sideways/down").IsGreater(0.9f);
					AssertFloat(flame.GlobalBasis.Y.Normalized().Dot(Vector3.Up))
						.OverrideFailureMessage($"{name} {clip} {phase}: flame rises sideways/down").IsGreater(0.9f);
				}
			}
			AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
		}
		await FreeRemovedModels(player);
	}

	/// <summary>Unequipped hand models are queued for deletion; let the frame end so they are not reported as orphans.</summary>
	private static async Task FreeRemovedModels(Node node)
	{
		var tree = node.GetTree();
		await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}
}