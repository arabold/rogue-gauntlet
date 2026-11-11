/// <summary>
/// A world with its own lighting, such as a generated level or a debug arena. Lights inside it, like the hero's carried
/// light and torch fixtures, follow its darkness.
/// </summary>
public interface ILitWorld
{
	/// <summary>The world's illumination; null when it has none, which leaves its lights as authored.</summary>
	WorldLighting Lighting { get; }
}
