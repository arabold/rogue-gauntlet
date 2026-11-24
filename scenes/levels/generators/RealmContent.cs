using Godot;

/// <summary>A realm schedule entry's window: the floors of the realm it may appear on, 1 being the realm's first.</summary>
[Tool, GlobalClass]
public abstract partial class RealmContent : Resource
{
	[Export(PropertyHint.Range, "1,10")] public int FirstFloor { get; set; } = 1;
	[Export(PropertyHint.Range, "1,10")] public int LastFloor { get; set; } = 5;

	public bool Spans(int floor) => floor >= FirstFloor && floor <= LastFloor;

	/// <summary>Whether the window lies inside floors 1 to <paramref name="floorCount"/>.</summary>
	public bool FitsIn(int floorCount) => FirstFloor >= 1 && FirstFloor <= LastFloor && LastFloor <= floorCount;
}