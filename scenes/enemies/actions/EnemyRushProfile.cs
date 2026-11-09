using Godot;

/// <summary>A short movement action executed by the actor's existing MovementComponent.</summary>
[GlobalClass]
public partial class EnemyRushProfile : EnemyActionProfile
{
	[ExportGroup("Rush")]
	[Export] public RushProfile Rush { get; set; }

	public override bool HasValidConfiguration => base.HasValidConfiguration
		&& Rush?.IsValid == true
		&& SlotKind == EnemyActionSlotKind.None && !RiposteEligible;
}
