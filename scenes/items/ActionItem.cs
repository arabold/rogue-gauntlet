using Godot;

/// <summary>
/// Base class for items that can be bound to player actions.
/// </summary>
[GlobalClass]
public partial class ActionItem : Item
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
    [Export] public float Delay { get; set => SetValue(ref field, value); } = 0f;
    /// <summary>
    /// Duration of the action.
    /// </summary>
    [Export] public float PerformDuration { get; set => SetValue(ref field, value); } = 0f;
    /// <summary>
    /// Cooldown duration after the action is performed before it can be performed again.
    /// </summary>
    [Export] public float CooldownDuration { get; set => SetValue(ref field, value); } = 0f;

    protected void PerformAction(Player player)
    {
    }
}
