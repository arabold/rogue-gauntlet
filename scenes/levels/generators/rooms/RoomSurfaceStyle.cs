using Godot;
using System;
using System.Linq;

/// <summary>A coherent room-wide floor and wall family, selected once rather than mixing unrelated wall meshes.</summary>
[Tool, GlobalClass]
public partial class RoomSurfaceStyle : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public RoomMaterial Floor { get; set; } = RoomMaterial.StoneSlab;
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	[Export] public string[] StraightWalls { get; set; } = ["wall"];
	[Export] public string HalfWall { get; set; } = "wall_half";
	[Export] public string CornerWall { get; set; } = "wall_corner";

	public string[] Validate(MeshLibrary library)
	{
		if (string.IsNullOrWhiteSpace(Id) || !Enum.IsDefined(Floor) || Floor == RoomMaterial.Random || !float.IsFinite(Weight) || Weight <= 0)
			return ["Surface style: assign a stable ID, explicit floor and positive weight."];
		if (library == null || StraightWalls == null || StraightWalls.Length == 0 || StraightWalls.Append(HalfWall).Append(CornerWall)
			.Any(name => string.IsNullOrWhiteSpace(name) || library.FindItemByName(name) < 0))
			return ["Surface style: assign a compatible wall family present in the wall library."];
		return [];
	}
}