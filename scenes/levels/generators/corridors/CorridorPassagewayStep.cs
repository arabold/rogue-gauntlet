using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Places open archway/passageway frames across straight 1-tile-wide corridor spans to break up long tunnels.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorPassagewayStep : CorridorStep
{
	[Export(PropertyHint.Range, "2,12")] public int MinimumSpanTiles { get; set; } = 3;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PerSpanChance { get; set; } = .65f;

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		var spans = corridors.Spans.Where(span => span.Length >= MinimumSpanTiles).ToList();
		Shuffle(spans, rng);
		int count = 0;

		foreach (var span in spans)
		{
			if (count >= MaximumCount) break;
			if (rng.Randf() >= PerSpanChance) continue;

			// Prefer interior tiles of the span so the archway sits well inside the straight tunnel;
			// long spans may receive more than one archway when Spacing permits.
			int interiorCount = Mathf.Max(1, span.Length - 2);
			var indices = new List<int>(interiorCount);
			for (int i = 1; i <= interiorCount; i++) indices.Add(i);
			Shuffle(indices, rng);
			foreach (int index in indices)
			{
				if (count >= MaximumCount) break;
				var prop = NextProp(corridors, rng);
				if (corridors.TryPlacePassageway(prop, span.TileAt(index), span.Along, rng, Spacing))
				{
					count++;
				}
			}
		}
	}
}