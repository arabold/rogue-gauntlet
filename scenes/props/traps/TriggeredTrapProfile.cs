using Godot;

/// <summary>
/// One kind of plate trap a floor may place in its trap slots: a concealed plate and the wall launcher across from it
/// that the plate sets off. Generation hangs the launcher on a plain stretch of room wall and lays the plate on open
/// floor <see cref="MinLane"/> to <see cref="MaxLane"/> in front of it, where the hero can walk around it.
/// </summary>
[Tool, GlobalClass]
public partial class TriggeredTrapProfile : Resource
{
	/// <summary>A concealed <see cref="PressurePlate"/> without receivers of its own.</summary>
	[Export] public PackedScene PlateScene { get; set; }
	/// <summary>The mechanism: a wall prop whose scene is a <see cref="WallLauncher"/>; its geometry decides where it hangs.</summary>
	[Export] public Prop Launcher { get; set; }
	/// <summary>How the plate sets the launcher off: Pulse, Hold or Latch.</summary>
	[Export] public TriggerMode Mode { get; set; }
	/// <summary>Relative chance among the profiles a floor may place.</summary>
	[Export(PropertyHint.Range, "0,10,0.1")] public float Weight { get; set; } = 1;
	/// <summary>Shallowest depth that places it.</summary>
	[Export(PropertyHint.Range, "1,25")] public int FirstDepth { get; set; } = 1;
	/// <summary>Shortest distance from the launcher to the plate's middle.</summary>
	[Export(PropertyHint.Range, "1,20,0.5,suffix:m")] public float MinLane { get; set; } = 4;
	/// <summary>Longest distance from the launcher to the plate's middle: short enough that a shot reaches a hero still on it.</summary>
	[Export(PropertyHint.Range, "1,20,0.5,suffix:m")] public float MaxLane { get; set; } = 7;

	public bool IsValid => PlateScene != null && Launcher is { Mount: PropMount.Wall, Scenes.Length: > 0 } && Mode != null
		&& MinLane <= MaxLane;
}
