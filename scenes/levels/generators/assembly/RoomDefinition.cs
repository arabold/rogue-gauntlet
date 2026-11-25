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
	/// <summary>
	/// Themes (<see cref="Realm.Theme"/>) this definition can be built in; empty lets every theme build it. Which floors
	/// show it is the realm's schedule (<see cref="Realm.Rooms"/>).
	/// </summary>
	[Export] public string[] Realms { get; set; } = [];
	/// <summary>Most copies of this definition one floor holds; 0 leaves it unlimited.</summary>
	[Export(PropertyHint.Range, "0,10")] public int MaxPerFloor { get; set; }

	/// <summary>Estimated number of realized floor regions contributed when this definition is placed.</summary>
	public virtual int EstimatedRoomCount => 1;

	/// <summary>True when this definition can be assembled for <paramref name="realm"/>.</summary>
	public virtual bool SupportsRealm(string realm) => Realm.Allows(Realms, realm);

	public abstract string[] Validate();
}
