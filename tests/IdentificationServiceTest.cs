namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="IdentificationService"/> behavior that does not depend on an
/// authored catalog: discovery de-duplication and the inert (no-catalog) defaults.
/// Disguise-assignment tests that construct <c>IdentityCatalog</c> resources can be
/// layered on later now that the harness is in place.
/// </summary>
[TestSuite]
public class IdentificationServiceTest
{
	[TestCase]
	public void IdentifyReturnsTrueOnceThenFalse()
	{
		var service = new IdentificationService();
		AssertBool(service.Identify("potion_red")).IsTrue();
		AssertBool(service.Identify("potion_red")).IsFalse();
	}

	[TestCase]
	public void IdentifyIgnoresNullAndEmpty()
	{
		var service = new IdentificationService();
		AssertBool(service.Identify(null)).IsFalse();
		AssertBool(service.Identify("")).IsFalse();
		AssertInt(service.GetIdentifiedTypeIds().Count).IsEqual(0);
	}

	[TestCase]
	public void IdentifiedTypeIdsTracksDiscoveries()
	{
		var service = new IdentificationService();
		service.Identify("scroll_a");
		service.Identify("scroll_b");
		service.Identify("scroll_a");

		AssertArray(service.GetIdentifiedTypeIds())
			.ContainsExactlyInAnyOrder("scroll_a", "scroll_b");
	}

	[TestCase]
	public void InertServiceTreatsNullItemAsIdentified()
	{
		// With no catalog initialized the service is inert: items read as themselves.
		var service = new IdentificationService();
		AssertBool(service.IsIdentified(null)).IsTrue();
	}

	[TestCase]
	public void NullItemHasEmptyDisplayNameAndNoAppearance()
	{
		var service = new IdentificationService();
		AssertString(service.GetDisplayName(null)).IsEqual("");
		AssertObject(service.GetAppearance(null)).IsNull();
	}
}