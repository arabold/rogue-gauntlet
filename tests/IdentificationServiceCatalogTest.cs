namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Integration tests for <see cref="IdentificationService"/> driven by the real
/// authored catalog. <see cref="IdentificationService.Initialize"/> always loads
/// <c>identity_catalog.tres</c> from a fixed path, so these need the Godot runtime
/// (resource loading + <c>RandomNumberGenerator</c>) and double as a regression
/// guard on the authored identity content.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class IdentificationServiceCatalogTest
{
	private const string CatalogPath = "res://scenes/items/identity/identity_catalog.tres";

	/// <summary>Loads the authored catalog, failing the test cleanly if it is missing.</summary>
	private static IdentityCatalog LoadCatalog()
	{
		IdentityCatalog catalog = ResourceLoader.Load<IdentityCatalog>(CatalogPath);
		AssertObject(catalog)
			.OverrideFailureMessage($"Could not load the identity catalog at '{CatalogPath}'.")
			.IsNotNull();
		return catalog;
	}

	/// <summary>Distinct identifiable type ids the catalog is expected to assign.</summary>
	private static List<string> CatalogTypeIds()
	{
		IdentityCatalog catalog = LoadCatalog();
		return catalog.Categories
			.Where(category => category?.Types != null)
			.SelectMany(category => category.Types)
			.Where(type => type != null && type.HasIdentity)
			.Select(type => type.TypeId)
			.Distinct()
			.ToList();
	}

	private static IdentifiableItem FirstIdentifiableItem()
	{
		IdentityCatalog catalog = LoadCatalog();
		return catalog.Categories
			.Where(category => category?.Types != null)
			.SelectMany(category => category.Types)
			.First(type => type != null && type.HasIdentity);
	}

	[TestCase]
	public void InitializeAssignsAnAppearanceToEveryCatalogType()
	{
		List<string> expectedTypeIds = CatalogTypeIds();
		AssertInt(expectedTypeIds.Count).OverrideFailureMessage(
			"The authored identity catalog has no identifiable types to assign.").IsGreater(0);

		var service = new IdentificationService();
		service.Initialize(42);

		IReadOnlyDictionary<string, string> assignment = service.GetAssignmentDescriptors();
		AssertInt(assignment.Count).IsEqual(expectedTypeIds.Count);
		foreach (string typeId in expectedTypeIds)
		{
			AssertBool(assignment.ContainsKey(typeId)).IsTrue();
		}
	}

	[TestCase]
	public void SameSeedProducesIdenticalAssignment()
	{
		var first = new IdentificationService();
		first.Initialize(1234);
		var second = new IdentificationService();
		second.Initialize(1234);

		AssertAssignmentsEqual(first.GetAssignmentDescriptors(), second.GetAssignmentDescriptors());
	}

	[TestCase]
	public void PersistedAssignmentsOverrideTheSeedBaseline()
	{
		// A saved run records its disguises; loading must reproduce them exactly even
		// under a different seed, otherwise an item's effect would change across saves.
		var saved = new IdentificationService();
		saved.Initialize(1000);
		IReadOnlyDictionary<string, string> persisted = saved.GetAssignmentDescriptors();

		var restored = new IdentificationService();
		restored.Initialize(9999, identifiedTypeIds: null, persistedAssignments: persisted);

		AssertAssignmentsEqual(persisted, restored.GetAssignmentDescriptors());
	}

	[TestCase]
	public void ItemReadsAsDisguisedUntilIdentified()
	{
		IdentifiableItem item = FirstIdentifiableItem();
		var service = new IdentificationService();
		service.Initialize(42);

		// Unidentified: not yet identified, shows a disguise rather than the true name.
		AssertBool(service.IsIdentified(item)).IsFalse();
		AssertObject(service.GetAppearance(item)).IsNotNull();
		string disguisedName = service.GetDisplayName(item);
		string trueName = string.IsNullOrEmpty(item.TrueName) ? item.Name : item.TrueName;
		AssertString(disguisedName).IsNotEqual(trueName);

		// Identifying the type reveals it: the display name becomes the true name.
		AssertBool(service.Identify(item.TypeId)).IsTrue();
		AssertBool(service.IsIdentified(item)).IsTrue();
		AssertString(service.GetDisplayName(item)).IsEqual(trueName);
	}

	private static void AssertAssignmentsEqual(
		IReadOnlyDictionary<string, string> expected,
		IReadOnlyDictionary<string, string> actual)
	{
		AssertInt(actual.Count).IsEqual(expected.Count);
		foreach (KeyValuePair<string, string> entry in expected)
		{
			AssertBool(actual.ContainsKey(entry.Key)).IsTrue();
			AssertString(actual[entry.Key]).IsEqual(entry.Value);
		}
	}
}