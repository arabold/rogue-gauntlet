/// <summary>
/// A world monsters navigate: a generated level or a debug arena. Monsters inside it find its grids through their
/// ancestors and follow them when the world rebuilds them.
/// </summary>
public interface INavigableWorld
{
	/// <summary>The world's ground navigation; null while it has none, which holds walking monsters still.</summary>
	FloorNavigation Navigation { get; }

	/// <summary>The grid flyers navigate (<see cref="NavigationLayer.Air"/>); null while it has none, which holds them still.</summary>
	FloorNavigation AirNavigation { get; }

	FloorNavigation NavigationFor(NavigationLayer layer) => layer == NavigationLayer.Air ? AirNavigation : Navigation;
}
