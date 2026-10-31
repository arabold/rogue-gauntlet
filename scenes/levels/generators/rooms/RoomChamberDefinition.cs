using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Reusable row-chamber geometry, independent of prison furniture or room sizing.</summary>
[Tool, GlobalClass]
public partial class RoomChamberDefinition : Resource
{
	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public string FrontWallItem { get; set; } = "wall_gated";
	[Export] public string[] PartitionWallItems { get; set; } = ["wall_gated", "wall_archedwindow_gated"];
	[Export] public Vector2I Size { get; set; } = new(6, 7);
	[Export(PropertyHint.Range, "2.1,4,0.1")] public float OpeningWidth { get; set; } = 2.4f;
	[Export] public PackedScene DoorScene { get; set; }

	public string[] Validate()
	{
		var errors = new List<string>();
		if (WallLibrary == null || string.IsNullOrWhiteSpace(FrontWallItem) || PartitionWallItems == null || PartitionWallItems.Length == 0
			|| PartitionWallItems.Append(FrontWallItem).Any(item => string.IsNullOrWhiteSpace(item) || WallLibrary.FindItemByName(item) < 0))
			errors.Add("Chamber: assign an existing, compatible front/partition wall palette.");
		if (Size.X < 6 || Size.X > 12 || Size.Y < 6 || Size.Y > 12 || !float.IsFinite(OpeningWidth)
			|| OpeningWidth < 2.1f || OpeningWidth > 4 || Size.X - OpeningWidth < 2)
			errors.Add("Chamber: use dimensions from 6 to 12 units and a player-width entrance with room for its jambs.");
		if (DoorScene != null)
		{
			var instance = DoorScene.Instantiate();
			try
			{
				if (instance is not Door door || door.Interior == null || door.Interior.Door != door || door.Leaf == null || door.Collision == null
					|| door.Interactive == null || door.NavigationLink == null || door.Lock?.Definition != null || door.Secret?.IsSecret == true)
					errors.Add("Chamber: assign an unlocked, non-secret door scene with its interior capability and normal door parts wired.");
			}
			finally { instance.Free(); }
		}
		return errors.ToArray();
	}
}