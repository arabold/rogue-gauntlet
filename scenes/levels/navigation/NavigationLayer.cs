/// <summary>
/// Which of a world's grids a monster navigates. Walkers keep to the ground; flyers cross pits, water and solids lower
/// than <see cref="ClearanceGrid.AirClearanceHeight"/>, but never walls, shut doors or tall props.
/// </summary>
public enum NavigationLayer
{
	Ground,
	Air,
}
