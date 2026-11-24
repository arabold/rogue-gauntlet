/// <summary>
/// An actor capability that may catch the blow that would kill it, such as a golem collapsing into rubble instead of
/// dying (<see cref="ReviveComponent"/>). <see cref="HealthComponent"/> asks it before health reaches zero.
/// </summary>
public interface IDeathGuard
{
	/// <summary>Catch a lethal blow: true leaves the actor alive with a sliver of health instead of dying; false lets it die.</summary>
	bool TryCatchLethalBlow();
}
