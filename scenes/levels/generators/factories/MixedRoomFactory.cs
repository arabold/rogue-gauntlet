using System;
using System.Linq;
using Godot;

/// <summary>
/// Blends authored room templates with assembled room types. Standard rooms are
/// assembled with <see cref="ProceduralShare"/> probability. Entrances and exits
/// stay authored for required gameplay content; optional authored landmarks become
/// standard room slots when their non-repeating pool is exhausted.
/// </summary>
[Tool]
[GlobalClass]
public partial class MixedRoomFactory : RoomFactory
{
	[Export] public RoomFactory AuthoredFactory { get; set; }
	/// <summary>Weighted room types, each assembled with its content.</summary>
	[Export] public RoomType[] RoomTypes { get; set; } = [];

	/// <summary>Fraction of standard rooms that are assembled rather than authored.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ProceduralShare { get; set; } = 0.4f;

	// Required entrance/exit scenes and authored fallback depend on this assignment.
	// Report once per generation rather than on every placement attempt.
	private bool _loggedMissingAuthoredFactory;

	public override Room CreateEntrance(RandomNumberGenerator rng)
	{
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateEntrance(rng);
	}

	public override Room CreateExit(RandomNumberGenerator rng)
	{
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateExit(rng);
	}

	public override Room CreateSpecialRoom(RandomNumberGenerator rng)
	{
		WarnIfAuthoredFactoryMissing();
		// An exhausted non-repeating landmark pool leaves an ordinary room slot.
		return AuthoredFactory?.CreateSpecialRoom(rng) ?? CreateStandardRoom(rng);
	}

	public override Room CreateStandardRoom(RandomNumberGenerator rng)
	{
		if (ProceduralShare >= 1 && RoomTypes.Length == 0)
			throw new InvalidOperationException("Generated room factory: assign room types.");
		if (RoomTypes.Length > 0 && rng.Randf() < ProceduralShare)
		{
			// A selected room type must succeed; substituting a frozen room hides broken authoring.
			if (RoomTypes.Any(type => type == null)) throw new InvalidOperationException("Room factory: assign every room type entry.");
			var type = Weighted.Pick(RoomTypes, entry => entry.Weight, rng);
			return RoomAssembler.Assemble(type, ((ulong)rng.Randi() << 32) | rng.Randi());
		}
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateStandardRoom(rng);
	}

	public override void Reset()
	{
		_loggedMissingAuthoredFactory = false;
		AuthoredFactory?.Reset();
	}

	private void WarnIfAuthoredFactoryMissing()
	{
		if (AuthoredFactory != null || _loggedMissingAuthoredFactory)
		{
			return;
		}

		_loggedMissingAuthoredFactory = true;
		GD.PrintErr($"MixedRoomFactory ({ResourcePath}): AuthoredFactory is not set. "
			+ "Entrance/exit rooms and the authored standard-room fallback will be unavailable.");
	}
}