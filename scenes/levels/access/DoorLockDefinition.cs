using Godot;
using System;
using System.Linq;

/// <summary>Immutable authored lock policy: the key that guarantees a solution and the ways the lock may be opened.</summary>
[Tool]
[GlobalClass]
public partial class DoorLockDefinition : Resource
{
	/// <summary>Key guaranteed by generation and shown on the door marker; spent by <see cref="KeyUnlockMethod"/>.</summary>
	[Export] public KeyItem RequiredKey { get; set; }
	/// <summary>Tried in order; the first one the actor is permitted to use and can pay for is offered.</summary>
	[Export] public UnlockMethod[] Methods { get; set; } = [];
	public void Validate()
	{
		if (Methods.Length == 0 || Methods.Any(m => m == null) || Methods.Select(m => m.OperationId).Distinct().Count() != Methods.Length)
			throw new InvalidOperationException($"Invalid lock definition: {ResourcePath}");
		foreach (var method in Methods) method.Validate(this);
	}
}