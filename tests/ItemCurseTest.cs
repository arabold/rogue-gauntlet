namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Hidden curses: rolled at creation (always on harmful types), a hidden penalty while worn, sticky equipment that
/// cannot be removed, displaced, dropped or destroyed, revealed by Identify, lifted by Enchant and Remove Curse
/// without telling whether a curse was there, and kept across save and restore. Needs the Godot runtime for item
/// Resources and <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemCurseTest
{
	private const string Ring = "res://scenes/items/jewelry/ring/ring_strength.tres";
	private const string OtherRing = "res://scenes/items/jewelry/ring/ring_dexterity.tres";
	private const string Frailty = "res://scenes/items/jewelry/ring/ring_frailty.tres";
	private const string Sword = "res://scenes/items/weapons/broadsword_common.tres";
	private const string TwoHandedAxe = "res://scenes/items/weapons/battleaxe_common.tres";
	private const string Shield = "res://scenes/items/armor/shield_common.tres";

	private static ItemCreationPolicy Policy => TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");

	private static EquipableItem Create(string path) => (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>(path));

	private static EquipableItem Cursed(string path)
	{
		EquipableItem item = Create(path);
		LootRoller.ApplyRoll(item, 12, new RandomNumberGenerator { Seed = 1 }, Policy, ItemQuality.Normal, cursed: true);
		return item;
	}

	private static InventoryItemSlot Add(Inventory inventory, Item item)
	{
		inventory.TryAdd(item);
		return inventory.Items.Last();
	}

	[TestCase]
	public void CreationCursesByChanceAndHarmfulTypesAlways()
	{
		using var rng = GenerationRandom.Create(42, "curse-creation");
		int cursed = 0;
		for (int i = 0; i < 400; i++)
		{
			var ring = (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>(Ring), Policy, 12, rng);
			AssertBool(ring.CurseStatusKnown).IsFalse();
			if (ring.IsCursed)
			{
				cursed++;
				AssertBool(ring.CurseModifiers.All(modifier => modifier.Value < 0)).IsTrue();
			}
		}
		AssertInt(cursed).IsBetween(20, 70);

		for (int i = 0; i < 20; i++)
		{
			AssertBool(((EquipableItem)ItemFactory.Create(TestFactories.Load<Item>(Frailty), Policy, 3, rng)).IsCursed).IsTrue();
		}

		EquipableItem fixedRing = Create(Frailty);
		AssertBool(fixedRing.IsCursed || !fixedRing.CurseStatusKnown).IsFalse();
	}

	[TestCase]
	public void ThePenaltyAppliesWhileWornButIsNotAnAttribute()
	{
		EquipableItem ring = Cursed(Ring);

		AssertInt(ring.StatModifiers.Count()).IsEqual(ring.AttributeModifiers.Count() + ring.CursePenalty.Length);
		AssertBool(ring.CurseModifiers.All(modifier => modifier.Value < 0)).IsTrue();
	}

	[TestCase]
	public void AWornCursedItemCannotBeRemovedDisplacedOrDestroyed()
	{
		var inventory = new Inventory { Capacity = 10 };
		EquipableItem cursed = Cursed(Ring);
		InventoryItemSlot entry = Add(inventory, cursed);
		AssertBool(inventory.Equip(entry, EquipmentSlot.LeftRing).Success).IsTrue();

		AssertInt((int)inventory.Unequip(entry).Failure).IsEqual((int)ItemFailure.Cursed);
		AssertInt((int)inventory.Unequip(EquipmentSlot.LeftRing).Failure).IsEqual((int)ItemFailure.Cursed);
		AssertInt((int)inventory.Equip(entry, EquipmentSlot.RightRing).Failure).IsEqual((int)ItemFailure.Cursed);

		InventoryItemSlot other = Add(inventory, Create(OtherRing));
		ItemOperationResult displaced = inventory.Equip(other, EquipmentSlot.LeftRing);
		AssertInt((int)displaced.Failure).IsEqual((int)ItemFailure.Cursed);
		AssertBool(displaced.Subject == cursed).IsTrue();
		AssertBool(inventory.EquippedItems[EquipmentSlot.LeftRing] == entry).IsTrue();
		AssertBool(inventory.Equip(other).Success).IsTrue();
		AssertBool(inventory.EquippedItems[EquipmentSlot.RightRing] == other).IsTrue();

		AssertInt((int)inventory.CanRemove(entry, 1).Failure).IsEqual((int)ItemFailure.Cursed);
		inventory.DestroyItem(entry);
		inventory.RemoveItem(entry, 1);
		AssertBool(inventory.Items.Contains(entry)).IsTrue();
		AssertBool(inventory.EquippedItems[EquipmentSlot.LeftRing] == entry).IsTrue();
	}

	[TestCase]
	public void ATwoHandedWeaponCannotDisplaceACursedShield()
	{
		var inventory = new Inventory { Capacity = 10 };
		InventoryItemSlot shield = Add(inventory, Cursed(Shield));
		inventory.Equip(shield, EquipmentSlot.ShieldHand);
		InventoryItemSlot axe = Add(inventory, Create(TwoHandedAxe));

		AssertInt((int)inventory.Equip(axe).Failure).IsEqual((int)ItemFailure.Cursed);
		AssertBool(inventory.EquippedItems[EquipmentSlot.WeaponHand] == null).IsTrue();
		AssertBool(inventory.EquippedItems[EquipmentSlot.ShieldHand] == shield).IsTrue();
	}

	[TestCase]
	public void OnlyIdentifyRevealsTheCurseAndNamesLeadWithIt()
	{
		EquipableItem sword = Cursed(Sword);
		var slot = new InventoryItemSlot { Item = sword };
		var identify = new IdentifyScrollEffect();

		AssertString(ItemIdentity.ResolveDisplayName(sword)).IsEqual("Broadsword");
		AssertBool(identify.IsValidTarget(null, slot)).IsTrue();
		sword.RevealAttributes(); // what wearing it does
		AssertBool(sword.CurseStatusKnown).IsFalse();

		identify.ApplyToTarget(null, slot);
		AssertBool(sword.CurseStatusKnown).IsTrue();
		AssertString(ItemIdentity.ResolveDisplayName(sword)).IsEqual("Cursed Broadsword");
		AssertBool(identify.IsValidTarget(null, slot)).IsFalse();
	}

	[TestCase]
	public void RemoveCurseTargetsAnythingNotKnownUncursedAndLeavesItKnownUncursed()
	{
		var removeCurse = new RemoveCurseScrollEffect();
		EquipableItem unknownUncursed = Create(Ring);
		unknownUncursed.CurseStatusKnown = false;
		EquipableItem knownUncursed = Create(Ring);
		EquipableItem knownCursed = Cursed(Ring);
		knownCursed.CurseStatusKnown = true;

		AssertBool(removeCurse.IsValidTarget(null, new InventoryItemSlot { Item = unknownUncursed })).IsTrue();
		AssertBool(removeCurse.IsValidTarget(null, new InventoryItemSlot { Item = knownUncursed })).IsFalse();
		AssertBool(removeCurse.IsValidTarget(null, new InventoryItemSlot { Item = knownCursed })).IsTrue();

		var inventory = new Inventory { Capacity = 5 };
		InventoryItemSlot entry = Add(inventory, knownCursed);
		inventory.Equip(entry, EquipmentSlot.LeftRing);
		EquipableItem changed = null;
		inventory.EquippedItemChanged += item => changed = item;
		inventory.ChangeItem(entry, item => item.LiftCurse());

		AssertBool(changed == knownCursed).IsTrue();
		AssertBool(inventory.EquippedItems[EquipmentSlot.LeftRing] == entry).IsTrue();
		AssertBool(knownCursed.IsCursed || !knownCursed.CurseStatusKnown).IsFalse();
		AssertBool(inventory.Unequip(entry).Success).IsTrue();

		unknownUncursed.LiftCurse();
		AssertBool(unknownUncursed.CurseStatusKnown && !unknownUncursed.IsCursed).IsTrue();
	}

	[TestCase]
	public void EnchantLiftsACurse()
	{
		EquipableItem sword = Cursed(Sword);
		var ladder = TestFactories.Load<QualityLadder>("res://scenes/items/quality/quality_ladder.tres");

		AssertBool(LootRoller.Enchant(sword, 10, new RandomNumberGenerator { Seed = 2 }, ladder)).IsTrue();

		AssertBool(sword.IsCursed || !sword.CurseStatusKnown).IsFalse();
	}

	[TestCase]
	public void SaveAndRestoreKeepTheCurseAndWhatIsKnown()
	{
		EquipableItem ring = Cursed(Ring);

		var restored = (EquipableItem)ItemSaveCodec.Restore(ItemSaveCodec.Capture(ring, 1));

		AssertBool(restored.IsCursed).IsTrue();
		AssertBool(restored.CurseStatusKnown).IsFalse();
		AssertInt(restored.CursePenalty.Length).IsEqual(ring.CursePenalty.Length);
		AssertFloat(restored.CursePenalty[0].Value).IsEqual(ring.CursePenalty[0].Value);
	}

	[TestCase]
	public void CursedItemShowsCurseAuraInInventoryOnlyWhenCurseStatusIsKnown()
	{
		var panel = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/item_slot_panel.tscn").Instantiate<ItemSlotPanel>());
		var preview = panel.GetNode<Preview>("%Preview");

		EquipableItem sword = Cursed(Sword);
		AssertBool(sword.CurseStatusKnown).IsFalse();

		// 1. Unknown curse in backpack slot: no active aura, render-once
		panel.SetItem(new InventoryItemSlot { Item = sword }, isEquipped: false);
		AssertBool(preview.AuraPresenter == null || !preview.AuraPresenter.HasActiveAura).IsTrue();
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Once);

		// 2. Revealed curse: shows curse aura, renders live
		sword.RevealCurseStatus();
		AssertObject(preview.AuraPresenter).IsNotNull();
		AssertBool(preview.AuraPresenter.HasActiveAura).IsTrue();
		AssertString(preview.AuraPresenter.ActiveEffect?.Id.ToString()).IsEqual("cursed_item");
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Always);

		// 3. Equipped slot also shows curse aura
		panel.SetItem(new InventoryItemSlot { Item = sword }, isEquipped: true);
		AssertBool(preview.AuraPresenter.HasActiveAura).IsTrue();
		AssertString(preview.AuraPresenter.ActiveEffect?.Id.ToString()).IsEqual("cursed_item");
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Always);

		// 4. Lifting the curse ends the aura and returns to render-once
		sword.LiftCurse();
		AssertBool(preview.AuraPresenter.HasActiveAura).IsFalse();
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Once);

		// 5. Uncursed item has no aura
		panel.SetItem(new InventoryItemSlot { Item = Create(Sword) }, isEquipped: false);
		AssertBool(preview.AuraPresenter == null || !preview.AuraPresenter.HasActiveAura).IsTrue();
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Once);

		// 6. Clearing the slot removes the aura
		panel.SetItem(null, isEquipped: false);
		AssertObject(preview.AuraPresenter).IsNull();
		AssertInt((int)preview.RenderTargetUpdateMode).IsEqual((int)SubViewport.UpdateMode.Once);
	}
}
