using System;
using System.Linq;
using Godot;

/// <summary>
/// Assembles every room from weighted <see cref="RoomType"/>s: entrances and exits from their own types (they carry
/// the stairs), every other slot from <see cref="RoomTypes"/>. Each room gets its own seed from the layout stream.
/// </summary>
[Tool]
[GlobalClass]
public partial class AssembledRoomFactory : RoomFactory
{
	/// <summary>Weighted room types for the level entrance, each with up-stairs.</summary>
	[Export] public RoomType[] EntranceTypes { get; set; } = [];
	/// <summary>Weighted room types for the level exit, each with down-stairs.</summary>
	[Export] public RoomType[] ExitTypes { get; set; } = [];
	/// <summary>Weighted room types for every other slot.</summary>
	[Export] public RoomType[] RoomTypes { get; set; } = [];

	public override Room CreateEntrance(RandomNumberGenerator rng) => Assemble(EntranceTypes, "entrance", rng);
	public override Room CreateExit(RandomNumberGenerator rng) => Assemble(ExitTypes, "exit", rng);
	public override Room CreateStandardRoom(RandomNumberGenerator rng) => Assemble(RoomTypes, "room", rng);
	// No landmark types yet: special slots take ordinary rooms.
	public override Room CreateSpecialRoom(RandomNumberGenerator rng) => CreateStandardRoom(rng);

	/// <summary>A selected type must succeed: substituting another room would hide broken authoring.</summary>
	private static Room Assemble(RoomType[] types, string slot, RandomNumberGenerator rng)
	{
		if (types.Length == 0 || types.Any(type => type == null))
			throw new InvalidOperationException($"Room factory: assign every {slot} type entry.");
		var type = Weighted.Pick(types, entry => entry.Weight, rng);
		return RoomAssembler.Assemble(type, ((ulong)rng.Randi() << 32) | rng.Randi());
	}
}
