/// <summary>
/// Defines a contract for entities that can be staggered (put into a forced reaction state)
/// by combat events such as parries or heavy impacts.
/// </summary>
public interface IStaggerable
{
	bool TryStagger(float duration);
}