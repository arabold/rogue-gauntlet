using Godot;

public partial class InventoryItemContextMenu : PopupPanel
{
	private Button _useButton;
	private Button _dropButton;
	private Button _equipButton;
	private Button _unequipButton;
	private Button _splitButton;

	private Inventory _inventory;
	private InventoryItemSlot _slot;

	public override void _Ready()
	{
		base._Ready();

		_useButton = GetNode<Button>("%UseButton");
		_useButton.ButtonUp += () => UseItem(_inventory, _slot);
		_dropButton = GetNode<Button>("%DropButton");
		_dropButton.ButtonUp += () => DropItem(_inventory, _slot);
		_equipButton = GetNode<Button>("%EquipButton");
		_equipButton.ButtonUp += () => EquipItem(_inventory, _slot);
		_unequipButton = GetNode<Button>("%UnequipButton");
		_unequipButton.ButtonUp += () => UnequipItem(_inventory, _slot);
		_splitButton = GetNode<Button>("%SplitButton");
		_splitButton.ButtonUp += () => SplitItem(_inventory, _slot);

		PopupHide += () =>
		{
			// Clean up
			_inventory = null;
			_slot = null;
		};
	}

	public void Initialize(Inventory inventory, InventoryItemSlot slot)
	{
		_inventory = inventory;
		_slot = slot;

		_useButton.Visible = slot.Item is ConsumableItem;
		_splitButton.Visible = slot.Item.IsStackable;
		_splitButton.Disabled = slot.Quantity <= 1;
		_dropButton.Visible = true;

		if (inventory.GetEquipmentSlot(slot.Item) != null)
		{
			_equipButton.Visible = false;
			_unequipButton.Visible = true;
			_dropButton.Disabled = true;
		}
		else
		{
			_equipButton.Visible = slot.Item is EquippableItem;
			_unequipButton.Visible = false;
			_dropButton.Disabled = false;
		}
	}

	private void UseItem(Inventory inventory, InventoryItemSlot slot)
	{
		// FIXME: This seems convoluted and should be refactored
		Player player = GameManager.Instance.Player;
		if (slot.Item is ConsumableItem consumableItem)
		{
			consumableItem.Consume(player);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not consumable");
		}
	}

	private void DropItem(Inventory inventory, InventoryItemSlot slot)
	{
		// inventory.RemoveItem(slot.Item);
		Hide();
	}

	private void EquipItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is EquippableItem equippableItem)
		{
			inventory.Equip(equippableItem);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not equippable");
		}
		Hide();
	}

	private void UnequipItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is EquippableItem equippableItem)
		{
			inventory.Unequip(equippableItem);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not equippable");
		}
		Hide();
	}

	private void SplitItem(Inventory inventory, InventoryItemSlot slot)
	{
		Hide();
	}
}
