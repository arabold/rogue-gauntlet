public interface IPlayerAction
{
    string AnimationId { get; }
    float Delay { get; }
    float PerformDuration { get; }
    float CooldownDuration { get; }
    void PerformAction(Player player);
}
