using Godot;

/// <summary>
/// A floor paved with one real dungeon tile from its mesh library, so harnesses and workbenches stand their subjects on
/// the game's own ground rather than a flat colour. Paved when it enters a running game; the editor shows it empty, so
/// saving a scene never writes the tiles into it.
/// </summary>
[GlobalClass]
public partial class SampleFloor : GridMap
{
	/// <summary>Spacing of the library's floor tiles, in metres.</summary>
	private const int TileSize = 4;

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

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
		{
			Pave();
		}
	}

	private void Pave()
	{
		Clear();
		int tile = MeshLibrary?.FindItemByName(Tile) ?? -1;
		if (tile < 0)
		{
			GD.PushError($"{Name}: the floor library has no tile named {Tile}.");
			return;
		}

		int reach = HalfExtent / TileSize * TileSize;
		for (int x = -reach; x <= reach; x += TileSize)
		{
			for (int z = -reach; z <= reach; z += TileSize)
			{
				SetCellItem(new Vector3I(x, 0, z), tile);
			}
		}
	}
}
