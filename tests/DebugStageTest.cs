namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Shared arrangements keep wall visuals and physics in sync for every spell workbench.</summary>
[TestSuite, RequireGodotRuntime]
public partial class DebugStageTest
{
	[TestCase]
	public async Task ArrangementsChangeWallsWithoutReplacingTargets()
	{
		string[] scenes = ["fireball_preview", "earth_spell_preview", "air_spell_preview", "water_spell_preview"];
		foreach (string name in scenes)
		{
			using var runner = TestFactories.LoadScene($"res://scenes/debug/effects/{name}.tscn");
			await runner.SimulateFrames(4);
			var root = runner.Scene();
			var stage = root.GetNode<DebugStage>("DebugStage");
			var target = root.GetNode<Node3D>("Target0");
			AssertBool(stage.WestWall.Visible).IsTrue();
			AssertBool(stage.DefaultWalls.All(w => w.Visible && w.CollisionLayer == 1)).IsTrue();
			stage.SetArrangement(1);
			AssertBool(stage.WestWall.Visible && stage.WestWall.CollisionLayer == 1).IsTrue();
			AssertBool(stage.DefaultWalls.All(w => !w.Visible && w.CollisionLayer == 0)).IsTrue();
			stage.SetArrangement(2);
			AssertBool(!stage.WestWall.Visible && stage.WestWall.CollisionLayer == 0).IsTrue();
			await runner.SimulateFrames(3);
			AssertBool(WorldSight.IsClear(stage.GetWorld3D(), Vector3.Up, new Vector3(8, 1, 0))).IsTrue();
			stage.SetArrangement(3);
			AssertBool(stage.RoomWalls.All(w => w.Visible && w.CollisionLayer == 1)).IsTrue();
			await runner.SimulateFrames(3);
			foreach (Vector3 end in new[] { new Vector3(8, 1, 0), new Vector3(-8, 1, 0), new Vector3(0, 1, 8), new Vector3(0, 1, -8) })
				AssertBool(WorldSight.IsClear(stage.GetWorld3D(), Vector3.Up, end)).IsFalse();
			AssertBool(root.GetNode<Node3D>("Target0") == target).IsTrue();
			stage.SetArrangement(99);
			AssertInt(stage.Arrangement).IsEqual(3);
		}
	}

	[TestCase]
	public async Task FrozenEffectIsSelectableForPlayerAndMonsters()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (ActorItemEffectPreview)runner.Scene();
		int index = System.Array.FindIndex(preview.Effects, effect => effect.Id == "frozen");
		AssertBool(index >= 0).IsTrue();
		preview.SelectEffect(index);
		for (int subject = 0; subject < 3; subject++)
		{
			preview.SetSubject(subject);
			await runner.SimulateFrames(4);
			AssertInt(preview.InstanceCount).IsEqual(1);
			AssertBool(preview.LastResult == "").IsTrue();
			preview.ClearEffects();
			AssertInt(preview.InstanceCount).IsEqual(0);
		}
	}
}