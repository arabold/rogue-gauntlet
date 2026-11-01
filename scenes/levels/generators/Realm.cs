using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A band of depths built from one architecture. Rooms come from the generator's shared room pool: room types, wall
/// and floor styles and furnishing steps name the realms they belong to (<see cref="Allows"/>), so each realm draws its
/// own subset of the same rooms and tiles. The realm adds its corridor tiles. Encounters stay with the generator's
/// mob factory.
/// </summary>
[Tool, GlobalClass]
public partial class Realm : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;
	[Export] public TileFactory Corridors { get; set; }

	public bool Covers(uint depth) => depth >= FirstDepth && depth <= LastDepth;

	/// <summary>Whether content tagged with <paramref name="realms"/> appears in <paramref name="realm"/>: no tags mean every realm, and no realm (previews) takes everything.</summary>
	public static bool Allows(string[] realms, string realm) => realm == null || realms.Length == 0 || realms.Contains(realm);

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Corridors == null) errors.Add($"Realm {Id}: assign an ID and its corridor tiles.");
		if (FirstDepth > LastDepth) errors.Add($"Realm {Id}: the first depth must not follow the last.");
		return errors.ToArray();
	}
}
