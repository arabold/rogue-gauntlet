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

	private static bool Deferred(Item item) => item.ResourcePath.StartsWith("res://scenes/items/tools/", StringComparison.Ordinal)
		|| item.ResourcePath.StartsWith("res://scenes/items/keys/", StringComparison.Ordinal);

	[TestCase]
	public void EveryDefinitionInitializesAndRestoresWithoutChangingAuthoredState()
	{
		var policy = GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
		AssertObject(policy).IsNotNull();
		AssertInt(Definitions().Count()).IsGreater(200);
		using var rng = GenerationRandom.Create(42, "catalog-validation");
		foreach (Item definition in Definitions())
		{
			AssertBool(definition.IsRuntimeInstance).IsFalse();
			AssertBool(!string.IsNullOrWhiteSpace(definition.Name)).IsTrue();
			AssertObject(definition.Scene).IsNotNull();
			Item runtime = ItemFactory.Create(definition, Deferred(definition) ? null : policy, 10, rng);
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
			if (definition is ConsumableItem consumable)
			{
				AssertBool(consumable.Buff != null || consumable is Scroll { Effect: not null }).IsTrue();
				if (definition is not FoodItem) { AssertInt(definition.MaxStackSize).IsEqual(20); }
			}
			if (Deferred(definition)) { AssertBool(definition is EquipableItem).IsFalse(); }
		}
	}

	[TestCase]
	public void EveryCustomExportHasItsOwnDefinitionAndInstantiableModelWrapper()
	{
		_ = Definitions().ToArray();
		foreach (string model in Paths("res://assets/custom-items", ".glb"))
		{
			string definitionPath = model.Replace("assets/custom-items", "scenes/items").Replace(".glb", ".tres");
			AssertBool(ResourceLoader.Exists(definitionPath)).IsTrue();
			Item item = GD.Load<Item>(definitionPath);
			AssertString(item.Scene.ResourcePath).IsEqual(definitionPath.Replace(".tres", ".tscn"));
			Node modelNode = item.Scene.Instantiate();
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
			var table = GD.Load<LootTable>(path);
			AssertObject(table).OverrideFailureMessage($"Cannot load table: {path}").IsNotNull();
			AssertInt(table.Items.Length).IsEqual(0);
			AssertInt(table.Groups.Length).IsEqual(7);
			AssertFloat(table.Groups.Sum(group => group.Weight)).IsEqual(100);
			foreach (var entry in table.Entries)
			{
				AssertBool(Deferred(entry.Item)).IsFalse();
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
		foreach (Item item in Definitions()) { AssertBool(paths.Contains(item.ResourcePath)).IsEqual(!Deferred(item)); }
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
			AssertObject(service.GetAppearance(item)).IsNotNull();
			AssertBool(service.GetDisplayName(item).Contains('{') || service.GetDisplayName(item).Contains('}')).IsFalse();
		}
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
}