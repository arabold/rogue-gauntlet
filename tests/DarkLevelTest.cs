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
	/// <summary>The actual debug menu changes floor illumination without relighting unlit fixtures or changing exploration.</summary>
	[TestCase]
	public async Task DebugDarkLevelToggleRestoresFixturesAndAmbientLight()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5, 20);
		var scene = runner.Scene();
		var level = scene.GetNode<Level>("Level");
		var environment = level.Lighting.Environment;
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
		var playerLight = player.GetNode<PlayerLight>("Pivot/PlayerLight");
		AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Lit.Range);
		var menu = scene.FindChildren("*", "HBoxContainer", true, false).OfType<DebugMenu>().Single();
		var popup = menu.GetTool<LevelTool>().Popup;
		int index = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Dark level");
		popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(index));
		AssertBool(level.Lighting.IsDark).IsTrue();
		AssertBool(popup.IsItemChecked(index)).IsTrue();
		AssertFloat(environment.AmbientLightEnergy).IsEqual(0f);
		AssertBool(fixtures.All(f => !f.Light.Visible && !f.Flame.Visible)).IsTrue();
		AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Dark.Range);

		// Fixtures added while dark must start extinguished too, including their flames.
		var lit = TestFactories.Torch();
		var unlit = TestFactories.Torch(TestFactories.UnlitTorch);
		lit.Transform = ((Node3D)fixtures[0].GetParent()).GlobalTransform;
		unlit.Transform = ((Node3D)fixtures[0].GetParent()).GlobalTransform;
		level.AddWorldNode(lit);
		level.AddWorldNode(unlit);
		level.Discovery.RevealAll();
		await runner.SimulateFrames(2, 20);
		TestFactories.AssertTorchEffects(lit, false);
		TestFactories.AssertTorchEffects(unlit, false);
		AssertBool(fixtures.All(f => !f.Light.Visible && !f.Flame.Visible)).IsTrue();

		popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(index));
		AssertBool(level.Lighting.IsDark).IsFalse();
		AssertBool(popup.IsItemChecked(index)).IsFalse();
		AssertFloat(environment.AmbientLightEnergy).IsEqual(ambient);
		AssertArray(fixtures.Select(f => f.Light.Visible).ToArray()).IsEqual(lights);
		AssertArray(fixtures.Select(f => f.Flame.Visible).ToArray()).IsEqual(flames);
		TestFactories.AssertTorchEffects(lit, true);
		TestFactories.AssertTorchEffects(unlit, false);
		AssertFloat(playerLight.Light.OmniRange).IsEqual(playerLight.Lit.Range);
	}

}