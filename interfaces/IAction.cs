public interface IAction
{
    /// <summary>
    /// The name of the action.
    /// </summary>
    string Id { get; }
    /// <summary>
    /// The duration of the action.
    /// </summary>
    float PerformDuration { get; }
    /// <summary>
    /// The duration of the cooldown after the action.
    /// </summary>
    float CooldownDuration { get; }

    void Execute(Player player);        // Start the action
    void ApplyEffect(Player player);    // Apply the effect of the action
    void Reset();                       // Reset action state
}
