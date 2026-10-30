namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Validates authored content against the production factory, codec, loot and identity systems.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemCatalogTest
{
	private static IEnumerable<string> Paths(string folder, string extension) =>
		Directory.EnumerateFiles(ProjectSettings.GlobalizePath(folder), "*" + extension, SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.Ordinal).Select(path => "res://" + Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"), path));

	// Retain the catalog roots throughout this audit rather than repeatedly releasing
	// and reloading hundreds of interconnected native Resource wrappers between cases.
	private Resource[] _catalogRoots;
	private IEnumerable<Item> Definitions()
	{
		_catalogRoots ??= Paths("res://scenes/items", ".tres")
			.Select(path => ResourceLoader.Load<Resource>(path)).ToArray();
		return _catalogRoots.OfType<Item>();
	}

	private static bool ExcludedFromLoot(Item item) => item.ResourcePath.StartsWith("res://scenes/items/tools/", StringComparison.Ordinal)
		|| item.ResourcePath.StartsWith("res://scenes/items/keys/", StringComparison.Ordinal);

	[TestCase]
	public void EveryDefinitionInitializesAndRestoresWithoutChangingAuthoredState()
	{
		var policy = GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
		AssertObject(policy).IsNotNull();
		AssertInt(Definitions().Count()).IsGreater(180);
		using var rng = GenerationRandom.Create(42, "catalog-validation");
		foreach (Item definition in Definitions())
		{
			AssertBool(definition.IsRuntimeInstance).IsFalse();
			AssertBool(!string.IsNullOrWhiteSpace(definition.Name)).IsTrue();
			// Identity types only ever show their run appearance, so they carry no model of their own.
			if (definition is IIdentifiable { HasIdentity: true }) { AssertObject(definition.Scene).IsNull(); }
			else { AssertObject(definition.Scene).IsNotNull(); }
			Item runtime = ItemFactory.Create(definition, ExcludedFromLoot(definition) ? null : policy, 10, rng);
			AssertString(runtime.DefinitionPath).IsEqual(definition.ResourcePath);
			var record = ItemSaveCodec.Capture(runtime, 1);
			var restored = ItemSaveCodec.Restore(record);
			AssertString(JsonSerializer.Serialize(ItemSaveCodec.Capture(restored, 1))).IsEqual(JsonSerializer.Serialize(record));
			AssertBool(definition.IsRuntimeInstance).IsFalse();
			if (definition is FoodItem food)
			{
				AssertBool(food.CanStack || food.IsPoisoned).IsFalse();
				AssertObject(food.PoisonEffect).IsNotNull();
			}
			if (definition is BuffedItem { Buff: StatModifierBuff statBuff })
			{
				AssertArray(statBuff.Validate()).OverrideFailureMessage($"{definition.ResourcePath} has an invalid stat buff").IsEmpty();
			}
			if (definition is ConsumableItem consumable)
			{
				AssertBool(consumable.Buff != null || consumable is Scroll { Effect: not null }).IsTrue();
				if (definition is not FoodItem) { AssertInt(definition.MaxStackSize).IsEqual(20); }
			}
			if (ExcludedFromLoot(definition) && definition is not PortableLightItem) { AssertBool(definition is EquipableItem).IsFalse(); }
		}
	}

	[TestCase]
	public void EveryCustomExportIsADisguiseOrADefinitionModelButNeverBoth()
	{
		var catalog = GD.Load<IdentityCatalog>("res://scenes/items/identity/identity_catalog.tres");
		// A model that is a disguise must belong to exactly one appearance and never also be a definition's own model,
		// or two items would share a look; every other custom export must be some definition's model.
		string[] appearanceModels = catalog.Categories
			.SelectMany(category => category.AppearancePool.Appearances)
			.SelectMany(appearance => appearance.Models.Values)
			.Select(scene => scene.ResourcePath)
			.ToArray();
		var definitionModels = Definitions().Where(item => item.Scene != null).Select(item => item.Scene.ResourcePath).ToHashSet();
		foreach (string model in Paths("res://assets/custom-items", ".glb"))
		{
			string wrapperPath = model.Replace("assets/custom-items", "scenes/items").Replace(".glb", ".tscn");
			AssertBool(ResourceLoader.Exists(wrapperPath)).OverrideFailureMessage($"Missing wrapper {wrapperPath}").IsTrue();
			int appearanceUses = appearanceModels.Count(path => path == wrapperPath);
			bool definitionUse = definitionModels.Contains(wrapperPath);
			AssertBool(appearanceUses == 1 ^ definitionUse)
				.OverrideFailureMessage($"{wrapperPath}: {appearanceUses} appearances, definition model {definitionUse}").IsTrue();
			AssertInt(appearanceUses).IsLessEqual(1);

			Node modelNode = GD.Load<PackedScene>(wrapperPath).Instantiate();
			AssertInt(modelNode.FindChildren("*", "MeshInstance3D", true, false).Count).IsGreater(0);
			modelNode.Free();
		}
	}

	[TestCase]
	public void LootCoversEveryActiveDefinitionAndExcludesDeferredMechanics()
	{
		_ = Definitions().ToArray();
		var paths = new HashSet<string>();
		foreach (string path in Paths("res://scenes/items/loot", ".tres"))
		{
			// Pools are checked through the tables that weight them.
			if (ResourceLoader.Load<Resource>(path) is not LootTable table) { continue; }
			AssertArray(table.Validate()).OverrideFailureMessage($"Invalid table: {path}").IsEmpty();
			AssertInt(table.Items.Length).IsEqual(0);
			AssertInt(table.Groups.Length).IsEqual(7);
			AssertFloat(table.Groups.Sum(group => group.Weight)).IsEqual(100);
			foreach (var entry in table.Entries)
			{
				AssertBool(ExcludedFromLoot(entry.Item)).IsFalse();
				AssertBool(float.IsFinite(entry.Weight) && entry.Weight > 0 && entry.Quantity > 0).IsTrue();
				AssertBool(entry.MaxDepth == 0 || entry.MaxDepth >= entry.MinDepth).IsTrue();
				paths.Add(entry.Item.ResourcePath);
			}
			using var rng = GenerationRandom.Create(42, "catalog-loot");
			foreach (uint depth in new uint[] { 1, 3, 5, 8, 12, 25 })
			{
				for (int i = 0; i < 100; i++) { AssertBool(table.PickEntry(depth, rng)?.IsEligibleAt(depth) == true).IsTrue(); }
			}
		}
		foreach (Item item in Definitions()) { AssertBool(paths.Contains(item.ResourcePath)).IsEqual(!ExcludedFromLoot(item)); }
	}

	[TestCase]
	public void HiddenFamiliesHaveUniqueAppearancesAndCorrectTemplates()
	{
		_ = Definitions().ToArray();
		var catalog = GD.Load<IdentityCatalog>("res://scenes/items/identity/identity_catalog.tres");
		AssertObject(catalog).IsNotNull();
		var service = new IdentificationService();
		service.Initialize(42);
		foreach (var category in catalog.Categories)
		{
			var families = category.Types.OfType<IIdentifiable>().Select(item => item.TypeId).Distinct().ToArray();
			AssertBool(category.AppearancePool.Appearances.Count >= families.Length).IsTrue();
			AssertInt(category.AppearancePool.Appearances.Select(a => a.Descriptor).Distinct().Count())
				.IsEqual(category.AppearancePool.Appearances.Count);
		}
		foreach (var item in Definitions().OfType<IIdentifiable>().Where(item => item.HasIdentity))
		{
			ItemAppearance appearance = service.GetAppearance(item);
			AssertObject(appearance).IsNotNull();
			AssertObject(appearance.GetModel(item.AppearanceForm))
				.OverrideFailureMessage($"{item.TypeId} has no '{item.AppearanceForm}' model in its appearance").IsNotNull();
			AssertBool(service.GetDisplayName(item).Contains('{') || service.GetDisplayName(item).Contains('}')).IsFalse();
		}
	}

	[TestCase]
	public void JewelryHasOneDefinitionPerTypeGradedByQuality()
	{
		IdentifiableEquipableItem[] jewelry = Definitions().OfType<IdentifiableEquipableItem>().ToArray();
		AssertInt(jewelry.Count(item => item.IdentityCategory == "ring")).IsEqual(9);
		AssertInt(jewelry.Count(item => item.IdentityCategory == "amulet")).IsEqual(7);
		AssertInt(jewelry.Select(item => item.TypeId).Distinct().Count()).IsEqual(jewelry.Length);
		foreach (IdentifiableEquipableItem item in jewelry)
		{
			AssertObject(item.RollTable).OverrideFailureMessage($"{item.TypeId} has no roll table").IsNotNull();
			AssertBool(item.UnidentifiedNameTemplate.StartsWith("{descriptor} ")).IsTrue();
			AssertInt(item.IntrinsicModifiers.Length).IsGreater(0);
			AssertInt(item.Tier).IsEqual(1);
		}
	}

	[TestCase]
	public void EquipmentNamesLeaveConditionToQualityAndNameOneBaseTier()
	{
		var words = Paths("res://scenes/items/quality/tables", ".tres").Select(path => GD.Load<EquipmentRollTable>(path))
			.SelectMany(table => table.PoorAdjectives.Concat(table.SuperiorAdjectives)).Append("old").Append("common").Append("worn")
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		EquipableItem[] equipment = Definitions().OfType<EquipableItem>().Where(item => item is not IdentifiableEquipableItem).ToArray();
		foreach (EquipableItem item in equipment)
		{
			if (!ExcludedFromLoot(item))
			{ AssertObject(item.RollTable).OverrideFailureMessage($"{item.ResourcePath} has no roll table").IsNotNull(); }
			AssertBool(item.Name.Split(' ').Any(words.Contains)).OverrideFailureMessage($"'{item.Name}' carries a condition word").IsFalse();
		}
		// A base name stands for one base tier, so "Battle Axe" never names two different strengths.
		foreach (IGrouping<string, EquipableItem> sameName in equipment.GroupBy(item => item.Name))
		{
			AssertInt(sameName.Select(item => item.Tier).Distinct().Count())
				.OverrideFailureMessage($"'{sameName.Key}' names several tiers").IsEqual(1);
		}
	}

	[TestCase]
	public void EveryPotionTypeComesInThreeSizesSharingOneIdentity()
	{
		IdentifiableItem[] potions = Definitions().OfType<IdentifiableItem>()
			.Where(item => item.ResourcePath.StartsWith("res://scenes/items/potions/", StringComparison.Ordinal))
			.ToArray();
		AssertInt(potions.Length).IsEqual(27);
		foreach (var family in potions.GroupBy(potion => potion.TypeId))
		{
			AssertArray(family.Select(potion => potion.AppearanceForm).OrderBy(form => form).ToArray())
				.OverrideFailureMessage($"{family.Key} sizes").ContainsExactly("large", "medium", "small");
		}
	}

	[TestCase]
	public void EveryAuthoredPoisonCarriesThePoisonedCondition()
	{
		// A poison source authored without the condition would poison the player without the overlay.
		var poisoned = GD.Load<StatusCondition>("res://scenes/buffs/conditions/poisoned.tres");
		Item[] items = Definitions().ToArray();
		Buff[] poisons = items.OfType<BuffedItem>().Select(item => item.Buff)
			.Concat(items.OfType<FoodItem>().Select(item => (Buff)item.PoisonEffect))
			.Where(buff => buff is PoisonBuff)
			.ToArray();
		AssertInt(poisons.Length).IsGreaterEqual(4);
		AssertBool(poisons.All(buff => buff.Condition == poisoned)).IsTrue();
	}

	[TestCase]
	public void CustomCastingWeaponsUseRangedStrategiesAndScrollsAreReadable()
	{
		foreach (Item item in Definitions())
		{
			if (item.ResourcePath.StartsWith("res://scenes/items/scrolls/", StringComparison.Ordinal))
			{ AssertBool(item is Scroll).IsTrue(); }
			if (item is MagicStaff staff)
			{
				AssertObject(staff.CustomAttackDefinition?.ProjectileScene).IsNotNull();
				AssertBool(staff.CustomAttackDefinition.ProjectileScene != staff.Scene).IsTrue();
			}
			if (item.ResourcePath.Contains("crossbow", StringComparison.Ordinal) || item.ResourcePath.Contains("wand", StringComparison.Ordinal)) { AssertBool(item is RangedWeapon).IsTrue(); }
		}
	}

	[TestCase]
	public void EveryWandFiresAHomingMissileVolleyOfItsElement()
	{
		var wands = Definitions().OfType<MagicStaff>().Where(staff => staff.ResourcePath.Contains("wand", StringComparison.Ordinal)).ToArray();
		AssertInt(wands.Length).IsGreaterEqual(3);
		foreach (var wand in wands)
		{
			var attack = wand.CustomAttackDefinition;
			AssertString(attack.ResourcePath).OverrideFailureMessage(wand.ResourcePath).StartsWith("res://scenes/magic/missile/");
			AssertBool(attack.HomingTurnRate > 0 && attack.ProjectileCount > 1 && attack.HasValidFlight()).IsTrue();
			AssertObject(attack.Element).OverrideFailureMessage(wand.ResourcePath).IsEqual(Enum.Parse<DamageElement>(wand.Element.ToString()));
			AssertBool(wand.IsTwoHanded).IsFalse();
		}
	}
}