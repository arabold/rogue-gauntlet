using Godot;

public enum RoomPoolSlot
{
	Standard,
	Entrance,
	Exit,
	/// <summary>The trap gauntlet the run schedules on the floor (<see cref="RoomPool.Gauntlets"/>).</summary>
	Gauntlet,
}

/// <summary>
/// One node in a <see cref="RoomGroup"/> tree. Member 0 is the root (<see cref="AttachToIndex"/> == -1); every later
/// member attaches across a 1-tile bridge to an earlier index (<c>0 &lt;= AttachToIndex &lt; index</c>).
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomGroupMember : Resource
{
	/// <summary>Candidate definitions for this slot; when empty, draws from <see cref="PoolSlot"/> in the active realm's <see cref="RoomPool"/>.</summary>
	[Export] public RoomDefinition[] Options { get; set; } = [];
	[Export] public RoomPoolSlot PoolSlot { get; set; } = RoomPoolSlot.Standard;
	/// <summary>-1 for the root member (index 0); 0 &lt;= AttachToIndex &lt; index for child members.</summary>
	[Export] public int AttachToIndex { get; set; } = -1;
	/// <summary>Logical role identifier copied onto <see cref="RoomRegion.SlotId"/> (e.g., "boss", "exit").</summary>
	[Export] public string SlotId { get; set; } = "";
	[Export] public bool MustTraverse { get; set; } = false;
	[Export] public bool AllowExternalDoorways { get; set; } = true;
	[Export] public bool AllowSecretDoors { get; set; } = true;
	[Export(PropertyHint.Range, "-1,1,0.05")] public float DoorChanceOverride { get; set; } = -1f;
	/// <summary>When true, excludes this member's doorways from random floor lock/secret passes.</summary>
	[Export] public bool ReserveAccess { get; set; } = false;
}
