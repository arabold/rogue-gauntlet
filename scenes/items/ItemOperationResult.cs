/// <summary>Expected item-operation failures, separate from unexpected programming errors.</summary>
public enum ItemFailure
{
	None, InvalidQuantity, InvalidDefinition, InvalidPolicy, IncompatibleStack,
	InsufficientSpace, NotOwned, InvalidTarget, ProtectedItem, WorldUnavailable, Busy, InvalidSave,
}

public readonly record struct ItemOperationResult(ItemFailure Failure)
{
	public bool Success => Failure == ItemFailure.None;
	public static ItemOperationResult Ok => new(ItemFailure.None);
}

public sealed record ItemQuantity(Item Item, int Quantity);