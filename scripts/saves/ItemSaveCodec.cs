using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
			data.Quality = (int)equipment.Quality;
			data.MagnitudeScale = equipment.MagnitudeScale;
			data.QualityAdjective = equipment.QualityAdjective;
			data.AttributesKnown = equipment.AttributesKnown;
			data.CursePenalty = CaptureModifiers(equipment.CursePenalty);
			data.CurseStatusKnown = equipment.CurseStatusKnown;
			data.Affixes = CaptureAffixes(equipment.Affixes);
		}
		if (item is MagicStaff staff) data.SpellPath = staff.BoundSpell?.ResourcePath ?? "";
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
			if (!Enum.IsDefined(typeof(ItemQuality), data.Quality) || !float.IsFinite(data.MagnitudeScale) || data.MagnitudeScale <= 0f)
			{ throw new InvalidOperationException("Invalid saved equipment quality."); }
			equipment.Quality = (ItemQuality)data.Quality;
			equipment.MagnitudeScale = data.MagnitudeScale;
			equipment.QualityAdjective = data.QualityAdjective ?? "";
			equipment.AttributesKnown = data.AttributesKnown;
			equipment.CursePenalty = BuildModifiers(data.CursePenalty);
			equipment.CurseStatusKnown = data.CurseStatusKnown;
			equipment.Affixes = BuildAffixes(data.Affixes);
		}
		if (item is MagicStaff staff) staff.BoundSpell = RestoreSpell(data.SpellPath, staff);
		if (item is FoodItem food)
		{
			if (data.Food == null) { throw new InvalidOperationException("Missing saved food state."); }
			food.RestorePoison(data.Food.IsPoisoned);
		}
		int cap = item.CanStack ? item.MaxStackSize : 1;
		if (data.Quantity > cap) { throw new InvalidOperationException("Invalid saved stack quantity."); }
		return item;
	}

	/// <summary>The saved spell, which must exist and support the weapon's tier.</summary>
	private static SpellDefinition RestoreSpell(string path, MagicStaff staff)
	{
		var spell = !string.IsNullOrEmpty(path) && ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as SpellDefinition : null;
		return staff.CanBind(spell) ? spell : throw new InvalidOperationException($"Invalid saved spell: {path}");
	}

	internal static List<RolledAffixSaveData> CaptureAffixes(Godot.Collections.Array<RolledAffix> affixes)
	{
		var list = new List<RolledAffixSaveData>();
		foreach (RolledAffix affix in affixes ?? new Godot.Collections.Array<RolledAffix>())
		{
			if (affix != null)
			{
				list.Add(CaptureAffix(affix));
			}
		}

		return list;
	}

	internal static Godot.Collections.Array<RolledAffix> BuildAffixes(List<RolledAffixSaveData> saved)
	{
		var affixes = new Godot.Collections.Array<RolledAffix>();
		foreach (RolledAffixSaveData data in saved ?? [])
		{
			affixes.Add(BuildAffix(data));
		}

		return affixes;
	}

	private static RolledAffixSaveData CaptureAffix(RolledAffix affix) => new()
	{
		NameFragment = affix.NameFragment,
		Group = affix.Group,
		Kind = (int)affix.Kind,
		Modifiers = CaptureModifiers(affix.Modifiers),
	};

	private static RolledAffix BuildAffix(RolledAffixSaveData data)
	{
		if (!Enum.IsDefined(typeof(AffixKind), data.Kind)) { throw new InvalidOperationException("Invalid saved affix kind."); }
		return new RolledAffix
		{
			NameFragment = data.NameFragment,
			Group = data.Group ?? "",
			Kind = (AffixKind)data.Kind,
			Modifiers = BuildModifiers(data.Modifiers),
		};
	}

	internal static List<StatModifierSaveData> CaptureModifiers(IEnumerable<StatModifier> modifiers) =>
		(modifiers ?? []).Where(modifier => modifier != null)
			.Select(modifier => new StatModifierSaveData { Stat = (int)modifier.Stat, Op = (int)modifier.Op, Value = modifier.Value })
			.ToList();

	internal static StatModifier[] BuildModifiers(List<StatModifierSaveData> saved) => (saved ?? []).Select(modifier =>
		Enum.IsDefined(typeof(StatType), modifier.Stat) && Enum.IsDefined(typeof(ModifierOp), modifier.Op) && float.IsFinite(modifier.Value)
			? new StatModifier { Stat = (StatType)modifier.Stat, Op = (ModifierOp)modifier.Op, Value = modifier.Value }
			: throw new InvalidOperationException("Invalid saved modifier.")).ToArray();

}
