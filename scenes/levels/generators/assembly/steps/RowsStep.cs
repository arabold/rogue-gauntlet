using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A grid of content in the open floor: library stacks, dining tables, pews, crate rows. Rows repeat along the room's
/// length; each row holds columns across it, optionally split into two banks around a central aisle. Counts follow
/// the measured room, so rooms of any size get as many rows as fit.
/// </summary>
[Tool, GlobalClass]
public partial class RowsStep : FurnishingStep
{
	/// <summary>Turns content relative to the room axis; 0 keeps its +Z along the room's length.</summary>
	[Export(PropertyHint.Range, "0,3")] public int QuarterTurns { get; set; }
	/// <summary>Clear aisle between rows, along the room's length.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float RowGap { get; set; } = 2.6f;
	/// <summary>Clear gap between neighbours in a row.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float ColumnGap { get; set; } = 2.6f;
	/// <summary>Clear space between the grid and the walls.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float Margin { get; set; } = 2.4f;
	/// <summary>A positive width splits every row into two banks around a central aisle.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float CenterAisle { get; set; }
	/// <summary>Columns per row (per bank when split); zero fits as many as possible.</summary>
	[Export(PropertyHint.Range, "0,8")] public int Columns { get; set; }

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var turn = new Transform3D(new Basis(Vector3.Up, QuarterTurns * Mathf.Pi / 2), Vector3.Zero);
		var plan = Next(room, rng, out var choice);
		if (plan.IsEmpty) return 0;
		var slot = turn * plan.Bounds();
		var area = room.Interior.Grow(-(.5f + Margin));
		int rows = Fit(area.Size.Y, slot.Size.Z, RowGap);
		var columnCenters = new List<float>();
		if (CenterAisle > 0)
		{
			float bank = (area.Size.X - CenterAisle) / 2;
			int perBank = Limit(Fit(bank, slot.Size.X, ColumnGap));
			float center = area.GetCenter().X, offset = CenterAisle / 2 + bank / 2;
			columnCenters.AddRange(Centers(center - offset, perBank, slot.Size.X, ColumnGap));
			columnCenters.AddRange(Centers(center + offset, perBank, slot.Size.X, ColumnGap));
		}
		else
		{
			columnCenters.AddRange(Centers(area.GetCenter().X, Limit(Fit(area.Size.X, slot.Size.X, ColumnGap)), slot.Size.X, ColumnGap));
		}
		int count = 0;
		// Fill from the middle out, so a count limit trims both ends evenly instead of leaving one end bare.
		var middle = area.GetCenter();
		foreach (float z in Centers(middle.Y, rows, slot.Size.Z, RowGap).OrderBy(z => Mathf.Abs(z - middle.Y)))
		foreach (float x in columnCenters.OrderBy(x => Mathf.Abs(x - middle.X)))
		{
			if (count >= MaximumCount || plan.IsEmpty) return count;
			var bounds = turn * plan.Bounds();
			var center = bounds.GetCenter();
			if (!TryPlace(room, choice, plan, new Transform3D(turn.Basis, new Vector3(x - center.X, 0, z - center.Z)))) continue;
			count++;
			plan = Next(room, rng, out choice);
		}
		return count;
	}

	private int Limit(int fit) => Columns > 0 ? Mathf.Min(Columns, fit) : fit;

	private static int Fit(float available, float size, float gap) => size <= 0 ? 0 : Mathf.Max(0, Mathf.FloorToInt((available + gap) / (size + gap)));

	private static IEnumerable<float> Centers(float middle, int count, float size, float gap)
	{
		float span = count * size + (count - 1) * gap;
		for (int index = 0; index < count; index++) yield return middle - span / 2 + size / 2 + index * (size + gap);
	}
}
