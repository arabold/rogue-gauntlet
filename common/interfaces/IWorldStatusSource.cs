/// <summary>
/// A place in the world that holds a status on whoever stands in it, such as a slime pool. Its statuses end when the
/// hero walks out or leaves the floor, so saves leave them out instead of restoring them.
/// </summary>
public interface IWorldStatusSource
{
}
