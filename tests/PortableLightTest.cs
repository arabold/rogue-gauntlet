namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Portable lights use the real equipment, hand attachment and save paths.</summary>
[TestSuite, RequireGodotRuntime]
public class PortableLightTest
{
	private static InventoryItemSlot Add(Inventory inventory, string path)
	{
		var item = ItemFactory.Create(TestFactories.Load<Item>(path));
		AssertBool(inventory.TryAdd(item).Success).IsTrue();
		return inventory.Items.Single(entry => entry.Item == item);
	}

	[TestCase]
	public void LightPrefersOffhandAndHandConflictsAreAtomic()
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
		AssertBool(inventory.Equip(twoHanded).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
		AssertBool(inventory.Equip(lamp, EquipmentSlot.ShieldHand).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsNull();
		AssertBool(inventory.Equip(lamp, EquipmentSlot.WeaponHand).Success).IsTrue();
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
	}

	/// <summary>
	/// The player's own light burns broad on normal floors and dims to a small baseline on dark ones; holding any light
	/// keeps it broad there, and held lights add no light of their own.
	/// </summary>
	[TestCase]
	public async Task HeldLightsKeepThePlayerLightBroadOnDarkFloors()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var level = runner.Scene().GetNode<Level>("Level");
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var playerLight = player.GetNode<PlayerLight>("Pivot/PlayerLight");
		var (lit, dark) = (playerLight.Lit, playerLight.Dark);
		AssertFloat(lit.Range).IsEqual(25f);
		AssertFloat(lit.Energy * lit.Flicker.Min).IsEqual(0.25f);
		AssertFloat(lit.Energy * lit.Flicker.Max).IsEqual(1f);
		AssertFloat(lit.HighlightRange).IsEqual(30f);
		AssertFloat(dark.Range).IsEqual(6f);
		AssertFloat(dark.Energy * dark.Flicker.Min).IsEqualApprox(0.04f, .0001f);
		AssertFloat(dark.Energy * dark.Flicker.Max).IsEqual(0.12f);
		void AssertBurns(PlayerLightProfile profile, string context)
		{
			AssertFloat(playerLight.Light.OmniRange).OverrideFailureMessage($"{context}: light range").IsEqual(profile.Range);
			AssertFloat(playerLight.Light.Energy).OverrideFailureMessage($"{context}: light energy").IsEqual(profile.Energy);
			AssertObject(playerLight.Light.Profile).OverrideFailureMessage($"{context}: flicker").IsSame(profile.Flicker);
			AssertFloat(playerLight.Highlight.OmniRange).OverrideFailureMessage($"{context}: highlight range").IsEqual(profile.HighlightRange);
		}
		player.Inventory.ReleaseAll();
		player.Inventory.Items.Clear();
		AssertBurns(lit, "normal floor");
		level.SetDark(true);
		AssertBurns(dark, "dark floor");
		level.SetDark(false);
		foreach (string name in new[] { "tool_candelabrum", "tool_candle_tallow", "tool_lamp_oil", "tool_lantern_brass", "tool_torch_iron", "tool_torch_wooden" })
		{
			var entry = Add(player.Inventory, $"res://scenes/items/tools/lamp/{name}.tres");
			AssertBool(player.InventoryController.TryEquip(entry, EquipmentSlot.ShieldHand).Success).IsTrue();
			var mount = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[AttachmentType.OffhandWeapon];
			AssertInt(mount.FindChildren("*", "Light3D", true, false).Count).OverrideFailureMessage($"{name} carries its own light").IsEqual(0);
			AssertBurns(lit, $"{name} on a normal floor");
			level.SetDark(true);
			AssertBurns(lit, $"{name} on a dark floor");
			AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
			AssertBurns(dark, $"{name} put away on a dark floor");
			AssertBool(player.InventoryController.TryEquip(entry, EquipmentSlot.WeaponHand).Success).IsTrue();
			AssertBurns(lit, $"{name} in the weapon hand on a dark floor");
			level.SetDark(false);
			AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
			AssertBurns(lit, $"{name} put away on a normal floor");
		}
	}

