namespace RogueGauntlet.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>The character-select turntable faces forward for half a second after each class is shown, then turns.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CharacterPreviewTest
{
	private const string ScenePath = "res://scenes/menu/character_preview.tscn";

	[TestCase]
	public void ShowingAClassFacesForwardForHalfASecondBeforeTurning()
	{
		using var runner = TestFactories.LoadScene(ScenePath);
		var preview = (CharacterPreview)runner.Scene();
		var turntable = preview.GetNode<Node3D>("Turntable");
		var catalog = TestFactories.Load<CharacterCatalog>(CharacterCatalog.DefaultCatalogPath);

		preview.ShowClass(catalog.Classes[0]);
		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// 0.4 s in: still paused, still facing forward.
		preview._Process(0.2);
		preview._Process(0.2);
		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);

		// 0.6 s in: the pause has elapsed and it turns.
		preview._Process(0.2);
		AssertBool(preview.IsPaused).IsFalse();
		AssertFloat(turntable.Rotation.Y).IsGreater(0f);

		// Switching class faces forward again and pauses anew.
		preview.ShowClass(catalog.Classes[1]);
		AssertBool(preview.IsPaused).IsTrue();
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);
		preview._Process(0.3);
		AssertFloat(turntable.Rotation.Y).IsEqual(0f);
	}
}