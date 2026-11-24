using Godot;

[Tool]
public partial class SignalBus : Node
{
	public static event CombatContactFeedbackHandler CombatContactResolved;
	public static event CameraImpulseRequestHandler CameraImpulseRequested;
	public static void EmitCameraImpulseRequested(in CameraImpulseRequest request)
	{
		if (CameraImpulseRequested == null) return;
		foreach (CameraImpulseRequestHandler handler in CameraImpulseRequested.GetInvocationList())
		{
			try { handler(in request); }
			catch (System.Exception error)
			{
				CameraImpulseRequested -= handler;
				GD.PushWarning($"Camera impulse observer disabled: {error.Message}");
			}
		}
	}
	public static void EmitCombatContactResolved(in CombatContactFeedback feedback)
	{
		if (CombatContactResolved == null) return;
		foreach (CombatContactFeedbackHandler handler in CombatContactResolved.GetInvocationList())
		{
			try { handler(in feedback); }
			catch (System.Exception error)
			{
				CombatContactResolved -= handler;
				GD.PushWarning($"Combat contact observer disabled: {error.Message}");
			}
		}
	}

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
    /// <summary>Fired once per level the hero gains, after their attributes and max health grew.</summary>
    [Signal] public delegate void PlayerLeveledUpEventHandler(int level);
    public static void EmitPlayerLeveledUp(int level) => Instance?.EmitSignalPlayerLeveledUp(level);

    [Signal] public delegate void ItemConsumedEventHandler(Player player, ConsumableItem item);
    [Signal] public delegate void ItemEquippedEventHandler(Player player, EquipableItem item);
    [Signal] public delegate void ItemUnequippedEventHandler(Player player, EquipableItem item);
    [Signal] public delegate void ItemDestroyedEventHandler(Player player, Item item, int quantity);
    [Signal] public delegate void ItemDroppedEventHandler(Player player, Item item, int quantity);
    [Signal] public delegate void ItemPickedUpEventHandler(Player player, Item item, int quantity);
    /// <summary>A thief took an item stack from the hero's pack; <c>thief</c> is its display name.</summary>
    [Signal] public delegate void ItemStolenEventHandler(Player player, Item item, int quantity, string thief);
    /// <summary>A thief vanished with the item stack it stole, which is gone for good.</summary>
    [Signal] public delegate void ThiefEscapedEventHandler(Item item, int quantity, string thief);
    /// <summary>Fired when an item type's hidden identity is newly discovered this run.</summary>
    [Signal] public delegate void ItemIdentifiedEventHandler(string typeId);
    /// <summary>
    /// Fired when an item type's hidden identity is revealed with its disguised and true name, and how
    /// (an <see cref="IdentificationSource"/>: Use, Wear or Scroll).
    /// </summary>
    [Signal] public delegate void ItemIdentityRevealedEventHandler(string typeId, string disguisedName, string trueName, string source);
    public static void EmitItemConsumed(Player player, ConsumableItem item) => Instance?.EmitSignalItemConsumed(player, item);
    public static void EmitItemIdentified(string typeId) => Instance?.EmitSignalItemIdentified(typeId);
    public static void EmitItemIdentityRevealed(string typeId, string disguisedName, string trueName, string source) =>
        Instance?.EmitSignalItemIdentityRevealed(typeId, disguisedName, trueName, source);
    public static void EmitItemEquipped(Player player, EquipableItem item) => Instance?.EmitSignalItemEquipped(player, item);
    public static void EmitItemUnequipped(Player player, EquipableItem item) => Instance?.EmitSignalItemUnequipped(player, item);
    public static void EmitItemDestroyed(Player player, Item item, int quantity) => Instance?.EmitSignalItemDestroyed(player, item, quantity);
    public static void EmitItemDropped(Player player, Item item, int quantity) => Instance?.EmitSignalItemDropped(player, item, quantity);
    public static void EmitItemPickedUp(Player player, Item item, int quantity) => Instance?.EmitSignalItemPickedUp(player, item, quantity);
    public static void EmitItemStolen(Player player, Item item, int quantity, string thief) =>
        Instance?.EmitSignalItemStolen(player, item, quantity, thief);
    public static void EmitThiefEscaped(Item item, int quantity, string thief) => Instance?.EmitSignalThiefEscaped(item, quantity, thief);

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

    /// <summary>Fired when an x-ray toggle changes in the debug menu so silhouettes refresh.</summary>
    [Signal] public delegate void XraySettingsChangedEventHandler();
    public static void EmitXraySettingsChanged() => Instance?.EmitSignalXraySettingsChanged();

    /// <summary>Fired when a gameplay status condition, buff, or curse effect describes itself to the hero.</summary>
    [Signal] public delegate void PlayerStatusMessageEventHandler(string message, int severity);
    public static void EmitPlayerStatusMessage(string message, LogSeverity severity = LogSeverity.Info) =>
        Instance?.EmitSignalPlayerStatusMessage(message, (int)severity);

    /// <summary>
    /// Fired when the hero finishes a keeper's trade interaction: the keeper offers its shelf. The run menu answers by
    /// opening its trade tabs, or says through <see cref="PlayerStatusMessage"/> why it cannot.
    /// </summary>
    [Signal] public delegate void TradeOfferedEventHandler(ShopComponent shop, Player player);
    public static void EmitTradeOffered(ShopComponent shop, Player player) => Instance?.EmitSignalTradeOffered(shop, player);

    /// <summary>Fired when a new buff or status activation is applied to the player.</summary>
    [Signal] public delegate void PlayerBuffAppliedEventHandler(Buff buff, bool hadCondition);
    public static void EmitPlayerBuffApplied(Buff buff, bool hadCondition) =>
        Instance?.EmitSignalPlayerBuffApplied(buff, hadCondition);

    /// <summary>Fired when an existing stack/refresh buff on the player resets its duration.</summary>
    [Signal] public delegate void PlayerBuffRefreshedEventHandler(Buff buff);
    public static void EmitPlayerBuffRefreshed(Buff buff) =>
        Instance?.EmitSignalPlayerBuffRefreshed(buff);

    /// <summary>Fired when a buff or status condition completely finishes on the player.</summary>
    [Signal] public delegate void PlayerBuffEndedEventHandler(Buff buff);
    public static void EmitPlayerBuffEnded(Buff buff) =>
        Instance?.EmitSignalPlayerBuffEnded(buff);

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
