#if TOOLS
using Godot;
using System.Linq;

/// <summary>Validate authored monster scenes without changing the selected scene or its Resources.</summary>
[Tool]
public partial class MonsterAuthoringInspector : EditorPlugin
{
	public override void _EnterTree() => AddToolMenuItem("Validate monster scene", Callable.From(ValidateScene));
	public override void _ExitTree() => RemoveToolMenuItem("Validate monster scene");

	/// <summary>Editor-safe entry point for validation probes and the menu command.</summary>
	public string[] ValidateActor(Node actor) => MonsterSceneValidator.Validate(actor);

	private void ValidateScene()
	{
		var actor = EditorInterface.Singleton.GetEditedSceneRoot();
		if (actor == null || !actor.GetPropertyList().Any(property => property["name"].AsString() == "Definition"))
		{
			GD.PushWarning("Open a monster actor scene, then choose Project > Tools > Validate monster scene.");
			return;
		}
		var errors = ValidateActor(actor);
		if (errors.Length == 0) GD.Print($"Monster validation passed: {actor.SceneFilePath}");
		foreach (var error in errors) GD.PushError($"{actor.SceneFilePath}: {error}");
	}
}
#endif