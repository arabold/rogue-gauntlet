using Godot;

/// <summary>An Inspector-authored composition of floor effects. Each selected scene owns its appearance and tuning.</summary>
[Tool] // Exported by the tool Realm script: a tool script's exports must be tool types in the editor.
[GlobalClass]
public partial class AtmosphereProfile : Resource
{
	/// <summary>Independent scenes rooted in AtmosphereEffect; an empty collection leaves the floor's air clear.</summary>
	[Export] public PackedScene[] Effects { get; set; } = [];
}