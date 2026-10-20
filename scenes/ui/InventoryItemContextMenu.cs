using Godot;
using System.Linq;

public partial class InventoryItemContextMenu : PopupPanel
{
	/// <summary>
	/// Raised instead of consuming immediately when a targeted scroll's "Read" button is
	/// pressed. <see cref="InventoryPanel"/> handles it by entering targeting mode; the
	/// scroll is only consumed once a valid target is confirmed.
	/// </summary>
	[Signal] public delegate void OperationFailedEventHandler(string message);
	private Player _player;

	[Signal] public delegate void TargetedUseRequestedEventHandler(InventoryItemSlot slot);

	private Label _titleLabel;
	private ItemDetailsView _details;
	private Button _useButton;
	private Button _dropButton;
	private Button _equipButton;
	private Button _unequipButton;
	private Button _splitButton;
	private Button _mergeButton;

	private Inventory _inventory;
	private InventoryItemSlot _slot;

	public override void _Ready()
	{
		base._Ready();

		_titleLabel = GetNode<Label>("%TitleLabel");
		// Reuse the same details component as the hover tooltip so click and hover show the
		// same rarity-colored name and affix lines. It replaces the plain title label.
		_details = new ItemDetailsView();
		Node header = _titleLabel.GetParent();
		header.AddChild(_details);
		header.MoveChild(_details, 0);
		_titleLabel.Visible = false;

		_useButton = GetNode<Button>("%UseButton");
		_useButton.ButtonUp += () => UseItem(_inventory, _slot);
		_dropButton = GetNode<Button>("%DropButton");
		_dropButton.ButtonUp += () => DropItem(_inventory, _slot);
		_equipButton = GetNode<Button>("%EquipButton");
		_equipButton.ButtonUp += () => EquipItem(_inventory, _slot);
		_unequipButton = GetNode<Button>("%UnequipButton");
		_unequipButton.ButtonUp += () => UnequipItem(_inventory, _slot);
		_splitButton = GetNode<Button>("%SplitButton");
		_splitButton.ButtonUp += () =>
		{
			var result = _inventory.TrySplit(_slot, _slot.Quantity / 2);
			if (!result.Success) { EmitSignalOperationFailed("Make room in your backpack before splitting."); }
			Hide();
		};
		_mergeButton = GetNode<Button>("%MergeButton");
		_mergeButton.ButtonUp += () =>
		{
			var target = FindMergeTarget();
			if (target != null) { _inventory.TryMerge(_slot, target); }
			else { EmitSignalOperationFailed("No matching stack has enough room."); }
			Hide();
		};
		PopupHide += () =>
		{
			_inventory = null;
			_slot = null;
		};
	}

	public void Initialize(Inventory inventory, InventoryItemSlot slot, Player player = null)
	{
		_player = player;
		_inventory = inventory;
		_slot = slot;

		_details.SetItem(slot.Item);
		// Shrink the popup to fit its contents once the new layout is computed, so its height
		// (and width) track the item rather than a fixed authored size.
		CallDeferred(Window.MethodName.ResetSize);
		_useButton.Visible = slot.Item is ConsumableItem;
		_useButton.Text = slot.Item is Scroll ? "Read" : slot.Item is FoodItem ? "Eat" : "Drink";
		_dropButton.Visible = true;
		_splitButton.Visible = slot.Item.CanStack && slot.Quantity > 1;
		_mergeButton.Visible = slot.Item.CanStack;
		_mergeButton.Disabled = FindMergeTarget() == null;

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

	private InventoryItemSlot FindMergeTarget() => _inventory.Items.FirstOrDefault(entry => entry != _slot
		&& entry.Item.CanStack && entry.Item.DefinitionPath == _slot.Item.DefinitionPath
		&& entry.Item.MaxStackSize - entry.Quantity >= _slot.Quantity);

	private void ShowResult(ItemOperationResult? result)
	{
		if (result?.Success != true)
		{ EmitSignalOperationFailed(_player?.InventoryController.LastOperationMessage ?? "No player is available."); }
	}

	private void UseItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is Scroll { RequiresTarget: true })
		{
			EmitSignalTargetedUseRequested(slot);
			Hide();
			return;
		}

		if (slot.Item is ConsumableItem)
		{
			ShowResult(_player?.InventoryController.TryUse(slot));
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not consumable");
		}
		Hide();
	}

	private void DropItem(Inventory inventory, InventoryItemSlot slot)
	{
		ShowResult(_player?.InventoryController.TryDrop(slot, slot.Quantity));
		Hide();
	}

	private void EquipItem(Inventory inventory, InventoryItemSlot slot)
	{
		if (slot.Item is EquipableItem)
		{
			ShowResult(_player?.InventoryController.TryEquip(slot));
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
			ShowResult(_player?.InventoryController.TryUnequip(slot));
		}
		else
		{
			GD.PrintErr($"{slot.Item.Name} is not equipable");
		}
		Hide();
	}

}