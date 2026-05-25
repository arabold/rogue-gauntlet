using Godot;

/// <summary>
/// Handles inventory side effects for a player actor.
/// </summary>
public partial class PlayerInventoryController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public Inventory Inventory { get; set; }
	[Export] public ActionManager ActionManager { get; set; }
	[Export] public PlayerStatsController StatsController { get; set; }
	[Export] public Level Level { get; set; }
	[Export] public PackedScene LootableItemScene { get; set; }

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		Inventory ??= Player.Inventory;
		ActionManager ??= Player.ActionManager;
		StatsController ??= Player.StatsController;
		Level ??= this.GetAncestorOrNull<Level>();
		LootableItemScene ??= Player.LootableItemScene;

		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemEquipped += OnItemEquipped,
			inventory => inventory.ItemEquipped -= OnItemEquipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemUnequipped += OnItemUnequipped,
			inventory => inventory.ItemUnequipped -= OnItemUnequipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemConsumed += OnItemConsumed,
			inventory => inventory.ItemConsumed -= OnItemConsumed);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemDropped += OnItemDropped,
			inventory => inventory.ItemDropped -= OnItemDropped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemDestroyed += OnItemDestroyed,
			inventory => inventory.ItemDestroyed -= OnItemDestroyed);
	}

	public void AutoEquipItems()
	{
		foreach (var item in Inventory.Items)
		{
			if (item.Item is EquipableItem)
			{
				Inventory.Equip(item);
			}
		}
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		item.OnEquipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemEquipped(Player, item);

		if (item is IPlayerAction action)
		{
			ActionManager.AssignFirstAvailableAction(action, item.Scene);
		}
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		item.OnUnequipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemUnequipped(Player, item);

		if (item is IPlayerAction action)
		{
			ActionManager.ClearAction(action);
		}
	}

	private void OnItemConsumed(ConsumableItem item)
	{
		item.PerformAction(Player);
		SignalBus.EmitItemConsumed(Player, item);
	}

	private void OnItemDropped(Item item, int quantity)
	{
		GD.Print($"{Player.Name} dropped {quantity}x {item.Name}");

		if (LootableItemScene == null)
		{
			GD.PrintErr($"{Name} has no lootable item scene assigned.");
			return;
		}

		var lootableItem = LootableItemScene.Instantiate<LootableItem>();
		lootableItem.Item = item;
		lootableItem.Quantity = quantity;
		lootableItem.WaitForPlayerExited = true;

		Level.AddWorldNode(lootableItem, Player.GlobalPosition);

		item.OnDropped(Player, quantity);
		SignalBus.EmitItemDropped(Player, item, quantity);
	}

	private void OnItemDestroyed(Item item, int quantity)
	{
		item.OnDestroyed(Player, quantity);
		SignalBus.EmitItemDestroyed(Player, item, quantity);
	}
}
