using Godot;

/// <summary>The one gameplay reader of exploration, for world presentation. Supports generated content and level-runtime
/// spawns; an initializing level stays concealed, while standalone previews and a level without discovery show everything.</summary>
public sealed class ExplorationVisibility
{
	private readonly Level _level;
	public bool HasLevelContext => _level != null;
	public ExplorationVisibility(Node context) => _level = context.GetAncestorOrNull<Level>();
	public bool IsRevealed(Vector3 worldPosition) => _level?.Discovery?.IsRevealedAt(worldPosition) ?? true;
}