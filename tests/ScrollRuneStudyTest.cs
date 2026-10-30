namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Authored glyph pairs stay with randomized paper appearances, including restored assignment descriptors.</summary>
[TestSuite, RequireGodotRuntime]
public class ScrollRuneStudyTest
{
	private static IdentityCategory ScrollCategory() => TestFactories.Load<IdentityCatalog>("res://scenes/items/identity/identity_catalog.tres").Categories.Single(c => c.Category == "scroll");
	[TestCase]
	public void EveryPaperAppearanceHasItsOwnAuthoredRenderableGlyph()
	{
		var appearances = ScrollCategory().AppearancePool.Appearances;
		AssertInt(appearances.Count).IsEqual(12);
		AssertInt(appearances.Select(a => a.GlyphId).Distinct().Count()).IsEqual(12);
		AssertInt(appearances.Select(a => a.GlyphMesh?.ResourcePath).Distinct().Count()).IsEqual(12);
		foreach (var appearance in appearances)
		{
			AssertBool(appearance.GlyphId.IsEmpty).IsFalse(); AssertObject(appearance.GetModel("")).IsNotNull();
			AssertObject(appearance.GlyphMesh).IsNotNull(); AssertInt(appearance.GlyphMesh.GetSurfaceCount()).IsGreater(0);
			var size = appearance.GlyphMesh.GetAabb().Size;
			AssertFloat(size.X).IsGreater(.3f); AssertFloat(size.Y).IsGreater(.7f); AssertFloat(size.Z).IsLess(.001f); // Imported flat-mesh bounds include a tiny depth pad.
		}
	}
	[TestCase]
	public void RandomizedAndRestoredAppearancesSelectTheMatchingMeshInsteadOfAFixedEffectRune()
	{
		var scroll = (Scroll)ScrollCategory().Types[0]; var first = new IdentificationService(); first.Initialize(42);
		var original = first.GetAppearance(scroll);
		bool changed = false;
		for (ulong seed = 43; seed < 50; seed++)
		{
			var next = new IdentificationService(); next.Initialize(seed);
			var appearance = next.GetAppearance(scroll);
			if (appearance != original)
			{
				changed = true; AssertBool(appearance.GlyphMesh != original.GlyphMesh).IsTrue();
				AssertBool(appearance.GetModel("") != original.GetModel("")).IsTrue();
			}
		}
		AssertBool(changed).IsTrue();
		var restored = new IdentificationService(); restored.Initialize(999, persistedAssignments: first.GetAssignmentDescriptors());
		AssertObject(restored.GetAppearance(scroll).GlyphMesh).IsSame(original.GlyphMesh);
		AssertObject(restored.GetAppearance(scroll).GetModel("")).IsSame(original.GetModel(""));
	}
	[TestCase]
	public async Task ReadingPreviewUsesTheProductionRendererAndMappedMesh()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/runes/scroll_rune_study.tscn");
		await runner.SimulateFrames(2); var study = (ScrollRuneStudy)runner.Scene();
		study.SetSeed(42); study.SetTime(.55f);
		var expected = new IdentificationService(); expected.Initialize(42);
		AssertObject(study.Appearance).IsSame(expected.GetAppearance(study.SelectedScroll));
		AssertBool(study.RuneRing.Glyphs.All(g => g.Mesh == study.Appearance.GlyphMesh)).IsTrue();
		AssertInt(study.Stage.GetChildCount()).IsEqual(1); AssertInt(study.RingGlyphCount).IsEqual(10);
		study.SelectScroll(2); study.SetSeed(43); await runner.SimulateFrames(2);
		AssertBool(study.RuneRing.Glyphs.All(g => g.Mesh == study.Appearance.GlyphMesh)).IsTrue();
	}
	[TestCase]
	public async Task SessionAssignmentCanBeCopiedWithoutIdentifyingOrReshufflingItems()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/runes/scroll_rune_study.tscn");
		await runner.SimulateFrames(2); var study = (ScrollRuneStudy)runner.Scene();
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
		await runner.SimulateFrames(2); var study = (ScrollRuneStudy)runner.Scene(); study.SetTime(.2f);
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