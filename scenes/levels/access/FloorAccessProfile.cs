using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Explicit floor access budget and lock definitions; unset means ordinary generation.</summary>
[Tool]
[GlobalClass]
public partial class FloorAccessProfile : Resource
{
	[Export] public DoorLockDefinition[] Locks { get; set; } = [];
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedRooms { get; set; } = 1;
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedDoors { get; set; } = 1;
	[Export(PropertyHint.Range, "0,1")] public float LockedRoomChance { get; set; } = 0.2f;
	/// <summary>When true, allows generated locks on non-reserved Exit rooms provided keys stay reachable outside them.</summary>
	[Export] public bool AllowLockingExit { get; set; } = false;
	/// <summary>When true, allows multi-tier chains where later keys may spawn inside earlier unlocked rooms.</summary>
	[Export] public bool AllowLockChains { get; set; } = false;

	public int RollLockedRoomBudget(RandomNumberGenerator rng) => rng.Randf() < LockedRoomChance ? MaximumLockedRooms : 0;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (!float.IsFinite(LockedRoomChance) || LockedRoomChance < 0 || LockedRoomChance > 1
			|| MaximumLockedDoors < 0 || MaximumLockedDoors > 10 || MaximumLockedRooms < 0 || MaximumLockedRooms > 10
			|| (MaximumLockedRooms > 0 && Locks.Length == 0))
		{
			errors.Add($"Invalid floor access profile: {ResourcePath}");
		}
		foreach (var policy in Locks)
		{
			if (policy?.RequiredKey == null || !policy.Methods.OfType<KeyUnlockMethod>().Any())
			{
				errors.Add($"Floor access profile {ResourcePath}: generated locks require a guaranteed key and a key unlock method.");
				continue;
			}
			try
			{
				policy.Validate();
			}
			catch (InvalidOperationException error)
			{
				errors.Add(error.Message);
			}
		}
		return errors.ToArray();
	}

	public void ValidateOrThrow()
	{
		var errors = Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}
}