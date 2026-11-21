using Godot;

/// <summary>
/// Shared base Resource for weighted room definitions (<see cref="RoomType"/> and <see cref="RoomGroup"/>).
/// Pure data: assembly belongs in <see cref="RoomAssembler"/>.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class RoomDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;
	/// <summary>Themes (<see cref="Realm.Theme"/>) this definition belongs to; empty lets every realm use it.</summary>
	[Export] public string[] Realms { get; set; } = [];
	/// <summary>Chance a floor offers this definition at all, rolled once per floor; below 1 makes it a rare sight, like a throne room.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float FloorChance { get; set; } = 1f;
	/// <summary>Most copies of this definition one floor holds; 0 leaves it unlimited.</summary>
	[Export(PropertyHint.Range, "0,10")] public int MaxPerFloor { get; set; }

	/// <summary>Estimated number of realized floor regions contributed when this definition is placed.</summary>
	public virtual int EstimatedRoomCount => 1;

	/// <summary>True when this definition can be assembled for <paramref name="realm"/>.</summary>
	public virtual bool SupportsRealm(string realm) => Realm.Allows(Realms, realm);

	public abstract string[] Validate();
}
