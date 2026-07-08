using System;
using Godot;

/// <summary>
/// Blends authored room templates with procedurally built rooms. Standard rooms are
/// procedural with <see cref="ProceduralShare"/> probability; entrances, exits, and
/// special rooms always come from the authored factory — they carry hand-placed
/// gameplay content (spawn points, stairs, set-piece props) a builder cannot provide.
/// </summary>
[Tool]
[GlobalClass]
public partial class MixedRoomFactory : RoomFactory
{
	[Export] public RoomFactory AuthoredFactory { get; set; }
	[Export] public ProceduralRoomBuilder RoomBuilder { get; set; }

	/// <summary>Fraction of standard rooms that are procedurally built.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ProceduralShare { get; set; } = 0.4f;

	// Entrances/exits/special rooms always come from AuthoredFactory, and it's the
	// fallback for standard rooms too -- an unset reference silently starves the whole
	// layout of rooms with no other symptom. Logged once per generation (not per call,
	// since CreateStandardRoom runs once per room) so misconfiguration is diagnosable
	// without flooding the output on every room placement attempt.
	private bool _loggedMissingAuthoredFactory;

	public override Room CreateEntrance()
	{
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateEntrance();
	}

	public override Room CreateExit()
	{
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateExit();
	}

	public override Room CreateSpecialRoom()
	{
		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateSpecialRoom();
	}

	public override Room CreateStandardRoom()
	{
		if (RoomBuilder != null && GD.Randf() < ProceduralShare)
		{
			// Fall back to an authored room on misconfiguration (e.g. a renamed mesh
			// item) instead of returning null and silently starving the layout of rooms.
			Room room = RoomBuilder.BuildRoom();
			if (room != null)
			{
				return room;
			}
		}

		WarnIfAuthoredFactoryMissing();
		return AuthoredFactory?.CreateStandardRoom();
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
			+ "Entrance/exit/special rooms (and standard rooms when procedural generation "
			+ "is skipped or fails) will be null.");
	}
}
