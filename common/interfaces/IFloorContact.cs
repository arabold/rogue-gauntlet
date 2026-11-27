/// <summary>
/// A body that can pass over the floor without touching it, as the hero does mid-dodge. Pressure plates count such a
/// body only once it touches down on them.
/// </summary>
public interface IFloorContact
{
	bool TouchesFloor { get; }
}