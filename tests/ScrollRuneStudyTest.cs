namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>
/// Every paper appearance owns a renderable glyph, and the rune study animates the ring without touching the run's
/// identification. Which glyph a read scroll shows is covered by <see cref="ScrollConsumptionEffectsTest"/>.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class ScrollRuneStudyTest
{
	private static IdentityCategory ScrollCategory() => TestFactories.Load<IdentityCatalog>("res://scenes/items/identity/identity_catalog.tres").Categories.Single(c => c.Category == "scroll");
	[TestCase]
	public void EveryPaperAppearanceHasItsOwnAuthoredRenderableGlyph()
	{
		var appearances = ScrollCategory().AppearancePool.Appearances;
		AssertInt(appearances.Count).IsGreater(0);
		AssertInt(appearances.Select(a => a.GlyphId).Distinct().Count()).IsEqual(appearances.Count);
		AssertInt(appearances.Select(a => a.GlyphMesh?.ResourcePath).Distinct().Count()).IsEqual(appearances.Count);
		foreach (var appearance in appearances)
		{
			AssertBool(appearance.GlyphId.IsEmpty).IsFalse(); AssertObject(appearance.GetModel("")).IsNotNull();
			AssertObject(appearance.GlyphMesh).IsNotNull(); AssertInt(appearance.GlyphMesh.GetSurfaceCount()).IsGreater(0);
			var size = appearance.GlyphMesh.GetAabb().Size;
			AssertFloat(size.X).IsGreater(.3f); AssertFloat(size.Y).IsGreater(.7f); AssertFloat(size.Z).IsLess(.001f); // Imported flat-mesh bounds include a tiny depth pad.
		}
	}
	[TestCase]
	public async Task SessionAssignmentCanBeCopiedWithoutIdentifyingOrReshufflingItems()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/runes/scroll_rune_study.tscn");
		await runner.SimulateFrames(2, 20); var study = (ScrollRuneStudy)runner.Scene();
		var run = GameSession.Instance.Run; var assignment = run.Identification.GetAssignmentDescriptors();
		var known = run.Identification.GetIdentifiedTypeIds().ToArray();
		study.UseSessionAssignment();
		AssertObject(study.MatchedGlyph).IsSame(run.Identification.GetAppearance(study.SelectedScroll).GlyphMesh);
		study.SetSeed(982);
		AssertArray(run.Identification.GetIdentifiedTypeIds().ToArray()).IsEqual(known);
		foreach (var pair in assignment) AssertString(run.Identification.GetAssignmentDescriptors()[pair.Key]).IsEqual(pair.Value);
	}
	[TestCase]
	public async Task RuneRingRotatesExpandsAndDissolvesWithoutSubstitutingOtherGlyphs()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/runes/scroll_rune_study.tscn");
		await runner.SimulateFrames(2, 20); var study = (ScrollRuneStudy)runner.Scene(); study.SetTime(.2f);
		var ring = study.Stage.GetChild<Node3D>(0);
		var glyphs = ring.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => m.Mesh == study.MatchedGlyph).ToArray();
		AssertInt(glyphs.Length).IsEqual(10);
		float radius = new Vector2(glyphs[0].Position.X, glyphs[0].Position.Z).Length();
		float angle = glyphs[0].GetParent<Node3D>().Rotation.Y;
		AssertBool(study.RingLight.LightColor.IsEqualApprox(study.Appearance.GlyphColor)).IsTrue();
		AssertBool(study.RingLight.ShadowEnabled).IsFalse();
		AssertFloat(study.RingLight.LightEnergy).IsGreater(0);
		study.SetTime(.6f);
		AssertFloat(new Vector2(glyphs[0].Position.X, glyphs[0].Position.Z).Length()).IsGreater(radius);
		AssertFloat(glyphs[0].GetParent<Node3D>().Rotation.Y).IsGreater(angle);
		AssertFloat(((ShaderMaterial)glyphs[0].MaterialOverride).GetShaderParameter("dissolution").AsSingle()).IsGreater(.5f);
		study.SetTime(.8f);
		AssertFloat(study.RingLight.LightEnergy).IsEqual(0);
		AssertFloat(((ShaderMaterial)glyphs[0].MaterialOverride).GetShaderParameter("envelope").AsSingle()).IsEqual(0);
	}

}