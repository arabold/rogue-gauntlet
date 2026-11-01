using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The data way to compose things that belong together (a bed with a nightstand and a chest, a wine barrel with a
/// small table and bottles): an anchor plus members at offsets. Required members fit with the anchor atomically;
/// optional dressing is validated on its own and dropped when it does not fit. Mirroring flips members to the other side.
/// </summary>
[Tool, GlobalClass]
public partial class PropGroup : Placeable
{
	/// <summary>Weighted choices for the anchor, which also decides wall alignment.</summary>
	[Export] public Placeable[] Anchor { get; set; } = [];
	[Export] public PropGroupMember[] Members { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.05")] public float MirrorChance { get; set; } = .5f;

	public override void Compose(RoomBlueprint room, RandomNumberGenerator rng, PlacementPlan plan, Transform3D at, bool optional)
	{
		room.Pick(Anchor, rng)?.Compose(room, rng, plan, at, optional);
		float side = rng.Randf() < MirrorChance ? -1 : 1;
		foreach (var member in Members)
		{
			// Every member draws the same values whether or not it appears, so one chance never shifts another.
			bool include = rng.Randf() < member.Chance;
			var option = room.Pick(member.Options, rng);
			if (!include || option == null) continue;
			var local = new Transform3D(new Basis(Vector3.Up, member.QuarterTurns * side * Mathf.Pi / 2),
				new Vector3(member.Offset.X * side, member.Offset.Y, member.Offset.Z));
			option.Compose(room, rng, plan, at * local, optional: optional || !member.Required);
		}
	}

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"Group: assign a stable ID.");
		if (Anchor.Length == 0 || Anchor.Any(entry => entry == null)) errors.Add($"Group {Id}: assign anchor options.");
		if (Members.Any(member => member == null || member.Options.Length == 0 || member.Options.Any(option => option == null)))
			errors.Add($"Group {Id}: every member needs options.");
		errors.AddRange(Anchor.Where(entry => entry != null).SelectMany(entry => entry.Validate()));
		errors.AddRange(Members.Where(member => member != null).SelectMany(member => member.Options).Where(option => option != null)
			.SelectMany(option => option.Validate()));
		return errors.Distinct().ToArray();
	}
}