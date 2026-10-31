using System;
using Godot;

/// <summary>
/// Blends authored room templates with procedurally built rooms. Standard rooms are
/// procedural with <see cref="ProceduralShare"/> probability. Entrances and exits
/// stay authored for required gameplay content; optional authored landmarks become
/// standard room slots when their non-repeating pool is exhausted.
/// </summary>
[Tool]
[GlobalClass]
public partial class MixedRoomFactory : RoomFactory
{
	[Export] public RoomFactory AuthoredFactory { get; set; }
	[Export] public ProceduralRoomBuilder RoomBuilder { get; set; }

	/// <summary>Weighted coded recipes for procedural slots. Empty uses the generic RoomBuilder.</summary>
	[Export] public RoomGenerator[] RoomGenerators { get; set; } = [];

	/// <summary>Fraction of standard rooms that are procedurally built.</summary>
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
		if (ProceduralShare >= 1 && RoomGenerators.Length == 0 && RoomBuilder == null)
			throw new InvalidOperationException("Generated room factory: assign a generator or room builder.");
		if ((RoomGenerators.Length > 0 || RoomBuilder != null) && rng.Randf() < ProceduralShare)
		{
			// A selected generated recipe must succeed; substituting a frozen room hides broken authoring.
			Room room;
			if (RoomGenerators.Length > 0)
			{
				foreach (var generator in RoomGenerators)
				{
					if (generator == null) throw new InvalidOperationException("Room factory: assign every generator entry.");
					var errors = generator.Validate();
					if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
				}
				double totalWeight = 0;
				foreach (var generator in RoomGenerators) totalWeight += generator.Weight;
				double roll = rng.Randf() * totalWeight;
				var selected = RoomGenerators[^1];
				foreach (var generator in RoomGenerators)
				{
					roll -= generator.Weight;
					if (roll < 0) { selected = generator; break; }
				}
				room = selected.BuildRoom(rng);
			}
			else room = RoomBuilder.BuildRoom(rng);
			return room ?? throw new InvalidOperationException("Selected generated room could not be built; authored substitution is disabled.");
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