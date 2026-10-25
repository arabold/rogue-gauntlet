namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Debug darkness extinguishes fixtures independently of exploration and carried lights.</summary>
[TestSuite, RequireGodotRuntime]
public class DarkLevelTest
{
	private static Node3D Torch(string path, Vector3 position, float yaw)
	{
		var torch = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		torch.Position = position;
		torch.RotateY(yaw);
		return torch;
	}
	private static void AssertEffects(Node torch, bool visible)
	{
		AssertBool(torch.FindChild("Fire", true, false) is Node3D { } fire && fire.IsVisibleInTree()).IsEqual(visible);
		AssertBool(torch.FindChild("FlickerLight", true, false) is OmniLight3D { } light && light.IsVisibleInTree()).IsEqual(visible);
	}
	/// <summary>The actual debug menu changes floor illumination without relighting unlit fixtures or changing exploration.</summary>
	[TestCase]
	public async Task DebugDarkLevelToggleRestoresFixturesAndAmbientLight()
	{
		using var runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(5);
		var scene = runner.Scene();
		var level = scene.GetNode<Level>("Level");
		var environment = scene.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
		float ambient = environment.AmbientLightEnergy;
		AssertBool(ambient > 0).IsTrue();
		var fixtures = level.MapGenerator.FindChildren("*", nameof(EnvironmentalLightComponent), true, false)
			.OfType<EnvironmentalLightComponent>().ToArray();
		AssertBool(fixtures.Length > 0).IsTrue();
		bool[] lights = fixtures.Select(f => f.Light.Visible).ToArray();
		bool[] flames = fixtures.Select(f => f.Flame.Visible).ToArray();
		var player = scene.GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var playerLights = player.FindChildren("*", "Light3D", true, false).OfType<Light3D>().ToArray();
		AssertBool(playerLights.Length > 0).IsTrue();
		bool[] playerVisibility = playerLights.Select(l => l.Visible).ToArray();
		var menu = scene.FindChildren("*", "HBoxContainer", true, false).OfType<DebugMenu>().Single();
		var popup = menu.GetNode<MenuButton>("%DebugMenuButton").GetPopup();
		int index = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Dark level");
		popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(index));
		AssertBool(level.IsDark).IsTrue();
		AssertBool(popup.IsItemChecked(index)).IsTrue();
		AssertFloat(environment.AmbientLightEnergy).IsEqual(0f);
		AssertBool(fixtures.All(f => !f.Light.Visible && !f.Flame.Visible)).IsTrue();
		AssertArray(playerLights.Select(l => l.Visible).ToArray()).IsEqual(playerVisibility);

		// Fixtures added while dark must start extinguished too, including their flames.
		var lit = Torch("res://scenes/props/torch_mounted.tscn", Vector3.Zero, 0);
		var unlit = Torch("res://scenes/levels/arrangements/wall/wall_torch_unlit.tscn", Vector3.Zero, 0);
		lit.Transform = ((Node3D)fixtures[0].GetParent()).GlobalTransform;
		unlit.Transform = ((Node3D)fixtures[0].GetParent()).GlobalTransform;
		level.AddWorldNode(lit);
		level.AddWorldNode(unlit);
		level.MapGenerator.RevealAllFog();
		await runner.SimulateFrames(2);
		AssertEffects(lit, false);
		AssertEffects(unlit, false);
		AssertBool(fixtures.All(f => !f.Light.Visible && !f.Flame.Visible)).IsTrue();

		popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(index));
		AssertBool(level.IsDark).IsFalse();
		AssertBool(popup.IsItemChecked(index)).IsFalse();
		AssertFloat(environment.AmbientLightEnergy).IsEqual(ambient);
		AssertArray(fixtures.Select(f => f.Light.Visible).ToArray()).IsEqual(lights);
		AssertArray(fixtures.Select(f => f.Flame.Visible).ToArray()).IsEqual(flames);
		AssertEffects(lit, true);
		AssertEffects(unlit, false);
		AssertArray(playerLights.Select(l => l.Visible).ToArray()).IsEqual(playerVisibility);
	}

}
