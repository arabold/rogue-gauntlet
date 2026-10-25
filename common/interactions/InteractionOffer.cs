public readonly record struct InteractionOffer(
	IInteractionTarget Target,
	string OperationId,
	string VerbLabel,
	float Duration,
	float Range,
	InteractionPermission RequiredPermission,
	ulong TargetRevision,
	bool IsBlocked = false
)
{
	public static readonly InteractionOffer None = default;
	public bool IsValid => Target != null && !string.IsNullOrEmpty(OperationId);
	/// <summary>Valid and not merely informational: blocked offers keep their prompt visible but cannot begin.</summary>
	public bool IsActionable => IsValid && !IsBlocked;
}