using Godot;

[Tool]
public partial class SignalBus : Node
{
    // Singleton instance for global access
    public static SignalBus Instance { get; private set; }

    [Signal] public delegate void GamePausedEventHandler(bool isPaused);
    public static void EmitGamePaused(bool isPaused) => Instance?.EmitSignalGamePaused(isPaused);

    [Signal] public delegate void PlayerStatsChangedEventHandler(PlayerStats stats);
    [Signal] public delegate void PlayerActionSlotChangedEventHandler(int slotIndex, ActionSlot action);
    [Signal] public delegate void CooldownUpdatedEventHandler(int actionIndex, float remainingTime, float totalTime);
    [Signal] public delegate void PlayerSpawnedEventHandler(Player player);
    [Signal] public delegate void PlayerDeathEventHandler(Player player);
    [Signal] public delegate void XpRewardedEventHandler(int xp);
    public static void EmitPlayerStatsChanged(PlayerStats stats) => Instance?.EmitSignalPlayerStatsChanged(stats);
    public static void EmitPlayerActionSlotChanged(int slotIndex, ActionSlot action) => Instance?.EmitSignalPlayerActionSlotChanged(slotIndex, action);
    public static void EmitCooldownUpdated(int actionIndex, float remainingTime, float totalTime) => Instance?.EmitSignalCooldownUpdated(actionIndex, remainingTime, totalTime);
    public static void EmitPlayerSpawned(Player player) => Instance?.EmitSignalPlayerSpawned(player);
    public static void EmitPlayerDeath(Player player) => Instance?.EmitSignalPlayerDeath(player);
    public static void EmitXpRewarded(int xp) => Instance?.EmitSignalXpRewarded(xp);

    [Signal] public delegate void ItemConsumedEventHandler(Player player, ConsumableItem item);
    [Signal] public delegate void ItemEquippedEventHandler(Player player, EquipableItem item);
    [Signal] public delegate void ItemUnequippedEventHandler(Player player, EquipableItem item);
    [Signal] public delegate void ItemDestroyedEventHandler(Player player, Item item, int quantity);
    [Signal] public delegate void ItemDroppedEventHandler(Player player, Item item, int quantity);
    public static void EmitItemConsumed(Player player, ConsumableItem item) => Instance?.EmitSignalItemConsumed(player, item);
    public static void EmitItemEquipped(Player player, EquipableItem item) => Instance?.EmitSignalItemEquipped(player, item);
    public static void EmitItemUnequipped(Player player, EquipableItem item) => Instance?.EmitSignalItemUnequipped(player, item);
    public static void EmitItemDestroyed(Player player, Item item, int quantity) => Instance?.EmitSignalItemDestroyed(player, item, quantity);
    public static void EmitItemDropped(Player player, Item item, int quantity) => Instance?.EmitSignalItemDropped(player, item, quantity);

    [Signal] public delegate void LevelChangedEventHandler(int level);
    [Signal] public delegate void LevelLoadedEventHandler(Level level);
    public static void EmitLevelChanged(int level) => Instance?.EmitSignalLevelChanged(level);
    public static void EmitLevelLoaded(Level level) => Instance?.EmitSignalLevelLoaded(level);

    [Signal] public delegate void RoomEnteredEventHandler(int roomId);
    public static void EmitRoomEntered(int roomId) => Instance?.EmitSignalRoomEntered(roomId);

    [Signal] public delegate void DoorOpenedEventHandler(Node3D door);
    public static void EmitDoorOpened(Node3D door) => Instance?.EmitSignalDoorOpened(door);

    [Signal] public delegate void DoorClosedEventHandler(Node3D door);
    public static void EmitDoorClosed(Node3D door) => Instance?.EmitSignalDoorClosed(door);

    public override void _Ready()
    {
        // Ensure this is the only instance
        if (Instance != null && GodotObject.IsInstanceValid(Instance))
        {
            GD.PrintErr("Multiple instances of SignalBus detected!");
            QueueFree();
            return;
        }

        GD.Print("SignalBus is ready");
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base._ExitTree();
    }
}
