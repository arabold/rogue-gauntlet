namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Opening a menu must not turn static slot previews into permanent 3D rendering workloads.</summary>
[TestSuite, RequireGodotRuntime]
public class ItemIconRenderingTest
{
	[TestCase]
	public async Task ReopeningIconsRestoresIdleAndAnimatedRenderingPolicies()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/ui/item_icon.tscn");
		var holder = new Control();
		var icon = scene.Instantiate<ItemIcon>();
		holder.AddChild(icon);
		TestFactories.AddToSceneTree(holder);
		for (int cycle = 0; cycle < 3; cycle++)
		{
			holder.Hide();
			await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
			AssertThat(icon.Preview.RenderTargetUpdateMode).IsEqual(SubViewport.UpdateMode.Disabled);
			holder.Show();
			await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
			await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
			// A renderer may already have consumed the one requested frame.
			AssertBool(icon.Preview.RenderTargetUpdateMode is SubViewport.UpdateMode.Once or SubViewport.UpdateMode.Disabled).IsTrue();
			AssertBool(icon.Preview.IsProcessing()).IsFalse();
		}
		icon.Preview.Animated = true;
		holder.Hide();
		holder.Show();
		await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
		await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
		AssertThat(icon.Preview.RenderTargetUpdateMode).IsEqual(SubViewport.UpdateMode.Always);
		AssertBool(icon.Preview.IsProcessing()).IsTrue();
		holder.Hide();
		holder.Show();
		holder.Hide();
		await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
		await holder.ToSignal(holder.GetTree(), SceneTree.SignalName.ProcessFrame);
		AssertThat(icon.Preview.RenderTargetUpdateMode).IsEqual(SubViewport.UpdateMode.Disabled);
	}
}