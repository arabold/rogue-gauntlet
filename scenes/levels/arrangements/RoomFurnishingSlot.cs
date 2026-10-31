using System.Linq;
using Godot;

/// <summary>A generator's required furniture anchor. Room rotation carries its position and facing with the structure.</summary>
[Tool, GlobalClass]
public partial class RoomFurnishingSlot : Node3D
{
	[Export] public string Id { get; set; } = "";
	[Export] public RoomArrangementDefinition Definition { get; set; }

	/// <summary>Resolve the rotated slot in the same coordinate system as the placement map.</summary>
	public RoomArrangementPlacement Resolve(int roomId, Vector2I anchor, Transform3D transform)
	{
		int turn = ((Mathf.RoundToInt(Mathf.Atan2(transform.Basis.Z.X, transform.Basis.Z.Z) / (Mathf.Pi / 2)) % 4) + 4) % 4;
		return new(Definition, roomId, anchor, turn,
			Definition.OccupiedTiles.Select(tile => anchor + ConnectedArrangementStrategy.Rotate(tile, turn)).ToArray(), transform, SlotId: Id);
	}
}