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
	private static async Task Frames(SceneTree tree, int count)
	{
		for (int frame = 0; frame < count; frame++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}

	private static ExplorationGateComponent Gate(Node torch) => torch.FindChildren("*", nameof(ExplorationGateComponent), true, false)
		.OfType<ExplorationGateComponent>().Single();
	[TestCase]
	public async Task TorchStaysDarkUntilItsOwnTileIsExplored()
	{
		var (level, map) = TestFactories.UngeneratedLevel();
		var region = map.GetNode<Node3D>("FloorContent");
		var lit = TestFactories.Torch(TestFactories.LitTorch, new Vector3(8, 2.2f, 8), Mathf.Pi / 2);
		var unlit = TestFactories.Torch(TestFactories.UnlitTorch, new Vector3(-8, 2.2f, -8), 0);
		region.AddChild(lit);
		region.AddChild(unlit);
		TestFactories.AddToSceneTree(level);
		var tree = level.GetTree();
		TestFactories.AssertTorchEffects(lit, false); // An initializing level fails closed.
		var floor = TestFactories.StartEmptyFloor(level, map);
		await Frames(tree, 3);
		TestFactories.AssertTorchEffects(lit, false);
		// Uncovering a neighboring cell must not expose the flame rising above this cell's cap.
		TestFactories.RevealTileAt(level.Discovery, floor.Map, Gate(lit).GlobalPosition + Vector3.Right * 4);
		await Frames(tree, 2); TestFactories.AssertTorchEffects(lit, false);
		TestFactories.RevealTileAt(level.Discovery, floor.Map, Gate(lit).GlobalPosition);
		TestFactories.RevealTileAt(level.Discovery, floor.Map, Gate(unlit).GlobalPosition);
		await Frames(tree, 2);
		TestFactories.AssertTorchEffects(lit, true);
		AssertBool(lit.IsVisibleInTree()).IsTrue();
		// The gate restores the torch, not the unlit variant's authored-off flame and light.
		AssertBool(unlit.IsVisibleInTree()).IsTrue();
		TestFactories.AssertTorchEffects(unlit, false);
	}
	[TestCase]
	public async Task StandaloneTorchPreviewIsLit()
	{
		var torch = TestFactories.AddToSceneTree(TestFactories.Torch());
		await Frames(torch.GetTree(), 2);
		TestFactories.AssertTorchEffects(torch, true);
	}
	[TestCase(false), TestCase(true)]
	public async Task EveryGeneratedTorchLightsOnceItsRoomIsExplored(bool debugRevealAll)
	{
		var (runner, level) = await TestFactories.GenerateLevel(23, 1);
		using var scope = runner;
		var gates = level.MapGenerator.FindChildren("*", nameof(ExplorationGateComponent), true, false).OfType<ExplorationGateComponent>().ToArray();
		AssertBool(gates.Length > 0).IsTrue();
		AssertBool(gates.Any(g => !g.Target.Visible)).IsTrue(); // Torches beyond the start room are still in the fog.
		// A gate probing the wall plane or the void behind it would stay dark forever.
		// The debug reveal must mark tiles explored too, not just clear the caps.
		if (debugRevealAll)
		{
			level.Discovery.RevealAll();
		}
		else
		{
			for (int room = 0; room < level.Floor.Rooms.Count; room++) level.Discovery.RevealRoom(room);
			foreach (var gate in gates.Where(g => g.GetAncestorOrNull<Room>() == null))
				TestFactories.RevealTileAt(level.Discovery, level.Floor.Map, gate.GlobalPosition);
		}
		await runner.SimulateFrames(2, 20);
		AssertArray(gates.Where(g => !g.Target.Visible).Select(g => g.GetPath().ToString()).ToArray()).IsEmpty();
	}
}