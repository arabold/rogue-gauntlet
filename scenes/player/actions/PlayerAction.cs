using Godot;

[GlobalClass]
public partial class PlayerAction : ObservableResource
{
	/// <summary>
	/// The ID of the animation to play when the action is performed.
	/// The animation must be present in the player's AnimationPlayer and will
	/// be triggered immediately when the action is executed.
	/// </summary>	
	[Export] public string AnimationId { get; set => SetValue(ref field, value); }
	/// <summary>
	/// Delay before the action is performed.
	/// </summary>
	[Export] public float Delay { get; set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Duration of the action.
	/// </summary>
	[Export] public float PerformDuration { get; set => SetValue(ref field, value); } = 1.0f;
	/// <summary>
	/// Cooldown duration after the action is performed before it can be performed again.
	/// </summary>
	[Export] public float CooldownDuration { get; set => SetValue(ref field, value); } = 1.0f;

	public virtual void Trigger(Player player) { }
	public virtual void ApplyEffect(Player player) { }
	public virtual void Reset() { }
}
