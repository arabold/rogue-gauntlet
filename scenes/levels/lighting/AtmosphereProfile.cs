using Godot;

/// <summary>An Inspector-authored composition of floor effects. Each selected scene owns its appearance and tuning.</summary>
[GlobalClass]
public partial class AtmosphereProfile : Resource
{
	/// <summary>Independent scenes rooted in AtmosphereEffect; an empty collection leaves the floor's air clear.</summary>
	[Export] public PackedScene[] Effects { get; set; } = [];
}