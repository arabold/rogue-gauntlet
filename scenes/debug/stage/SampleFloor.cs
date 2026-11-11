using Godot;

/// <summary>
/// A floor paved with one real dungeon tile from its mesh library, so harnesses and workbenches stand their subjects on
/// the game's own ground rather than a flat colour. A tile's walking surface lies above its cell, where actors in the
/// game stand on its collision, so the grid drops by that height: the surface sits at the parent's origin, level with the
/// flat colliders and staged subjects there. Paved when it enters a running game; the editor shows it empty, so saving a
/// scene never writes the tiles into it.
/// </summary>
[GlobalClass]
public partial class SampleFloor : GridMap
{
	/// <summary>The tile to pave with, by its name in <see cref="GridMap.MeshLibrary"/>.</summary>
	[Export]
	public string Tile
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Pave(); }
		}
	} = "floor_tile_large";

	/// <summary>How far the paving reaches from the floor's centre, in metres, rounded down to whole tiles.</summary>
	[Export]
	public int HalfExtent
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Pave(); }
		}
	} = 8;

	public override void _Ready() => Pave();

	private void Pave()
	{
		Clear();
		int tile = MeshLibrary?.FindItemByName(Tile) ?? -1;
		if (tile < 0)
		{
			GD.PushError($"{Name}: the floor library has no tile named {Tile}.");
			return;
		}

		Position = Position with { Y = -WalkingSurface(tile) };
		int reach = HalfExtent / Room.TileSize * Room.TileSize;
		for (int x = -reach; x <= reach; x += Room.TileSize)
		{
			for (int z = -reach; z <= reach; z += Room.TileSize)
			{
				SetCellItem(new Vector3I(x, 0, z), tile);
			}
		}
	}

	/// <summary>Height of the tile's walking surface above its cell: the top of its collision, or 0 without any.</summary>
	private float WalkingSurface(int tile)
	{
		var shapes = MeshLibrary.GetItemShapes(tile);
		float top = 0;
		for (int index = 0; index + 1 < shapes.Count; index += 2)
		{
			if (shapes[index].AsGodotObject() is Shape3D shape)
			{
				top = Mathf.Max(top, CollisionOutlines.FromShape(shape, shapes[index + 1].AsTransform3D()).Top);
			}
		}
		return top;
	}
}