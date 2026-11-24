/// <summary>
/// A world monsters navigate: a generated level or a debug arena. Monsters inside it find its grids through their
/// ancestors and follow them when the world rebuilds them.
/// </summary>
public interface INavigableWorld
{
	/// <summary>
	/// The world's grid for <paramref name="layer"/>: the ground walkers keep to, or the air flyers cross. Null while it
	/// has none, which holds that layer's monsters still.
	/// </summary>
	FloorNavigation NavigationFor(NavigationLayer layer);
}
