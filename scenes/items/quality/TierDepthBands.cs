using Godot;
using Godot.Collections;
using System.Collections.Generic;

/// <summary>
/// The dungeon depths where each equipment tier drops: the one place the tier ladder meets depth. Loot pools that
/// reference it validate their entries against it, so retuning a band shows every entry that still needs moving.
/// </summary>
[Tool]
[GlobalClass]
public partial class TierDepthBands : Resource
{
	/// <summary>Entry <c>i</c> is tier <c>i + 1</c>: X is the first depth, Y the last (0 means no upper bound).</summary>
	[Export] public Array<Vector2I> Bands { get; set; } = [];

	/// <summary>The band for <paramref name="tier"/>, or (0, 0) for a tier with no band.</summary>
	public Vector2I For(int tier) => tier >= 1 && tier <= (Bands?.Count ?? 0) ? Bands[tier - 1] : Vector2I.Zero;

	/// <summary>Authoring problems: every band needs a first depth and must not end before it starts.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Bands == null || Bands.Count == 0) { problems.Add($"{ResourcePath}: no tier bands."); }
		for (int i = 0; i < (Bands?.Count ?? 0); i++)
		{
			Vector2I band = Bands[i];
			if (band.X < 1 || (band.Y != 0 && band.Y < band.X)) { problems.Add($"{ResourcePath}: tier {i + 1} band {band.X}–{band.Y} is invalid."); }
		}
		return problems.ToArray();
	}
}