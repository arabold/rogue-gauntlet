namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Undiscovered loot never renders its model, particles, light or x-rays, for either world-spawn hierarchy.</summary>
[TestSuite, RequireGodotRuntime]
public class LootExplorationVisibilityTest
{
	private static LootableItem Pickup()
	{
		var pickup = TestFactories.Load<PackedScene>("res://scenes/items/lootable_item.tscn").Instantiate<LootableItem>();
		pickup.Item = ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/keys/key/key_gold_treasury.tres"));
		pickup.Animate = false;
		return pickup;
	}
	private static void Reveal(Level level, GeneratedFloor floor, Vector3 point)
	{
		TestFactories.RevealTileAt(level.Discovery, floor.Map, point);
		SignalBus.EmitRoomEntered(-1);
	}
	private static void AssertPresentation(LootableItem pickup, bool revealed)
	{
		AssertBool(pickup.IsVisibleInTree()).IsEqual(revealed);
		AssertBool(pickup.Pivot.IsVisibleInTree()).IsEqual(revealed);
		var beam = pickup.GetNode<LootBeam>("LootBeam");
		AssertBool(beam.IsVisibleInTree()).IsEqual(revealed);
		AssertBool(beam.GetNode<GpuParticles3D>("Sparks").Emitting).IsEqual(revealed);
		AssertBool(beam.GetNode<OmniLight3D>("Light").IsVisibleInTree()).IsEqual(revealed);
		if (!revealed)
		{
			AssertBool(pickup.FindChildren("*", "GeometryInstance3D", true, false)
				.OfType<GeometryInstance3D>().All(mesh => !mesh.IsVisibleInTree())).IsTrue();
		}
	}
	[TestCase(false), TestCase(true)]
	public async Task GeneratedAndRuntimeLootStayHiddenUntilTheirOwnTileIsExplored(bool runtimeWorld)
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var (level, map) = TestFactories.UngeneratedLevel();
		var pickup = Pickup(); pickup.Position = new Vector3(8, 0, 8);
		if (runtimeWorld) level.AddWorldNode(pickup);
		else map.GetNode<Node3D>("NavigationRegion3D").AddChild(pickup);
		// The pickup becomes ready before the map exists. It must fail closed.
		TestFactories.AddToSceneTree(level);
		AssertPresentation(pickup, false);
		await runner.SimulateFrames(3);
		AssertPresentation(pickup, false); // The map has not been generated yet either.
		var floor = TestFactories.StartEmptyFloor(level, map);
		await runner.SimulateFrames(3);
		AssertPresentation(pickup, false);
		var helpers = pickup.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
			.Where(m => VisualHelper.IsHelper(m)).ToArray();
		AssertBool(helpers.Length > 0).IsTrue(); AssertBool(helpers.All(m => !m.Visible)).IsTrue();
		// Uncovering a neighboring cell must not expose this pickup or its tall particles.
		Reveal(level, floor, pickup.GlobalPosition + Vector3.Right * 4);
		await runner.SimulateFrames(2); AssertPresentation(pickup, false);
		Reveal(level, floor, pickup.GlobalPosition);
		await runner.SimulateFrames(3); AssertPresentation(pickup, true);
		AssertBool(helpers.Any(m => m.Visible)).IsEqual(GameDebug.LootXrayEnabled);
		// Moving into unexplored space must suppress even previously running particles and lights.
		pickup.GlobalPosition += Vector3.Back * 4;
		await runner.SimulateFrames(2); AssertPresentation(pickup, false);
	}
	[TestCase]
	public async Task StandalonePreviewsRemainVisibleAndParticleActivationDoesNotRestartEveryFrame()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var pickup = TestFactories.AddToSceneTree(Pickup());
		await runner.SimulateFrames(3); AssertPresentation(pickup, true);
		var beam = pickup.GetNode<LootBeam>("LootBeam");
		beam.SetActive(false);
		AssertBool(beam.GetNode<GpuParticles3D>("Sparks").Emitting).IsFalse();
		AssertBool(beam.GetNode<OmniLight3D>("Light").Visible).IsFalse();
		beam.SetActive(true); beam.SetActive(true);
		AssertBool(beam.GetNode<GpuParticles3D>("Sparks").Emitting).IsTrue();
		AssertBool(beam.GetNode<OmniLight3D>("Light").Visible).IsTrue();
	}
}