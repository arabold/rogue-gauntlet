using Godot;

public partial class EquipmentPanel : PanelContainer
{
	public EquipmentItemPanel AmuletPanel { get; private set; }
	public EquipmentItemPanel HelmetPanel { get; private set; }
	public EquipmentItemPanel ArrowsPanel { get; private set; }
	public EquipmentItemPanel WeaponPanel { get; private set; }
	public EquipmentItemPanel ArmorPanel { get; private set; }
	public EquipmentItemPanel ShieldPanel { get; private set; }
	public EquipmentItemPanel RightRingPanel { get; private set; }
	public EquipmentItemPanel GlovesPanel { get; private set; }
	public EquipmentItemPanel LeftRingPanel { get; private set; }
	public EquipmentItemPanel LegsPanel { get; private set; }
	public EquipmentItemPanel BootsPanel { get; private set; }

	private Inventory _inventory;

	public override void _Ready()
	{
		AmuletPanel = GetNode<EquipmentItemPanel>("%AmuletPanel");
		HelmetPanel = GetNode<EquipmentItemPanel>("%HelmetPanel");
		ArrowsPanel = GetNode<EquipmentItemPanel>("%ArrowsPanel");
		WeaponPanel = GetNode<EquipmentItemPanel>("%WeaponPanel");
		ArmorPanel = GetNode<EquipmentItemPanel>("%ArmorPanel");
		ShieldPanel = GetNode<EquipmentItemPanel>("%ShieldPanel");
		RightRingPanel = GetNode<EquipmentItemPanel>("%RightRingPanel");
		GlovesPanel = GetNode<EquipmentItemPanel>("%GlovesPanel");
		LeftRingPanel = GetNode<EquipmentItemPanel>("%LeftRingPanel");
		LegsPanel = GetNode<EquipmentItemPanel>("%LegsPanel");
		BootsPanel = GetNode<EquipmentItemPanel>("%BootsPanel");
	}

	public void Initialize(Inventory inventory)
	{
		if (_inventory != null)
		{
			// Unsubscribe from old inventory
			_inventory.ItemEquipped -= OnItemEquipped;
			_inventory.ItemUnequipped -= OnItemUnequipped;
		}
		_inventory = inventory;
		if (_inventory != null)
		{
			_inventory.ItemEquipped += OnItemEquipped;
			_inventory.ItemUnequipped += OnItemUnequipped;

			foreach (var slot in _inventory.EquippedItems)
			{
				OnItemEquipped(slot.Value?.Item as EquipableItem, slot.Key);
			}
		}
	}

	public override void _ExitTree()
	{
		if (_inventory != null)
		{
			_inventory.ItemEquipped -= OnItemEquipped;
			_inventory.ItemUnequipped -= OnItemUnequipped;
			_inventory = null;
		}

		base._ExitTree();
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		switch (slot)
		{
			case EquipmentSlot.Neck:
				AmuletPanel.SetItem(item);
				break;
			case EquipmentSlot.Head:
				HelmetPanel.SetItem(item);
				break;
			case EquipmentSlot.Arrows:
				ArrowsPanel.SetItem(item);
				break;
			case EquipmentSlot.WeaponHand:
				WeaponPanel.SetItem(item);
				if (item is Weapon weapon && weapon.IsTwoHanded)
				{
					ShieldPanel.SetItem(item);
				}
				break;
			case EquipmentSlot.Chest:
				ArmorPanel.SetItem(item);
				break;
			case EquipmentSlot.ShieldHand:
				ShieldPanel.SetItem(item);
				break;
			case EquipmentSlot.RightRing:
				RightRingPanel.SetItem(item);
				break;
			case EquipmentSlot.Hands:
				GlovesPanel.SetItem(item);
				break;
			case EquipmentSlot.LeftRing:
				LeftRingPanel.SetItem(item);
				break;
			case EquipmentSlot.Legs:
				LegsPanel.SetItem(item);
				break;
			case EquipmentSlot.Feet:
				BootsPanel.SetItem(item);
				break;
		}
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		if (item is Weapon weapon && weapon.IsTwoHanded)
		{
			ShieldPanel.SetItem(null);
		}
		OnItemEquipped(null, slot);
	}
}
