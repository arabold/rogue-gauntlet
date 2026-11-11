/// <summary>
/// A world monsters navigate: a generated level or a debug arena. Monsters inside it find its grid through their
/// ancestors and follow it when the world rebuilds it.
/// </summary>
public interface INavigableWorld
{
	/// <summary>The world's monster navigation; null while it has none, which holds monsters still.</summary>
	FloorNavigation Navigation { get; }
}
