using Godot;

/// <summary>Shared exploration gate for world presentation. Supports generated content and level-runtime spawns;
/// an initializing level stays concealed, while standalone previews have no fog restriction.</summary>
public sealed class ExplorationVisibility
{
	private readonly MapGenerator _generator;
	private readonly Level _level;
	public bool HasLevelContext => _generator != null || _level != null;
	public ExplorationVisibility(Node context)
	{
		_generator = context.GetAncestorOrNull<MapGenerator>();
		_level = context.GetAncestorOrNull<Level>();
	}
	public bool IsRevealed(Vector3 worldPosition)
	{
		if (!HasLevelContext) return true;
		// A code-built level may assign its generator after this helper was created.
		var generator = _generator ?? _level?.MapGenerator;
		return generator?.Map != null && generator.IsRevealedAt(worldPosition);
	}
}