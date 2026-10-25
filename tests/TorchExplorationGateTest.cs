namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Torch flames rise above the fog caps, so undiscovered torches hide flame and light until their own tile is explored.</summary>
[TestSuite, RequireGodotRuntime]
public class TorchExplorationGateTest
{
	private static Node3D Torch(string path, Vector3 position, float yaw)
	{
		var torch = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		torch.Position = position;
		torch.RotateY(yaw);
		return torch;
	}
	private static ExplorationGateComponent Gate(Node torch) => torch.FindChildren("*", nameof(ExplorationGateComponent), true, false)
		.OfType<ExplorationGateComponent>().Single();
	private static void AssertEffects(Node torch, bool visible)
	{
		AssertBool(torch.FindChild("Fire", true, false) is Node3D { } fire && fire.IsVisibleInTree()).IsEqual(visible);
		AssertBool(torch.FindChild("FlickerLight", true, false) is OmniLight3D { } light && light.IsVisibleInTree()).IsEqual(visible);
	}
	[TestCase]
	public async Task TorchStaysDarkUntilItsOwnTileIsExplored()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/world_effect_harness.tscn", true);
		await runner.SimulateFrames(2);
		var (level, map) = TestFactories.UngeneratedLevel();
		var region = map.GetNode<Node3D>("NavigationRegion3D");
		var lit = Torch("res://scenes/props/torch_mounted.tscn", new Vector3(8, 2.2f, 8), Mathf.Pi / 2);
		var unlit = Torch("res://scenes/levels/arrangements/wall/wall_torch_unlit.tscn", new Vector3(-8, 2.2f, -8), 0);
		region.AddChild(lit);
		region.AddChild(unlit);
		TestFactories.AddToSceneTree(level);
		AssertEffects(lit, false); // An initializing level fails closed.
		map.Map = new MapData(20, 20);
		await runner.SimulateFrames(3);
		AssertEffects(lit, false);
		// Uncovering a neighboring cell must not expose the flame rising above this cell's cap.
		TestFactories.RevealTileAt(map, Gate(lit).GlobalPosition + Vector3.Right * 4);
		await runner.SimulateFrames(2); AssertEffects(lit, false);
		TestFactories.RevealTileAt(map, Gate(lit).GlobalPosition);
		TestFactories.RevealTileAt(map, Gate(unlit).GlobalPosition);
		await runner.SimulateFrames(2);
		AssertEffects(lit, true);
		AssertBool(lit.IsVisibleInTree()).IsTrue();
		// The gate restores the torch, not the unlit variant's authored-off flame and light.
		AssertBool(unlit.IsVisibleInTree()).IsTrue();
		AssertEffects(unlit, false);
	}
	[TestCase]
	public async Task StandaloneTorchPreviewIsLit()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/world_effect_harness.tscn", true);
		await runner.SimulateFrames(2);
		var torch = TestFactories.AddToSceneTree(Torch("res://scenes/props/torch_mounted.tscn", Vector3.Zero, 0));
		await runner.SimulateFrames(2);
		AssertEffects(torch, true);
	}
	[TestCase(false), TestCase(true)]
	public async Task EveryGeneratedTorchLightsOnceItsRoomIsExplored(bool debugRevealAll)
	{
		using var runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(5);
		var map = runner.Scene().GetNode<Level>("Level").MapGenerator;
		var gates = map.FindChildren("*", nameof(ExplorationGateComponent), true, false).OfType<ExplorationGateComponent>().ToArray();
		AssertBool(gates.Length > 0).IsTrue();
		AssertBool(gates.Any(g => !g.Target.Visible)).IsTrue(); // Torches beyond the start room are still in the fog.
		// A gate probing the wall plane or the void behind it would stay dark forever.
		// The debug reveal must mark tiles explored too, not just clear the caps.
		if (debugRevealAll) map.RevealAllFog();
		else for (int room = 0; room < map.RoomRegions.Count; room++) map.RevealRoom(room, false);
		await runner.SimulateFrames(2);
		AssertArray(gates.Where(g => !g.Target.Visible).Select(g => g.GetPath().ToString()).ToArray()).IsEmpty();
	}
}