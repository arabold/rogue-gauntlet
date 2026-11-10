namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="IdentificationService"/> behavior that needs no authored catalog: discovery de-duplication and the inert
/// (no-catalog) answers for missing input. Catalog-driven disguises live in <see cref="IdentificationServiceCatalogTest"/>.
/// </summary>
[TestSuite]
public class IdentificationServiceTest
{
	[TestCase]
	public void IdentifyReportsOnlyTheFirstDiscoveryOfEachType()
	{
		var service = new IdentificationService();
		AssertBool(service.Identify("scroll_a")).IsTrue();
		AssertBool(service.Identify("scroll_b")).IsTrue();
		AssertBool(service.Identify("scroll_a")).IsFalse();

		AssertArray(service.GetIdentifiedTypeIds())
			.ContainsExactlyInAnyOrder("scroll_a", "scroll_b");
	}

	[TestCase]
	public void MissingInputIsInert()
	{
		var service = new IdentificationService();
		AssertBool(service.Identify(null)).IsFalse();
		AssertBool(service.Identify("")).IsFalse();
		AssertInt(service.GetIdentifiedTypeIds().Count).IsEqual(0);
		// With no catalog initialized, items read as themselves.
		AssertBool(service.IsIdentified(null)).IsTrue();
		AssertString(service.GetDisplayName(null)).IsEqual("");
		AssertObject(service.GetAppearance(null)).IsNull();
	}
}