namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Checks the authored chest's golden silhouette, toggle, opening and exploration behavior.</summary>
[TestSuite, RequireGodotRuntime]
public class ChestXrayTest
{
	private static MeshInstance3D[] Silhouettes(Chest chest) => chest.FindChildren("*", "MeshInstance3D", true, false)
		.OfType<MeshInstance3D>().Where(m => m.MaterialOverride == chest.Xray.OcclusionMaterial).ToArray();

	[TestCase]
	public async Task ChestHighlightsBothMeshesAndStopsWhenOpened()
	{
		bool original = GameDebug.LootXrayEnabled;
		try
		{
			GameDebug.LootXrayEnabled = true;
			var chest = TestFactories.AddToSceneTree(GD.Load<PackedScene>("res://scenes/props/chest.tscn").Instantiate<Chest>());
			await Settle(chest);
			AssertObject(chest.Xray).IsNotNull();
			AssertInt((int)chest.Xray.Category).IsEqual((int)XrayCategory.Loot);
			MeshInstance3D[] silhouettes = Silhouettes(chest);
			AssertInt(silhouettes.Length).IsEqual(2); // Body and lid each use the same depth-based occlusion shader as doors.
			AssertBool(silhouettes.All(m => m.Visible)).IsTrue();
			AssertInt(silhouettes.Count(m => ((ShaderMaterial)m.MaterialOverride).RenderPriority == 8)).IsEqual(2);
			foreach (var mesh in silhouettes.Where(m => ((ShaderMaterial)m.MaterialOverride).RenderPriority == 8))
			{
				Color tint = ((ShaderMaterial)mesh.MaterialOverride).GetShaderParameter("xray_color").AsColor();
				AssertBool(tint.IsEqualApprox(new Color(1, 0.72f, 0.3f, 0.85f))).IsTrue();
			}

			GameDebug.LootXrayEnabled = false;
			SignalBus.EmitXraySettingsChanged();
			AssertBool(silhouettes.All(m => !m.Visible)).IsTrue();
			GameDebug.LootXrayEnabled = true;
			SignalBus.EmitXraySettingsChanged();
			AssertBool(silhouettes.All(m => m.Visible)).IsTrue();

			// Use the real chest interaction callback: the indicator must stop at commit, before the lid tween ends.
			chest.GetNode<LootTableComponent>("LootTableComponent").RestoreClaimed(true);
			chest.Call("OnInteract", default(Variant));
			AssertBool(chest.IsOpen).IsTrue();
			AssertBool(silhouettes.All(m => !m.Visible)).IsTrue();
			SignalBus.EmitXraySettingsChanged();
			AssertBool(silhouettes.All(m => !m.Visible)).IsTrue();

			var restored = TestFactories.AddToSceneTree(GD.Load<PackedScene>("res://scenes/props/chest.tscn").Instantiate<Chest>());
			restored.RestoreOpenState();
			await Settle(restored);
			AssertBool(Silhouettes(restored).All(m => !m.Visible)).IsTrue();
		}
		finally
		{
			GameDebug.LootXrayEnabled = original;
			SignalBus.EmitXraySettingsChanged();
		}
	}

	[TestCase]
	public async Task ChestSilhouetteDoesNotRevealUnexploredTreasure()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(5);
		var level = runner.Scene().GetNode<Level>("Level");
		var map = level.MapGenerator;
		var chest = GD.Load<PackedScene>("res://scenes/props/chest.tscn").Instantiate<Chest>();
		// Pick a real floor cell that is still under fog and has a room to reveal.
		Vector3 point = map.FloorGridMap.GetUsedCells()
			.Select(cell => map.FloorGridMap.ToGlobal(map.FloorGridMap.MapToLocal(cell)))
			.First(p => !level.Discovery.IsRevealedAt(p) && map.Floor.RoomIdAt(map.Map.WorldToTile(p)) >= 0);
		chest.Position = point;
		level.AddChild(chest);
		await Settle(chest);
		AssertInt(Silhouettes(chest).Length).IsEqual(2);
		AssertBool(Silhouettes(chest).All(m => !m.Visible)).IsTrue();
		bool original = GameDebug.LootXrayEnabled;
		try
		{
			GameDebug.LootXrayEnabled = true;
			int roomId = map.Floor.RoomIdAt(map.Map.WorldToTile(point));
			level.Discovery.RevealRoom(roomId);
			SignalBus.EmitRoomEntered(roomId);
			AssertBool(Silhouettes(chest).All(m => m.Visible)).IsTrue();
		}
		finally
		{
			GameDebug.LootXrayEnabled = original;
			SignalBus.EmitXraySettingsChanged();
		}
	}

	private static async Task Settle(Node node)
	{
		await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
		await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}