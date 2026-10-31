using Godot;
using Godot.Collections;

/// <summary>One kind of fountain a run can hold, such as health or mana: its designs and how many a run has.</summary>
[GlobalClass]
public partial class FountainKind : Resource
{
	/// <summary>Stable id: it keys the run's schedule stream and each placed fountain's persistent id.</summary>
	[Export] public string Id { get; set; } = "";
	/// <summary>Interchangeable fountain scenes; each placement picks one.</summary>
	[Export] public Array<PackedScene> Variants { get; set; } = [];
	[Export(PropertyHint.Range, "0,10")] public int MinPerRun { get; set; } = 2;
	[Export(PropertyHint.Range, "0,10")] public int MaxPerRun { get; set; } = 3;
}