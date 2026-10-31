/// <param name="AnimationId">Optional action id the actor's animation tree plays while channeling, such as "drink_potion".</param>
public readonly record struct InteractionOffer(
	IInteractionTarget Target,
	string OperationId,
	string VerbLabel,
	float Duration,
	float Range,
	InteractionPermission RequiredPermission,
	ulong TargetRevision,
	bool IsBlocked = false,
	string AnimationId = null
)
{
	public static readonly InteractionOffer None = default;
	public bool IsValid => Target != null && !string.IsNullOrEmpty(OperationId);
	/// <summary>Valid and not merely informational: blocked offers keep their prompt visible but cannot begin.</summary>
	public bool IsActionable => IsValid && !IsBlocked;
}