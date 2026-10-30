namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Checks projected UI dimensions with a real viewport and the shared authored floating UI scenes.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ConstantScreenSize3DTest
{
	[TestCase]
	public async Task FloatingVisualsKeepTheirProjectedSizeAcrossZoomLevels()
	{
		var viewport = new SubViewport { Size = new Vector2I(960, 540), OwnWorld3D = true };
		var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 20f, Position = new Vector3(0, 0, 15), Current = true };
		viewport.AddChild(camera);
		var prompt = TestFactories.Load<PackedScene>("res://scenes/components/floating_label.tscn").Instantiate<FloatingLabel>();
		// A health bar scales through the nameplate it sits on.
		var plate = TestFactories.Load<PackedScene>("res://scenes/components/nameplate.tscn").Instantiate<Nameplate>();
		var health = TestFactories.Load<PackedScene>("res://scenes/components/floating_health_bar.tscn").Instantiate<FloatingHealthBar>();
		plate.AddChild(health);
		var title = new Label3D { Text = "Enemy", Position = Vector3.Up * 2, Scale = new Vector3(0.8f, 0.8f, 0.8f), PixelSize = 0.008f };
		title.AddChild(new ConstantScreenSize3D { Target = title });
		viewport.AddChild(prompt);
		viewport.AddChild(plate);
		viewport.AddChild(title);
		TestFactories.AddToSceneTree(viewport);
		await Frame(viewport);
		await Frame(viewport);

		Node3D[] visuals = [prompt, health, title];
		float[] widths = [ProjectedWidth(camera, prompt, 256 * prompt.PixelSize), ProjectedWidth(camera, health, 256 * health.PixelSize), ProjectedWidth(camera, title, 100 * title.PixelSize)];
		Vector3[] anchors = [prompt.GlobalPosition, health.GlobalPosition, title.GlobalPosition];
		foreach (float zoom in new[] { 10f, 32f, 20f, 10f, 20f })
		{
			camera.Size = zoom;
			await Frame(viewport);
			await Frame(viewport);
			AssertFloat(ProjectedWidth(camera, prompt, 256 * prompt.PixelSize)).IsEqualApprox(widths[0], 0.01f);
			AssertFloat(ProjectedWidth(camera, health, 256 * health.PixelSize)).IsEqualApprox(widths[1], 0.01f);
			AssertFloat(ProjectedWidth(camera, title, 100 * title.PixelSize)).IsEqualApprox(widths[2], 0.01f);
			for (int i = 0; i < visuals.Length; i++)
				AssertBool(visuals[i].GlobalPosition.IsEqualApprox(anchors[i])).IsTrue();
		}
		AssertVector(title.Scale).IsEqual(new Vector3(0.8f, 0.8f, 0.8f));
	}

	[TestCase]
	public void AuthoredTitlesAndPromptsHaveExplicitSizeCompensation()
	{
		foreach (var (path, labelPath) in new[]
		{
			("res://scenes/components/nameplate.tscn", "."),
			("res://scenes/props/door.tscn", "InteractiveComponent/FloatingLabel"),
			("res://scenes/debug/combat/combat_practice_target.tscn", "Label"),
		})
		{
			Node scene = AutoFree(TestFactories.Load<PackedScene>(path).Instantiate());
			Node3D label = scene.GetNode<Node3D>(labelPath);
			AssertObject(label.GetNode<ConstantScreenSize3D>("ConstantScreenSize3D").Target).IsSame(label);
		}
	}

	[TestCase]
	public async Task NameplateKeepsTheGapBetweenNameAndBarAcrossZoomLevels()
	{
		var viewport = new SubViewport { Size = new Vector2I(960, 540), OwnWorld3D = true };
		var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 20f, Position = new Vector3(0, 0, 15), Current = true };
		viewport.AddChild(camera);
		var plate = TestFactories.Load<PackedScene>("res://scenes/components/nameplate.tscn").Instantiate<Nameplate>();
		var bar = TestFactories.Load<PackedScene>("res://scenes/components/floating_health_bar.tscn").Instantiate<FloatingHealthBar>();
		plate.AddChild(bar);
		viewport.AddChild(plate);
		TestFactories.AddToSceneTree(viewport);
		await Frame(viewport);
		await Frame(viewport);

		float Gap() => camera.UnprojectPosition(plate.NameLabel.GlobalPosition).DistanceTo(camera.UnprojectPosition(bar.GlobalPosition));
		float gap = Gap();
		AssertFloat(gap).IsGreater(1f);
		foreach (float zoom in new[] { 10f, 32f, 20f })
		{
			camera.Size = zoom;
			await Frame(viewport);
			await Frame(viewport);
			AssertFloat(Gap()).IsEqualApprox(gap, 0.01f);
		}
	}

	/// <summary>Projects a visual's billboard width into pixels, including its world scale.</summary>
	private static float ProjectedWidth(Camera3D camera, Node3D visual, float width)
	{
		Vector3 halfWidth = camera.GlobalBasis.X * width * visual.GlobalBasis.Scale.X * 0.5f;
		return camera.UnprojectPosition(visual.GlobalPosition - halfWidth)
			.DistanceTo(camera.UnprojectPosition(visual.GlobalPosition + halfWidth));
	}

	private static async Task Frame(Node node) => await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
}