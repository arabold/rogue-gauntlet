using Godot;

/// <summary>One health threshold and its complete combat policy; phase state remains actor-local.</summary>
[GlobalClass]
public partial class BossPhaseProfile : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float EnterHealthFraction { get; set; } = 0.5f;
	[Export] public EnemyCombatProfile Combat { get; set; }
}
