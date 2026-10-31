using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Quarter-tile pieces split diagonally between two materials, such as the kit's stone-to-dirt corner: a row of them
/// lays a smooth diagonal boundary. At no turn, <see cref="To"/> fills the triangle in the piece's -X/+Z corner and
/// <see cref="From"/> the rest.
/// </summary>
[Tool, GlobalClass]
public partial class FloorTransition : Resource
{
	private static readonly Dictionary<Vector2I, int> CornerTurns = new()
		{ [new(-1, 1)] = GridTurns.Turn0, [new(1, 1)] = GridTurns.Turn90, [new(1, -1)] = GridTurns.Turn180, [new(-1, -1)] = GridTurns.Turn270 };

	[Export] public FloorMaterial From { get; set; }
	[Export] public FloorMaterial To { get; set; }
	/// <summary>2×2 transition items, by weight.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Items { get; set; } = [];

	public bool Joins(FloorMaterial a, FloorMaterial b) =>
		(FloorMaterial.Same(From, a) && FloorMaterial.Same(To, b)) || (FloorMaterial.Same(From, b) && FloorMaterial.Same(To, a));

	/// <summary>The turn that puts <paramref name="material"/>'s triangle in quarter-tile corner <paramref name="corner"/> (±1, ±1).</summary>
	public int Turn(FloorMaterial material, Vector2I corner) => CornerTurns[FloorMaterial.Same(material, To) ? corner : -corner];

	public string[] Validate()
	{
		if (From?.Library == null || To == null || Items.Count == 0) return ["Floor transition: assign both materials and its items."];
		return FloorMaterial.CheckItems(From.Library, $"Floor transition {From.Id}/{To.Id}", Items.Keys, Vector2.One * 2).ToArray();
	}
}

/// <summary>A quarter tile laid as a transition piece instead of its material.</summary>
public readonly record struct FloorSplit(FloorTransition Transition, int Orientation);
