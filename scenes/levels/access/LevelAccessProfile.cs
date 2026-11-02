using Godot;
using System;
using System.Linq;

/// <summary>Explicit level access budget and item/scene dependencies; unset means ordinary generation.</summary>
[Tool]
[GlobalClass]
public partial class LevelAccessProfile : Resource
{
	[Export] public DoorLockDefinition[] Locks { get; set; } = [];
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedRooms { get; set; } = 1;
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedDoors { get; set; } = 1;
	[Export(PropertyHint.Range, "0,1")] public float LockedRoomChance { get; set; } = 0.2f;
	public int RollLockedRoomBudget(RandomNumberGenerator rng) => rng.Randf() < LockedRoomChance ? MaximumLockedRooms : 0;
	public void Validate()
	{
		if (!float.IsFinite(LockedRoomChance) || LockedRoomChance < 0 || LockedRoomChance > 1
			|| MaximumLockedDoors < 0 || MaximumLockedDoors > 10 || MaximumLockedRooms < 0 || MaximumLockedRooms > 10
			|| MaximumLockedRooms > 0 && Locks.Length == 0)
			throw new InvalidOperationException($"Invalid level access profile: {ResourcePath}");
		foreach (var policy in Locks)
		{
			if (policy?.RequiredKey == null || !policy.Methods.OfType<KeyUnlockMethod>().Any())
				throw new InvalidOperationException("Generated locks require a guaranteed key and a key unlock method.");
			policy.Validate();
		}
	}
}