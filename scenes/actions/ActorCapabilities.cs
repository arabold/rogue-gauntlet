using Godot;

[GlobalClass]
public partial class ActorCapabilities : Resource
{
	[Export] public bool CanOpenDoors { get; set; } = false;
	[Export] public bool CanOpenContainers { get; set; } = false;
	[Export] public bool CanUseMechanisms { get; set; } = false;
	[Export] public bool CanPickMechanicalLocks { get; set; } = false;
	[Export] public DodgeDefinition InnateDodge { get; set; }
	[Export] public GuardDefinition InnateGuard { get; set; }

	public bool HasPermission(InteractionPermission permission) => permission switch
	{
		InteractionPermission.None => true,
		InteractionPermission.OpenDoors => CanOpenDoors,
		InteractionPermission.OpenContainers => CanOpenContainers,
		InteractionPermission.UseMechanisms => CanUseMechanisms,
		InteractionPermission.PickMechanicalLocks => CanPickMechanicalLocks,
		_ => false
	};
}