/// <summary>How a repeated application with the same canonical key is handled.</summary>
public enum BuffStacking
{
	Stack,
	Refresh
}

/// <summary>Outcome of an actor-neutral buff application.</summary>
public enum BuffApplication
{
	Rejected,
	Applied,
	Refreshed
}
