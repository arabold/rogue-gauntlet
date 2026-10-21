public readonly record struct InteractionOffer(
	IInteractionTarget Target,
	string OperationId,
	string VerbLabel,
	float Duration,
	float Range,
	InteractionPermission RequiredPermission,
	ulong TargetRevision
)
{
	public static readonly InteractionOffer None = default;
	public bool IsValid => Target != null && !string.IsNullOrEmpty(OperationId);
}