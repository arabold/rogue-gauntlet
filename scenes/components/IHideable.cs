/// <summary>
/// An actor the hero can lose sight of: invisible, or lying in wait. Its nameplate and x-ray read
/// <see cref="IsHiddenFromHero"/> instead of each way of hiding, so nothing that shows them gives the actor away.
/// Targeting is a separate rule (<see cref="AttackController.IsTargetable"/>).
/// </summary>
public interface IHideable
{
	bool IsHiddenFromHero { get; }

	/// <summary>Raised when <see cref="IsHiddenFromHero"/> may have changed.</summary>
	event System.Action HiddenChanged;
}
