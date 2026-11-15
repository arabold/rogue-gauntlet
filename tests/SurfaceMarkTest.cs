namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Live appearance edits update marks without rebuilding unchanged procedural textures.</summary>
[TestSuite, RequireGodotRuntime]
public class SurfaceMarkTest
{
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
}