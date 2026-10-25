using Godot;

[Tool]
[GlobalClass]
public partial class InteractionDefinition : Resource
{
	[Export] public string OperationId { get; set; } = "interact";
	[Export] public string VerbLabel { get; set; } = "Interact";
	[Export] public float Duration { get; set; } = 1.0f;
	[Export] public float InteractionRange { get; set; } = 2.0f;
	[Export] public InteractionPermission RequiredPermission { get; set; } = InteractionPermission.None;
}