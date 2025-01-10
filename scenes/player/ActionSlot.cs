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

    public IAction AssignedAction { get; private set; }
    public bool IsPerformingAction { get; private set; }
    public bool IsOnCooldown { get; private set; }

    /// <summary>
    /// Assign an action to the slot.
    /// </summary>
    public void AssignAction(IAction action)
    {
        AssignedAction = action;
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

        // Perform the action
        GD.Print($"Performing {AssignedAction.Id}");
        IsPerformingAction = true;
        AssignedAction.Execute(player);
        EmitSignal(SignalName.ActionTriggered);

        // Wait for perform duration
        await ToSignal(GetTree().CreateTimer(AssignedAction.PerformDuration), "timeout");
        IsPerformingAction = false;
        EmitSignal(SignalName.ActionPerformed);

        // Apply effect
        GD.Print($"Applying effect of {AssignedAction.Id}");
        AssignedAction.ApplyEffect(player);

        // Start cooldown
        IsOnCooldown = true;
        GD.Print($"Starting cooldown of {AssignedAction.Id}");
        EmitSignal(SignalName.CooldownStarted);
        await ToSignal(GetTree().CreateTimer(AssignedAction.CooldownDuration), "timeout");
        IsOnCooldown = false;
        EmitSignal(SignalName.CooldownEnded);

        // Reset action if needed
        GD.Print($"Resetting {AssignedAction.Id}");
        AssignedAction.Reset();
    }
}
