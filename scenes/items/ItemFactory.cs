using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Copies definitions explicitly. Creation may roll state; copies and restoration never do.
/// Quantity expansion gives every individual piece its own identity, including hidden food state.
/// </summary>
public static class ItemFactory
{
	public static Item CopyDefinition(Item definition, string instanceId = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		var item = (Item)definition.Duplicate(true);
		item.SourceDefinitionPath = definition.DefinitionPath;
		item.InstanceId = instanceId ?? Guid.NewGuid().ToString("N");
		if (item is FoodItem food && definition is FoodItem source) { food.RestorePoison(source.IsPoisoned); }
		return item;
	}

	public static Item Create(Item definition, ItemCreationPolicy policy = null, uint depth = 1,
		RandomNumberGenerator rng = null, string instanceId = null)
	{
		if (definition == null || definition.IsRuntimeInstance)
		{
			throw new ArgumentException("Creation requires an authored item definition.", nameof(definition));
		}
		if (policy?.RollEquipment == true && policy.QualityLadder == null)
		{
			throw new ArgumentException("Equipment rolling requires an authored quality ladder.", nameof(policy));
		}
		if (policy?.RollEquipment == true && definition is EquipableItem { RollTable: null })
		{
			throw new ArgumentException("Rolled equipment requires an authored roll table.", nameof(definition));
		}
		if (policy?.RollEquipment == true && definition is EquipableItem equipable
			&& (equipable.AlwaysCursed || policy.CurseChance > 0) && (policy.CursePenalties?.Length ?? 0) == 0)
		{
			throw new ArgumentException("Cursed equipment requires authored curse penalties.", nameof(policy));
		}
		if ((policy?.RollEquipment == true && definition is EquipableItem
			|| policy?.RollFoodContamination == true && definition is FoodItem) && rng == null)
		{
			throw new ArgumentNullException(nameof(rng));
		}
		if (definition is FoodItem foodDefinition && policy?.RollFoodContamination == true
			&& foodDefinition.PoisonChance > 0 && foodDefinition.PoisonEffect == null)
		{ throw new ArgumentException("Contaminated food requires a poison effect.", nameof(definition)); }
		Item item = CopyDefinition(definition, instanceId);
		if (item is EquipableItem equipment && policy?.RollEquipment == true)
		{
			LootRoller.ApplyRoll(equipment, depth, rng, policy);
		}
		if (item is FoodItem food)
		{
			food.RestorePoison(policy?.RollFoodContamination == true && rng.Randf() < food.PoisonChance);
		}
		return item;
	}

	public static IReadOnlyList<ItemQuantity> CreateBatch(Item definition, int quantity,
		ItemCreationPolicy policy = null, uint depth = 1, RandomNumberGenerator rng = null, string idPrefix = null)
	{
		if (quantity <= 0) { throw new ArgumentOutOfRangeException(nameof(quantity)); }
		var entries = new List<ItemQuantity>();
		int cap = definition.CanStack ? definition.MaxStackSize : 1;
		idPrefix ??= Guid.NewGuid().ToString("N");
		while (quantity > 0)
		{
			int count = Math.Min(quantity, cap);
			entries.Add(new(Create(definition, policy, depth, rng, $"{idPrefix}:{entries.Count}"), count));
			quantity -= count;
		}
		return entries;
	}
}