	[TestCase]
	public async Task InventoryMenuOffersBothHandsAndUnequippingRemovesLight()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var inventory = player.Inventory;
		var entry = Add(inventory, "res://scenes/items/tools/lamp/tool_lamp_oil.tres");
		var menu = TestFactories.Load<PackedScene>("res://scenes/ui/inventory_item_context_menu.tscn").Instantiate<InventoryItemContextMenu>();
		runner.Scene().AddChild(menu);
		menu.Initialize(inventory, entry, player);
		AssertBool(menu.GetNode<Button>("%EquipButton").Visible).IsTrue();
		AssertBool(menu.GetNode<Button>("%EquipOtherHandButton").Visible).IsTrue();
		menu.GetNode<Button>("%EquipButton").EmitSignal(BaseButton.SignalName.ButtonUp);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsSame(entry);
		menu.Initialize(inventory, entry, player);
		AssertBool(menu.GetNode<Button>("%EquipButton").Visible).IsFalse();
		menu.GetNode<Button>("%EquipOtherHandButton").EmitSignal(BaseButton.SignalName.ButtonUp);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsSame(entry);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
		menu.Initialize(inventory, entry, player);
		menu.GetNode<Button>("%UnequipButton").EmitSignal(BaseButton.SignalName.ButtonUp);
		AssertBool(inventory.IsEquipped(entry)).IsFalse();
		var mount = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[AttachmentType.OneHandedWeapon];
		AssertInt(mount.GetChildCount()).IsEqual(0);
	}

	/// <summary>Hands hang at the actor's sides in locomotion; light tops and flame emitters must rise in both hands.</summary>
	[TestCase]
	public async Task LightModelsAndFlamesPointUpInIdleAndWalking()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		player.MovementComponent.SetPhysicsProcess(false);
		player.GetNode<AnimationTree>("AnimationTree").Active = false;
		var animation = player.GetNode<AnimationPlayer>("Pivot/Character/AnimationPlayer");
		player.Inventory.ReleaseAll();
		player.Inventory.Items.Clear();
		foreach (string name in new[] { "tool_candelabrum", "tool_candle_tallow", "tool_lamp_oil", "tool_lantern_brass", "tool_torch_iron", "tool_torch_wooden" })
		{
			var entry = Add(player.Inventory, $"res://scenes/items/tools/lamp/{name}.tres");
			foreach (var hand in new[] { EquipmentSlot.WeaponHand, EquipmentSlot.ShieldHand })
			{
				AssertBool(player.InventoryController.TryEquip(entry, hand).Success).IsTrue();
				var mount = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[
					hand == EquipmentSlot.ShieldHand ? AttachmentType.OffhandWeapon : AttachmentType.OneHandedWeapon];
				var mesh = mount.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First();
				var flame = mount.FindChildren("Flame", "Node3D", true, false).OfType<Node3D>().Single();
				foreach (string clip in new[] { "Idle", "Walking_B" })
				{
					animation.Play(clip);
					foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f })
					{
						animation.Seek(animation.GetAnimation(clip).Length * phase, true);
						animation.Advance(0);
						await runner.SimulateFrames(1);
						AssertFloat(mesh.GlobalBasis.Y.Normalized().Dot(Vector3.Up))
							.OverrideFailureMessage($"{name} {hand} {clip} {phase}: model points sideways/down").IsGreater(0.9f);
						AssertFloat(flame.GlobalBasis.Y.Normalized().Dot(Vector3.Up))
							.OverrideFailureMessage($"{name} {hand} {clip} {phase}: flame rises sideways/down").IsGreater(0.9f);
					}
				}
				player.InventoryController.TryUnequip(entry);
			}
		}
	}

	[TestCase]
	public async Task EveryLightAttachesIlluminatesAndRestoresInEitherHand()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var level = runner.Scene().GetNode<Level>("Level");
		level.SetDark(true);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var playerLight = player.GetNode<PlayerLight>("Pivot/PlayerLight");
		var inventory = player.Inventory;
		inventory.ReleaseAll();
		inventory.Items.Clear();
		foreach (string name in new[] { "tool_candelabrum", "tool_candle_tallow", "tool_lamp_oil", "tool_lantern_brass", "tool_torch_iron", "tool_torch_wooden" })
		{
			var entry = Add(inventory, $"res://scenes/items/tools/lamp/{name}.tres");
			var definition = (PortableLightItem)entry.Item;
			var pickupModel = definition.Scene.Instantiate();
			AssertInt(pickupModel.FindChildren("*", "Light3D", true, false).Count).IsEqual(0);
			pickupModel.Free();
			foreach (var hand in new[] { EquipmentSlot.ShieldHand, EquipmentSlot.WeaponHand })
			{
				AssertBool(player.InventoryController.TryEquip(entry, hand).Success).IsTrue();
				var mount = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[
					hand == EquipmentSlot.ShieldHand ? AttachmentType.OffhandWeapon : AttachmentType.OneHandedWeapon];
				AssertString(mount.BoneName).IsEqual(definition.GripFor(hand).Bone.ToString());
				AssertInt(mount.GetChildCount()).IsGreater(0);
				AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Lit.Range);
				AssertBool(player.InventoryController.TryUnequip(entry).Success).IsTrue();
				AssertInt(mount.GetChildCount()).IsEqual(0);
				AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Dark.Range);
			}
			AssertBool(player.InventoryController.TryEquip(entry).Success).IsTrue();
			var saved = JsonSerializer.Deserialize<InventorySaveData>(JsonSerializer.Serialize(InventorySaveCodec.Capture(inventory)));
			var restored = InventorySaveCodec.Restore(saved);
			AssertBool(restored.Items.Single().Item is PortableLightItem).IsTrue();
			inventory.ReleaseAll();
			inventory.Items.Clear();
			foreach (var restoredEntry in restored.Items) { inventory.Items.Add(restoredEntry); }
			foreach (var equipment in saved.EquippedItems)
			{
				AssertBool(inventory.Equip(inventory.Items.Single(i => i.Item.InstanceId == equipment.ItemInstanceId),
					Enum.Parse<EquipmentSlot>(equipment.Slot)).Success).IsTrue();
			}
			var offhand = player.GetNode<BoneAttachmentManager>("BoneAttachmentManager").AttachmentNodes[AttachmentType.OffhandWeapon];
			AssertInt(offhand.GetChildCount()).IsGreater(0);
			AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Lit.Range);
			// Dropping equipped gear uses the same unequip events and must leave no carried light.
			AssertBool(player.InventoryController.TryDrop(inventory.Items.Single(), 1).Success).IsTrue();
			AssertInt(offhand.GetChildCount()).IsEqual(0);
			AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Dark.Range);
		}
		await runner.SimulateFrames(2);
	}
}