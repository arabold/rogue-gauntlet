using Godot;
using System;
using System.Linq;

/// <summary>Explicit level access budget and item/scene dependencies; unset means ordinary generation.</summary>
[Tool]
[GlobalClass]
public partial class LevelAccessProfile : Resource
{
	/// <summary>The concealed door of secret pockets; room doorways bring their own doors (see <see cref="WallStyle.DoorScene"/>).</summary>
	[Export] public PackedScene DoorScene { get; set; }
	[Export] public DoorLockDefinition[] Locks { get; set; } = [];
	[Export] public SecretDetectionProfile Detection { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedRooms { get; set; } = 1;
	[Export(PropertyHint.Range, "0,10")] public int MaximumLockedDoors { get; set; } = 1;
	[Export(PropertyHint.Range, "0,1")] public float LockedRoomChance { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0,1")] public float SecretRoomChance { get; set; } = 0.45f;
	[Export(PropertyHint.Range, "0,5")] public int MaximumSecretRooms { get; set; } = 1;
	public int RollLockedRoomBudget(RandomNumberGenerator rng) => rng.Randf() < LockedRoomChance ? MaximumLockedRooms : 0;
	public int RollSecretRoomBudget(RandomNumberGenerator rng) => rng.Randf() < SecretRoomChance ? MaximumSecretRooms : 0;
	public void Validate()
	{
		if (!float.IsFinite(LockedRoomChance) || LockedRoomChance < 0 || LockedRoomChance > 1
			|| !float.IsFinite(SecretRoomChance) || SecretRoomChance < 0 || SecretRoomChance > 1
			|| MaximumLockedDoors < 0 || MaximumLockedDoors > 10 || DoorScene == null || MaximumLockedRooms < 0 || MaximumLockedRooms > 10 || MaximumSecretRooms < 0 || MaximumSecretRooms > 5
			|| MaximumLockedRooms > 0 && Locks.Length == 0
			|| MaximumSecretRooms > 0 && Detection == null)
			throw new InvalidOperationException($"Invalid level access profile: {ResourcePath}");
		Detection?.Validate();
		foreach (var policy in Locks)
		{
			if (policy?.RequiredKey == null || !policy.Methods.OfType<KeyUnlockMethod>().Any())
				throw new InvalidOperationException("Generated locks require a guaranteed key and a key unlock method.");
			policy.Validate();
		}
	}
}