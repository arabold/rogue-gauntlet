using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;


/// <summary>
/// Flags for equipment slots.
/// </summary>
[Flags]
public enum ValidSlots
{
	Head = EquipmentSlot.Head,
	Chest = EquipmentSlot.Chest,
	Hands = EquipmentSlot.Hands,
	Legs = EquipmentSlot.Legs,
	Feet = EquipmentSlot.Feet,
	Neck = EquipmentSlot.Neck,
	LeftRing = EquipmentSlot.LeftRing,
	RightRing = EquipmentSlot.RightRing,
	WeaponHand = EquipmentSlot.WeaponHand,
	ShieldHand = EquipmentSlot.ShieldHand,
	Arrows = EquipmentSlot.Arrows,
}

public enum EquipableItemRarity
{
	Common = 0,
	Uncommon = 1,
	Rare = 2,
	Legendary = 3,
	Unique = 4,
}

[GlobalClass]
public partial class EquipableItem : BuffedItem
{
	/// <summary>
	/// Type of this item
	/// </summary>
	[Export] public ValidSlots ValidSlots { get; set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Rarity of this item
	/// </summary>
	[Export]
	public EquipableItemRarity Rarity { get; set => SetValue(ref field, value); } = EquipableItemRarity.Common;

	/// <summary>
	/// Instance-level affixes rolled onto this item when it dropped. Empty on shared
	/// definitions; populated on the duplicated instance produced by <see cref="LootRoller"/>.
	/// </summary>
	[Export] public Array<RolledAffix> Affixes { get; set => SetValue(ref field, value); } = new();

	/// <summary>
	/// Resource path of the definition this instance was rolled from. Set on rolled
	/// duplicates (whose own <see cref="Resource.ResourcePath"/> is empty) so the save can
	/// reload the base item and re-attach the rolled affixes. Empty on shared definitions.
	/// </summary>
	public string SourceDefinitionPath { get; set; } = "";

	public virtual void OnEquipped(Player player)
	{
		ApplyBuff(player);
		player.Stats.AddModifiers(this, BuildStatModifiers());
	}

	public virtual void OnUnequipped(Player player)
	{
		player.Stats.RemoveModifiersFrom(this);
		RemoveBuff(player);
	}

	/// <summary>
	/// The stat modifiers this item grants while equipped, all registered under the item
	/// instance so unequipping reverses exactly them. Subclasses add their intrinsic stats
	/// (damage, armor, …) on top of this base contribution, which is the rolled affixes.
	/// </summary>
	protected virtual IEnumerable<StatModifier> BuildStatModifiers()
	{
		if (Affixes == null)
		{
			yield break;
		}

		foreach (RolledAffix affix in Affixes)
		{
			if (affix?.Modifiers == null)
			{
				continue;
			}

			foreach (StatModifier modifier in affix.Modifiers)
			{
				yield return modifier;
			}
		}
	}

	/// <summary>
	/// Wraps a base name with the rolled affix fragments, e.g. "Vicious Broadsword of the
	/// Bear". Uses the first prefix and first suffix; extra affixes add stats but not name.
	/// </summary>
	public string ComposeName(string baseName)
	{
		if (Affixes == null || Affixes.Count == 0)
		{
			return baseName;
		}

		string prefix = null;
		string suffix = null;
		foreach (RolledAffix affix in Affixes)
		{
			if (string.IsNullOrEmpty(affix?.NameFragment))
			{
				continue;
			}

			if (affix.Kind == AffixKind.Prefix)
			{
				prefix ??= affix.NameFragment;
			}
			else
			{
				suffix ??= affix.NameFragment;
			}
		}

		string composed = baseName;
		if (!string.IsNullOrEmpty(prefix))
		{
			composed = $"{prefix} {composed}";
		}

		if (!string.IsNullOrEmpty(suffix))
		{
			composed = $"{composed} {suffix}";
		}

		return composed;
	}
}
