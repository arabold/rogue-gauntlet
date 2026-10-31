using System;
using System.Linq;
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

	/// <summary>Weighted room types assembled with their content; shares procedural slots with the legacy generators.</summary>
	[Export] public RoomType[] RoomTypes { get; set; } = [];
	/// <summary>Weighted legacy coded recipes for procedural slots. With no room types either, the generic RoomBuilder is used.</summary>
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
		bool generated = RoomTypes.Length > 0 || RoomGenerators.Length > 0;
		if (ProceduralShare >= 1 && !generated && RoomBuilder == null)
			throw new InvalidOperationException("Generated room factory: assign a room type, generator or room builder.");
		if ((generated || RoomBuilder != null) && rng.Randf() < ProceduralShare)
		{
			// A selected generated recipe must succeed; substituting a frozen room hides broken authoring.
			if (!generated) return RoomBuilder.BuildRoom(rng) ?? throw new InvalidOperationException("The generic room builder could not build a room.");
			if (RoomTypes.Any(type => type == null) || RoomGenerators.Any(generator => generator == null))
				throw new InvalidOperationException("Room factory: assign every room type and generator entry.");
			var options = RoomTypes.Select(type => ((Resource)type, type.Weight)).Concat(RoomGenerators.Select(generator => ((Resource)generator, generator.Weight))).ToArray();
			double roll = rng.Randf() * options.Sum(option => option.Item2);
			var selected = options[^1].Item1;
			foreach (var (option, weight) in options)
			{
				roll -= weight;
				if (roll < 0) { selected = option; break; }
			}
			if (selected is RoomType roomType)
				return RoomAssembler.Assemble(roomType, ((ulong)rng.Randi() << 32) | rng.Randi());
			var generator = (RoomGenerator)selected;
			var errors = generator.Validate();
			if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
			return generator.BuildRoom(rng) ?? throw new InvalidOperationException("Selected generated room could not be built; authored substitution is disabled.");
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