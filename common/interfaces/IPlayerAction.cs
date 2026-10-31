public interface IPlayerAction
{
    float Delay { get; }
    float PerformDuration { get; }
    float CooldownDuration { get; }
    void PerformAction(Player player);
}
