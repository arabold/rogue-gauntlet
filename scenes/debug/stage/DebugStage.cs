using Godot;
using System.Linq;

/// <summary>Reusable test stage on a sample dungeon floor, with selectable, authored wall arrangements.</summary>
public partial class DebugStage : Node3D
{
	[Signal] public delegate void ArrangementChangedEventHandler();
	[Export] public StaticBody3D WestWall { get; set; }
	[Export] public StaticBody3D[] DefaultWalls { get; set; } = [];
	[Export] public StaticBody3D[] RoomWalls { get; set; } = [];
	[Export] public DebugArrangement[] Arrangements { get; set; } = [];
	/// <summary>Arrangement the stage starts with; an arena that must stay open picks one without walls.</summary>
	[Export] public int InitialArrangement { get; set; }
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
		SetArrangement(InitialArrangement);
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