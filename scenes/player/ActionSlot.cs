using Godot;

public partial class ActionSlot : Node
{
    [Signal]
    public delegate void ActionTriggeredEventHandler();
    [Signal]
    public delegate void ActionPerformedEventHandler();
    [Signal]
    public delegate void CooldownStartedEventHandler();
    [Signal]
    public delegate void CooldownEndedEventHandler();

    public PlayerAction AssignedAction { get; private set; }
    public PackedScene PreviewScene { get; private set; }
    public bool IsPerformingAction { get; private set; }
    public bool IsOnCooldown { get; private set; }

    /// <summary>
    /// Assign an action to the slot.
    /// </summary>
    public void AssignAction(PlayerAction action, PackedScene previewScene)
    {
        AssignedAction = action;
        PreviewScene = previewScene;
        IsPerformingAction = false;
        IsOnCooldown = false;
    }

    /// <summary>
    /// Trigger the action assigned to the slot.
    /// </summary>
    /// <param name="player">The player that triggers the action.</param>
    public async void TriggerAction(Player player)
    {
        if (AssignedAction == null || IsOnCooldown)
            return;

        GD.Print($"Performing {AssignedAction}");
        IsPerformingAction = true;

        // Wait initial delay
        await ToSignal(GetTree().CreateTimer(AssignedAction.Delay), "timeout");

        // Perform the action
        AssignedAction.Trigger(player);
        EmitSignalActionTriggered();

        // Wait for perform duration
        await ToSignal(GetTree().CreateTimer(AssignedAction.PerformDuration), "timeout");
        IsPerformingAction = false;
        EmitSignalActionPerformed();

        // Apply effect
        GD.Print($"Applying effect of {AssignedAction}");
        AssignedAction.ApplyEffect(player);

        // Start cooldown
        IsOnCooldown = true;
        GD.Print($"Starting cooldown of {AssignedAction}");
        EmitSignalCooldownStarted();
        await ToSignal(GetTree().CreateTimer(AssignedAction.CooldownDuration), "timeout");
        IsOnCooldown = false;
        EmitSignalCooldownEnded();

        // Reset action if needed
        GD.Print($"Resetting {AssignedAction}");
        AssignedAction.Reset();
    }
}
