/// <summary>Expected item-operation failures, separate from unexpected programming errors.</summary>
public enum ItemFailure
{
	None, InvalidQuantity, InvalidDefinition, InvalidPolicy, IncompatibleStack,
	InsufficientSpace, NotOwned, InvalidTarget, ProtectedItem, WorldUnavailable, Busy, InvalidSave, Cursed,
}

/// <summary>An operation's outcome. <see cref="Subject"/> names the item that blocked it, e.g. the worn cursed item.</summary>
public readonly record struct ItemOperationResult(ItemFailure Failure, Item Subject = null)
{
	public bool Success => Failure == ItemFailure.None;
	public static ItemOperationResult Ok => new(ItemFailure.None);
}

public sealed record ItemQuantity(Item Item, int Quantity);