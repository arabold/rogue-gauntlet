using Godot;

public partial class InventoryItemContextMenu : PopupPanel
{
	private Label _titleLabel;
	private Button _useButton;
	private Button _dropButton;
	private Button _equipButton;
	private Button _unequipButton;

	private Inventory _inventory;
	private InventoryItemSlot _slot;

	public override void _Ready()
	{
		base._Ready();

		_titleLabel = GetNode<Label>("%TitleLabel");
		_useButton = GetNode<Button>("%UseButton");
		_useButton.ButtonUp += () => UseItem(_inventory, _slot);
		_dropButton = GetNode<Button>("%DropButton");
		_dropButton.ButtonUp += () => DropItem(_inventory, _slot);
		_equipButton = GetNode<Button>("%EquipButton");
		_equipButton.ButtonUp += () => EquipItem(_inventory, _slot);
		_unequipButton = GetNode<Button>("%UnequipButton");
		_unequipButton.ButtonUp += () => UnequipItem(_inventory, _slot);
		PopupHide += () =>
		{
			_inventory = null;
			_slot = null;
		};
	}

	public void Initialize(Inventory inventory, InventoryItemSlot slot)
	{
		_inventory = inventory;
		_slot = slot;

		_titleLabel.Text = ItemIdentity.ResolveDisplayName(slot.Item);
		_useButton.Visible = slot.Item is ConsumableItem;
		_dropButton.Visible = true;

		if (inventory.IsEquipped(slot))
		{
			_equipButton.Visible = false;
			_unequipButton.Visible = true;
			_dropButton.Disabled = true;
		}
		else
		{
			_equipButton.Visible = slot.Item is EquipableItem;
			_unequipButton.Visible = false;
			_dropButton.Disabled = slot.Item.IsQuestItem;
		}
	}

	private void UseItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is ConsumableItem)
		{
			inventory.Consume(slot);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not consumable");
		}
		Hide();
	}

	private void DropItem(Inventory inventory, InventoryItemSlot slot)
	{
		inventory.DropItem(slot);
		Hide();
	}

	private void EquipItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is EquipableItem)
		{
			inventory.Equip(slot);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not equipable");
		}
		Hide();
	}

	private void UnequipItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is EquipableItem)
		{
			inventory.Unequip(slot);
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not equipable");
		}
		Hide();
	}

}
