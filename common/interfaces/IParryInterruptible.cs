/// <summary>
/// Contract for attack actions or controllers that can be interrupted when an opponent parries them.
/// </summary>
public interface IParryInterruptible
{
	bool TryInterruptForParry(AttackIdentity execution, float staggerDuration);
}