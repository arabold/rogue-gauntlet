namespace RogueGauntlet.Tests;

using Godot;
using System.Threading.Tasks;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Live appearance edits update marks without rebuilding unchanged procedural textures.</summary>
[TestSuite, RequireGodotRuntime]
public class SurfaceMarkTest
{
	[TestCase]
	public void WorldOverrideSuspendsFixtureMarksAndKeepsAuthoredHiddenMarksHidden()
	{
		var levelScene = TestFactories.Load<PackedScene>("res://scenes/levels/level.tscn");
		var markScene = TestFactories.Load<PackedScene>("res://scenes/effects/surface_marks/wet_mark.tscn");
		var level = levelScene.Instantiate<Level>();
		level.AutoGenerateOnReady = false;
		level.Lighting.DisabledEffects = EnvironmentalEffect.SurfaceMarks;
		var mark = markScene.Instantiate<SurfaceMark>();
		var hidden = markScene.Instantiate<SurfaceMark>();
		hidden.Visible = false;
		level.AddChild(mark);
		level.AddChild(hidden);
		TestFactories.AddToSceneTree(level);
		AssertBool(mark.IsVisibleInTree() || mark.CanProcess()).IsFalse();
		AssertBool(mark.GetNode<CameraSheen>("CameraSheen").CanProcess()).IsFalse();
		level.Lighting.DisabledEffects = EnvironmentalEffect.None;
		AssertBool(mark.IsVisibleInTree() && mark.CanProcess()).IsTrue();
		AssertBool(hidden.IsVisibleInTree()).IsFalse();
		AssertBool(mark.GetNode<CameraSheen>("CameraSheen").CanProcess()).IsTrue();
	}

	[TestCase]
	public void TintAndBlendEditsKeepSharedTexturesWhileUpdatingTheMark()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/effects/surface_marks/wet_mark.tscn");
		var mark = TestFactories.AddToSceneTree(scene.Instantiate<SurfaceMark>());
		mark.Profile = (SurfaceMarkProfile)mark.Profile.Duplicate();
		var albedo = mark.TextureAlbedo;
		var orm = mark.TextureOrm;
		var tint = new Color(.1f, .2f, .3f, .8f);
		mark.Profile.Tint = tint;
		mark.Profile.AlbedoMix = .25f;
		AssertThat(mark.Modulate).IsEqual(tint);
		AssertFloat(mark.AlbedoMix).IsEqual(.25f);
		AssertObject(mark.TextureAlbedo).IsSame(albedo);
		AssertObject(mark.TextureOrm).IsSame(orm);
	}

	[TestCase]
	public async Task CameraSheenFollowsTheViewAndPreservesTheMarkAlpha()
	{
		var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
		var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 10, Current = true };
		var scene = TestFactories.Load<PackedScene>("res://scenes/effects/surface_marks/wet_mark.tscn");
		var mark = scene.Instantiate<SurfaceMark>();
		mark.Position = new Vector3(0, 0, -5);
		viewport.AddChild(camera);
		viewport.AddChild(mark);
		TestFactories.AddToSceneTree(viewport);
		await viewport.ToSignal(viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
		var sheen = mark.GetNode<CameraSheen>("CameraSheen");
		sheen.SpacingRange = new Vector2(3f, 3f);
		sheen.GlintLength = 1f;
		sheen.Strength = .5f;
		sheen._Process(0);
		float low = mark.Modulate.R, high = low;
		for (int i = 1; i <= 36; i++)
		{
			camera.Position = new Vector3(i * .1f, 0, 0);
			await viewport.ToSignal(viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
			await viewport.ToSignal(viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
			sheen._Process(0);
			low = Mathf.Min(low, mark.Modulate.R);
			high = Mathf.Max(high, mark.Modulate.R);
		}
		AssertBool(high > low + .1f).IsTrue();
		AssertFloat(mark.Modulate.A).IsEqual(mark.Profile.Tint.A);
		Color stationary = mark.Modulate;
		await viewport.ToSignal(viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
		sheen._Process(0);
		AssertThat(mark.Modulate).IsEqual(stationary);
	}
}