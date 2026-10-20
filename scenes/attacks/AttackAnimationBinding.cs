using Godot;

/// <summary>Maps an attack style to one actor rig's clip and normalized contact window.</summary>
[GlobalClass]
public partial class AttackAnimationBinding : Resource
{
	[Export] public StringName Style { get; set; } = "";
	[Export] public StringName Clip { get; set; } = "";
	[Export(PropertyHint.Range, "0,1,0.01")] public float ContactStart { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ContactEnd { get; set; } = 0.7f;

	/// <summary>Reject incomplete or ambiguous bindings before accepting an action.</summary>
	public bool IsValid => !string.IsNullOrWhiteSpace(Style) && !string.IsNullOrWhiteSpace(Clip)
		&& float.IsFinite(ContactStart) && float.IsFinite(ContactEnd)
		&& ContactStart >= 0 && ContactEnd >= ContactStart && ContactEnd <= 1;
}