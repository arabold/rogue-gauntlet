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
	/// <summary>Realms this definition belongs to; empty lets every realm use it.</summary>
	[Export] public string[] Realms { get; set; } = [];

	/// <summary>Estimated number of realized floor regions contributed when this definition is placed.</summary>
	public virtual int EstimatedRoomCount => 1;

	/// <summary>True when this definition can be assembled for <paramref name="realm"/>.</summary>
	public virtual bool SupportsRealm(string realm) => Realm.Allows(Realms, realm);

	public abstract string[] Validate();
}
