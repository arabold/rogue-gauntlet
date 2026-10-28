using Godot;
using System.Linq;

/// <summary>Reusable tiled test stage with selectable, authored wall arrangements.</summary>
public partial class DebugStage : Node3D
{
	[Signal] public delegate void ArrangementChangedEventHandler();
	[Export] public GridMap FloorTiles { get; set; }
	[Export] public StaticBody3D WestWall { get; set; }
	[Export] public StaticBody3D[] DefaultWalls { get; set; } = [];
	[Export] public StaticBody3D[] RoomWalls { get; set; } = [];
	[Export] public DebugArrangement[] Arrangements { get; set; } = [];
	public string[] ArrangementNames => Arrangements.Select(a => a.DisplayName).ToArray();
	private StaticBody3D[] _walls = [];
	public int Arrangement { get; private set; }
	public bool HasWestWall => Arrangements[Arrangement].HasWestWall;
	/// <summary>Walls suitable for effects emanating from the current room boundary.</summary>
	public System.Collections.Generic.IEnumerable<StaticBody3D> BoundaryWalls()
	{
		foreach (int index in Arrangements[Arrangement].BoundaryWalls) yield return _walls[index];
	}

	public override void _Ready()
	{
		_walls = new[] { WestWall }.Concat(DefaultWalls).Concat(RoomWalls).ToArray();
		PopulateFloor(FloorTiles);
		SetArrangement(0);
	}

	/// <summary>Populates the common floor grid for test scenes.</summary>
	public static void PopulateFloor(GridMap floor)
	{
		for (int x = -8; x <= 8; x += 4)
			for (int z = -8; z <= 8; z += 4) floor.SetCellItem(new Vector3I(x, 0, z), 0);
	}

	/// <summary>Changes visible geometry and physics together; unknown indices are ignored.</summary>
	public void SetArrangement(int index)
	{
		if (index < 0 || index >= Arrangements.Length) return;
		var preset = Arrangements[index];
		if (preset == null || preset.Walls.Any(i => i < 0 || i >= _walls.Length)
			|| preset.BoundaryWalls.Any(i => !preset.Walls.Contains(i)))
		{ GD.PushError("Invalid debug stage arrangement."); return; }
		Arrangement = index;
		for (int i = 0; i < _walls.Length; i++) SetWall(_walls[i], preset.Walls.Contains(i));
		EmitSignalArrangementChanged();
	}
	private static void SetWall(StaticBody3D wall, bool active)
	{
		wall.Visible = active;
		wall.CollisionLayer = active ? 1u : 0u;
	}
}