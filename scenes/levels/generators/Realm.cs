using System.Collections.Generic;
using Godot;

/// <summary>
/// A band of depths built from one architecture: the room factory its floors assemble rooms from and the tiles of
/// the corridors between them. Realms share the dungeon mesh libraries; each uses its own subset of them. Encounters
/// stay with the generator's mob factory.
/// </summary>
[Tool, GlobalClass]
public partial class Realm : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;
	[Export] public RoomFactory Rooms { get; set; }
	[Export] public TileFactory Corridors { get; set; }

	public bool Covers(uint depth) => depth >= FirstDepth && depth <= LastDepth;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Rooms == null || Corridors == null)
			errors.Add($"Realm {Id}: assign an ID, its room factory and its corridor tiles.");
		if (FirstDepth > LastDepth) errors.Add($"Realm {Id}: the first depth must not follow the last.");
		return errors.ToArray();
	}
}
