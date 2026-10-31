using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Column strategy: where free-standing columns go, in symmetric groups placed all-or-nothing. One column model and
/// variant is used for the whole room. Groups that would collide or cut a doorway off are omitted.
/// </summary>
[Tool, GlobalClass]
public abstract partial class ColumnPattern : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	[Export] public Prop[] Column { get; set; } = [];

	/// <summary>Candidate groups in room-local positions; each group is placed as a whole or skipped.</summary>
	protected abstract IEnumerable<Vector3[]> Groups(RoomFootprint footprint, RandomNumberGenerator rng);

	public string[] Validate() => Column.Length == 0 || Column.Any(column => column == null)
		? [$"Column pattern {Id}: assign column props."]
		: Column.SelectMany(column => column.Validate()).ToArray();

	public int Place(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var column = room.Pick(Column, rng);
		if (column == null) return 0;
		int variant = room.PickVariant(column, rng);
		int placed = 0;
		foreach (var group in Groups(room.Footprint, rng))
		{
			var plan = new PlacementPlan();
			foreach (var point in group) plan.Add(column, variant, new Transform3D(Basis.Identity, point), optional: false);
			if (room.TryPlace(plan, Transform3D.Identity)) placed += group.Length;
		}
		return placed;
	}

	/// <summary>Points at interior tile corners only, so every column stands clear of the walls.</summary>
	protected static Vector3[] Corners(RoomFootprint footprint, params Vector2I[] corners) =>
		corners.Distinct().All(footprint.IsInteriorCorner) ? corners.Distinct().Select(RoomFootprint.CornerPoint).ToArray() : [];

	/// <summary>Evenly spaced corner indices between 1 and <paramref name="tiles"/>-1, centered on the span.</summary>
	protected static IEnumerable<int> Spaced(int tiles, int spacing)
	{
		if (tiles < 2) yield break;
		spacing = Mathf.Max(1, spacing);
		int count = (tiles - 2) / spacing + 1;
		int first = 1 + ((tiles - 2) - (count - 1) * spacing) / 2;
		for (int index = 0; index < count; index++) yield return first + index * spacing;
	}
}
