using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A table with seats spaced along its measured long sides (and optionally its ends), each facing the table. Coded
/// because seat count follows the chosen table's size; every seat is optional and dropped when it does not fit.
/// </summary>
[Tool, GlobalClass]
public partial class SeatedTable : Placeable
{
	[Export] public Placeable[] Tables { get; set; } = [];
	/// <summary>Seats face local +Z (see <see cref="Prop"/>); each slot picks one.</summary>
	[Export] public Prop[] Seats { get; set; } = [];
	[Export(PropertyHint.Range, "0.6,3,0.05")] public float SeatSpacing { get; set; } = 1.3f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float SeatChance { get; set; } = .8f;
	/// <summary>Gap between a seat and the table edge.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float SeatGap { get; set; } = .15f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float EndSeatChance { get; set; }

	public override void Compose(RoomBlueprint room, RandomNumberGenerator rng, PlacementPlan plan, Transform3D at, bool optional)
	{
		var table = room.Pick(Tables, rng);
		if (table == null) return;
		int first = plan.Pieces.Count;
		table.Compose(room, rng, plan, at, optional);
		// Measure the table in its own frame: the plan may already hold rotated pieces from an enclosing group.
		var bounds = MeasureLocal(plan, first, at);
		bool longZ = bounds.Size.Z >= bounds.Size.X;
		float length = longZ ? bounds.Size.Z : bounds.Size.X;
		float halfWidth = (longZ ? bounds.Size.X : bounds.Size.Z) / 2;
		var center = bounds.GetCenter();
		var along = longZ ? Vector3.Back : Vector3.Right;
		var across = longZ ? Vector3.Right : Vector3.Back;
		int perSide = Mathf.Max(1, Mathf.FloorToInt(length / SeatSpacing));
		foreach (float side in new[] { -1f, 1f })
			for (int index = 0; index < perSide; index++)
			{
				float offset = -length / 2 + (index + .5f) * length / perSide;
				AddSeat(center + along * offset, across * side, halfWidth, SeatChance);
			}
		if (EndSeatChance > 0)
			foreach (float side in new[] { -1f, 1f })
				AddSeat(center, along * side, length / 2, EndSeatChance);

		void AddSeat(Vector3 edgeCenter, Vector3 outward, float distance, float chance)
		{
			bool include = rng.Randf() < chance;
			var seat = room.Pick(Seats, rng);
			if (!include || seat == null) return;
			int variant = room.PickVariant(seat, rng);
			var envelope = seat.Geometry(variant).Envelope;
			// The seat's front (+Z) faces the table and stops SeatGap short of the table edge.
			var facing = new Basis(Vector3.Up, Mathf.Atan2(-outward.X, -outward.Z));
			var origin = new Vector3(edgeCenter.X, 0, edgeCenter.Z) + outward * (distance + SeatGap + envelope.End.Z);
			plan.Add(seat, variant, at * new Transform3D(facing, origin), optional: true);
		}
	}

	private static Aabb MeasureLocal(PlacementPlan plan, int first, Transform3D at)
	{
		var inverse = at.AffineInverse();
		Aabb? bounds = null;
		for (int index = first; index < plan.Pieces.Count; index++)
		{
			var piece = plan.Pieces[index];
			var box = inverse * piece.Local * piece.Prop.Geometry(piece.Variant).Envelope;
			bounds = bounds?.Merge(box) ?? box;
		}
		return bounds ?? new Aabb();
	}

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("Seated table: assign a stable ID.");
		if (Tables.Length == 0 || Tables.Any(table => table == null)) errors.Add($"Seated table {Id}: assign tables.");
		if (Seats.Length == 0 || Seats.Any(seat => seat == null)) errors.Add($"Seated table {Id}: assign seats.");
		errors.AddRange(Tables.Where(table => table != null).SelectMany(table => table.Validate()));
		errors.AddRange(Seats.Where(seat => seat != null).SelectMany(seat => seat.Validate()));
		return errors.Distinct().ToArray();
	}
}