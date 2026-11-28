namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The map harness previews a realm's first depth with a floor profile from the production generator.</summary>
[TestSuite, RequireGodotRuntime]
public class MapGenerationHarnessTest
{
	private const string Scene = "res://scenes/debug/maps/map_generation_harness.tscn";

	[TestCase]
	public async Task TheSelectedRealmAndProfileBuildAConnectedFloor()
	{
		using var runner = TestFactories.LoadScene(Scene);
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var harness = (MapGenerationHarness)runner.Scene();
		AssertString(harness.LastError).IsEmpty();
		// A realm other than the first: the floor holds the selected realm's rooms and corridors.
		harness.SelectRealm(1);
		harness.Regenerate();
		await TestFactories.AwaitPhysicsFrames(runner, 1);
		AssertString(harness.LastError).IsEmpty();
		AssertObject(harness.Generator.CurrentRealm).IsSame(harness.Realm);

		// Picking another floor profile previews a floor built from it. Every profile's floors are swept for their
		// contracts in GeneratedFloorSweepTest; here one switch proves the picker reaches the generator.
		AssertInt(harness.Profiles.Count).IsGreaterEqual(4);
		int last = harness.Profiles.Count - 1;
		harness.SelectProfile(last);
		harness.Regenerate();
		await TestFactories.AwaitPhysicsFrames(runner, 1);
		AssertString(harness.LastError).OverrideFailureMessage($"profile {harness.Profiles[last].Path}: {harness.LastError}").IsEmpty();
		AssertObject(harness.Generator.Profile).IsSame(harness.Profiles[last].Resource);
		AssertArray(TestFactories.ConnectivityFailures(harness.Generator).ToArray()).IsEmpty();
	}
}