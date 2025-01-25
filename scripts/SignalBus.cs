using Godot;

[Tool]
public partial class SignalBus : Node
{
    // Singleton instance for global access
    public static SignalBus Instance { get; private set; }

    [Signal] public delegate void GamePausedEventHandler(bool isPaused);
    public static void EmitGamePaused(bool isPaused) => SafeEmitSignal(SignalName.GamePaused, isPaused);

    [Signal] public delegate void XpUpdatedEventHandler(int xp);
    [Signal] public delegate void LevelUpEventHandler(int xpLevel);
    [Signal] public delegate void GoldUpdatedEventHandler(int gold);
    [Signal] public delegate void HealthChangedEventHandler(int currentHealth, int maxHealth);
    [Signal] public delegate void CooldownUpdatedEventHandler(int actionIndex, float remainingTime, float totalTime);
    [Signal] public delegate void PlayerSpawnedEventHandler(Player player);
    [Signal] public delegate void PlayerDeathEventHandler(Player player);
    public static void EmitXpUpdated(int xp) => SafeEmitSignal(SignalName.XpUpdated, xp);
    public static void EmitLevelUp(int xpLevel) => SafeEmitSignal(SignalName.LevelUp, xpLevel);
    public static void EmitGoldUpdated(int score) => SafeEmitSignal(SignalName.GoldUpdated, score);
    public static void EmitHealthChanged(int currentHealth, int maxHealth) => SafeEmitSignal(SignalName.HealthChanged, currentHealth, maxHealth);
    public static void EmitCooldownUpdated(int actionIndex, float remainingTime, float totalTime) => SafeEmitSignal(SignalName.CooldownUpdated, actionIndex, remainingTime, totalTime);
    public static void EmitPlayerSpawned(Player player) => SafeEmitSignal(SignalName.PlayerSpawned, player);
    public static void EmitPlayerDeath(Player player) => SafeEmitSignal(SignalName.PlayerDeath, player);

    [Signal] public delegate void ItemConsumedEventHandler(ConsumableItem item);
    [Signal] public delegate void ItemEquippedEventHandler(EquippableItem item);
    [Signal] public delegate void ItemUnequippedEventHandler(EquippableItem item);
    [Signal] public delegate void ItemDestroyedEventHandler(Item item);
    public static void EmitItemConsumed(ConsumableItem item) => SafeEmitSignal(SignalName.ItemConsumed, item);
    public static void EmitItemEquipped(EquippableItem item) => SafeEmitSignal(SignalName.ItemEquipped, item);
    public static void EmitItemUnequipped(EquippableItem item) => SafeEmitSignal(SignalName.ItemUnequipped, item);
    public static void EmitItemDestroyed(Item item) => SafeEmitSignal(SignalName.ItemDestroyed, item);

    [Signal] public delegate void LevelChangedEventHandler(int level);
    [Signal] public delegate void LevelLoadedEventHandler(Level level);
    public static void EmitLevelChanged(int level) => SafeEmitSignal(SignalName.LevelChanged, level);
    public static void EmitLevelLoaded(Level level) => SafeEmitSignal(SignalName.LevelLoaded, level);

    public override void _Ready()
    {
        // Ensure this is the only instance
        if (Instance != null)
        {
            GD.PrintErr("Multiple instances of SignalBus detected!");
            QueueFree();
            return;
        }

        GD.Print("SignalBus is ready");
        Instance = this;
    }

    private static void SafeEmitSignal(string signalName, params Variant[] args)
    {
        // When used in the editor, the SignalBus is not available
        if (Instance != null)
        {
            Instance.EmitSignal(signalName, args);
        }
    }
}
