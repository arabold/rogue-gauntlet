namespace RogueGauntlet.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class CharacterPreviewTest
{
	private const string ScenePath = "res://scenes/menu/character_preview.tscn";

	[TestCase]
	public void TurntableStartsFacingForwardWithIdentityRotation()
	{
		using var runner = TestFactories.LoadScene(ScenePath);
		var preview = (CharacterPreview)runner.Scene();
		var turntable = preview.GetNode<Node3D>("Turntable");

		AssertObject(turntable).IsNotNull();
		AssertBool(turntable.Rotation.IsEqualApprox(Vector3.Zero)).IsTrue();
	}

	[TestCase]
	public void ShowClassPausesRotationForHalfSecondBeforeRotating()
	{
		using var runner = TestFactories.LoadScene(ScenePath);
		var preview = (CharacterPreview)runner.Scene();
		var turntable = preview.GetNode<Node3D>("Turntable");
		var catalog = TestFactories.Load<CharacterCatalog>(CharacterCatalog.DefaultCatalogPath);
		CharacterClass hero = catalog.Classes[0];

		preview.ShowClass(hero);

		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// Process 0.2s: should still be paused and not rotated
		preview._Process(0.2);
		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// Process another 0.2s (total 0.4s < 0.5s): still paused
		preview._Process(0.2);
		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// Process another 0.2s (total 0.6s > 0.5s): pause elapsed, starts rotating
		preview._Process(0.2);
		AssertBool(preview.IsPaused).IsFalse();
		AssertFloat(turntable.Rotation.Y).IsGreater(0f);
	}

	[TestCase]
	public void SwitchingClassResetsTurntableRotationAndPausesAgain()
	{
		using var runner = TestFactories.LoadScene(ScenePath);
		var preview = (CharacterPreview)runner.Scene();
		var turntable = preview.GetNode<Node3D>("Turntable");
		var catalog = TestFactories.Load<CharacterCatalog>(CharacterCatalog.DefaultCatalogPath);

		preview.ShowClass(catalog.Classes[0]);

		// Advance past pause duration so turntable has rotated
		preview._Process(1.0);
		AssertFloat(turntable.Rotation.Y).IsGreater(0f);

		// Switch to next class
		preview.ShowClass(catalog.Classes[1]);

		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// While paused for 0.3s, rotation stays at 0
		preview._Process(0.3);
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);
	}
}