using Godot;
using System;

public partial class ActionSlot : Node
{
    [Signal] public delegate void PerformActionEventHandler();
    [Signal] public delegate void ActionPerformedEventHandler();
    [Signal] public delegate void CooldownStartedEventHandler();
    [Signal] public delegate void CooldownEndedEventHandler();

    public IPlayerAction AssignedAction { get; private set; }
    public PackedScene PreviewScene { get; private set; }
    public bool IsDelayed { get; private set; }
    public bool IsPerformingAction { get; private set; }
    public bool IsOnCooldown { get; private set; }
    public double TotalDuration { get; private set; }
    public double RemainingTime { get; private set; }
    public int SlotIndex { get; private set; }

    /// <summary>
    /// Assign an action to the slot.
    /// </summary>
    public void AssignAction(int slotIndex, IPlayerAction action, PackedScene previewScene)
    {
        SlotIndex = slotIndex;
        AssignedAction = action;
        PreviewScene = previewScene;
        IsOnCooldown = false;
    }

    /// <summary>
    /// Trigger the action assigned to the slot.
    /// </summary>
    /// <param name="player">The player that triggers the action.</param>
    public async void TryPerformAction(Player player)
    {
        if (AssignedAction == null || IsDelayed || IsPerformingAction || IsOnCooldown)
            return;

        TotalDuration = AssignedAction.Delay + AssignedAction.PerformDuration + AssignedAction.CooldownDuration;
        RemainingTime = TotalDuration;
        SignalBus.EmitCooldownUpdated(SlotIndex, (float)TotalDuration, (float)TotalDuration);

        if (AssignedAction.Delay > 0)
        {
            IsDelayed = true;
            await ToSignal(GetTree().CreateTimer(AssignedAction.Delay), "timeout");
        }
        IsDelayed = false;

        // Perform the action
        GD.Print($"Performing action slot {SlotIndex}");
        IsPerformingAction = true;
        EmitSignalPerformAction();
        AssignedAction.PerformAction(player);

        // Wait for perform duration
        await ToSignal(GetTree().CreateTimer(AssignedAction.PerformDuration), "timeout");
        IsPerformingAction = false;
        EmitSignal(SignalName.ActionPerformed);

        // Start cooldown
        if (AssignedAction.CooldownDuration > 0)
        {
            IsOnCooldown = true;
            EmitSignal(SignalName.CooldownStarted);
            await ToSignal(GetTree().CreateTimer(AssignedAction.CooldownDuration), "timeout");
            IsOnCooldown = false;
            EmitSignal(SignalName.CooldownEnded);
        }

        // Ensure to reset the cooldown UI
        SignalBus.EmitCooldownUpdated(SlotIndex, 0, (float)TotalDuration);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (IsDelayed || IsPerformingAction || IsOnCooldown)
        {
            RemainingTime = Math.Max(RemainingTime - delta, 0);
            SignalBus.EmitCooldownUpdated(SlotIndex, (float)RemainingTime, (float)TotalDuration);
        }
    }
}
