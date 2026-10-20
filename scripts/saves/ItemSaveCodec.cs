using Godot;
using System;
using System.Collections.Generic;

/// <summary>One current-format codec for carried and world item records. Restoration never rolls.</summary>
public static class ItemSaveCodec
{
	public static ItemEntrySaveData Capture(Item item, int quantity)
	{
		if (!item.IsRuntimeInstance || string.IsNullOrEmpty(item.DefinitionPath) || quantity <= 0
			|| quantity > (item.CanStack ? item.MaxStackSize : 1))
		{ throw new InvalidOperationException("Cannot save an uninitialized or unresolved item."); }
		var data = new ItemEntrySaveData
		{
			ItemPath = item.DefinitionPath, InstanceId = item.InstanceId, Quantity = quantity,
			Food = item is FoodItem food ? new FoodItemSaveData { IsPoisoned = food.IsPoisoned } : null,
		};
		if (item is EquipableItem equipment)
		{
			data.Rarity = (int)equipment.Rarity;
			data.Affixes = CaptureAffixes(equipment.Affixes);
		}
		return data;
	}

	public static Item Restore(ItemEntrySaveData data)
	{
		if (data == null || string.IsNullOrEmpty(data.InstanceId) || data.Quantity <= 0)
		{ throw new InvalidOperationException("Invalid saved item identity or quantity."); }
		Item definition = ResourceLoader.Load<Item>(data.ItemPath)
			?? throw new InvalidOperationException($"Missing item definition: {data.ItemPath}");
		Item item = ItemFactory.CopyDefinition(definition, data.InstanceId);
		if (item is EquipableItem equipment)
		{
			if (!Enum.IsDefined(typeof(EquipableItemRarity), data.Rarity))
			{ throw new InvalidOperationException("Invalid saved equipment rarity."); }
			equipment.Rarity = (EquipableItemRarity)data.Rarity;
			equipment.Affixes = BuildAffixes(data.Affixes);
		}
		if (item is FoodItem food)
		{
			if (data.Food == null) { throw new InvalidOperationException("Missing saved food state."); }
			food.RestorePoison(data.Food.IsPoisoned);
		}
		int cap = item.CanStack ? item.MaxStackSize : 1;
		if (data.Quantity > cap) { throw new InvalidOperationException("Invalid saved stack quantity."); }
		return item;
	}

	internal static List<RolledAffixSaveData> CaptureAffixes(Godot.Collections.Array<RolledAffix> affixes)
	{
		var list = new List<RolledAffixSaveData>();
		if (affixes == null)
		{
			return list;
		}

		foreach (RolledAffix affix in affixes)
		{
			if (affix == null)
			{
				continue;
			}

			var data = new RolledAffixSaveData { NameFragment = affix.NameFragment, Kind = (int)affix.Kind };
			if (affix.Modifiers != null)
			{
				foreach (StatModifier modifier in affix.Modifiers)
				{
					if (modifier != null)
					{
						data.Modifiers.Add(new StatModifierSaveData
						{
							Stat = (int)modifier.Stat,
							Op = (int)modifier.Op,
							Value = modifier.Value,
						});
					}
				}
			}

			list.Add(data);
		}

		return list;
	}

	internal static Godot.Collections.Array<RolledAffix> BuildAffixes(List<RolledAffixSaveData> saved)
	{
		var affixes = new Godot.Collections.Array<RolledAffix>();
		if (saved == null)
		{
			return affixes;
		}

		foreach (RolledAffixSaveData data in saved)
		{
			if (!Enum.IsDefined(typeof(AffixKind), data.Kind)) { throw new InvalidOperationException("Invalid saved affix kind."); }
			var modifiers = new List<StatModifier>();
			foreach (StatModifierSaveData modifier in data.Modifiers)
			{
				if (!Enum.IsDefined(typeof(StatType), modifier.Stat) || !Enum.IsDefined(typeof(ModifierOp), modifier.Op)
					|| !float.IsFinite(modifier.Value)) { throw new InvalidOperationException("Invalid saved modifier."); }
				modifiers.Add(new StatModifier
				{
					Stat = (StatType)modifier.Stat,
					Op = (ModifierOp)modifier.Op,
					Value = modifier.Value,
				});
			}

			affixes.Add(new RolledAffix
			{
				NameFragment = data.NameFragment,
				Kind = (AffixKind)data.Kind,
				Modifiers = modifiers.ToArray(),
			});
		}

		return affixes;
	}

}