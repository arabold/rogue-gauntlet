using Godot;

/// <summary>
/// One pack a realm's floors can spawn: its members stand together in one room. Immutable weight and depth window;
/// what it costs a floor's budget follows from its members' threat levels (<see cref="RealmEncounterFactory.Cost"/>).
/// </summary>
[Tool]
[GlobalClass]
public partial class MobSpawnEntry : Resource
{
	/// <summary>The pack's monster scenes, one entry per monster; repeat a scene for several of it.</summary>
	[Export(PropertyHint.TypeString, "4/13:*.tscn")] public string[] Members { get; set; } = [];
	[Export(PropertyHint.Range, "1,100")] public int Weight { get; set; } = 1;
	[Export] public uint MinimumDepth { get; set; } = 1;
	/// <summary>Zero leaves the upper bound open.</summary>
	[Export] public uint MaximumDepth { get; set; }
	public bool EligibleAt(uint depth) => depth >= MinimumDepth && (MaximumDepth == 0 || depth <= MaximumDepth);
}
