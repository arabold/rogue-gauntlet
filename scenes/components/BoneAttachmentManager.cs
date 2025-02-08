using Godot;
using Godot.Collections;

public enum AttachmentType
{
	OneHandedWeapon,
	TwoHandedWeapon,
	OffhandWeapon,
	Shield,
	Item,
	Hat,
	Cape
}

public partial class BoneAttachmentManager : Node
{
	/// <summary>
	/// The mapping of attachment types for the current character
	/// </summary>
	[Export]
	public Dictionary<AttachmentType, BoneAttachment3D> AttachmentNodes { get; set; } =
		new Dictionary<AttachmentType, BoneAttachment3D>();

	/// <summary>
	/// The player that this BoneAttachmentManager is attached to.
	/// This is used to listen to the player's inventory events.
	/// </summary>
	[Export] public Player player { get; set; }

	[Export] public bool ResetAttachmentsOnReady { get; set; } = true;

	private Inventory _inventory;

	public override void _Ready()
	{
		if (ResetAttachmentsOnReady)
		{
			// Clean up the weapon and item attachment nodes
			RemoveAttachments(AttachmentType.OneHandedWeapon);
			RemoveAttachments(AttachmentType.TwoHandedWeapon);
			RemoveAttachments(AttachmentType.OffhandWeapon);
			RemoveAttachments(AttachmentType.Shield);
			RemoveAttachments(AttachmentType.Item);

			if (player != null)
			{
				GD.Print($"Setting up BoneAttachmentManager for {player.Name}");
				_inventory = player.Inventory;
				_inventory.ItemEquipped += OnItemEquipped;
				_inventory.ItemUnequipped += OnItemUnequipped;

				foreach (var (slot, item) in _inventory.EquippedItems)
				{
					if (item != null)
					{
						OnItemEquipped(item.Item as EquipableItem, slot);
					}
				}
			}
		}
	}

	private void RemoveAttachments(AttachmentType attachmentType)
	{
		if (AttachmentNodes == null)
		{
			return;
		}

		AttachmentNodes.TryGetValue(attachmentType, out var boneAttachment);
		if (boneAttachment != null)
		{
			GD.Print($"Removing attachments from {attachmentType}");
			var children = boneAttachment.GetChildren();
			foreach (Node child in children)
			{
				boneAttachment.RemoveChild(child);
				child.QueueFree();
			}
		}
	}

	private void AddAttachment(AttachmentType attachmentType, EquipableItem item)
	{
		if (AttachmentNodes == null)
		{
			return;
		}

		AttachmentNodes.TryGetValue(attachmentType, out var boneAttachment);
		if (boneAttachment != null)
		{
			GD.Print($"Adding attachment {item.Name} to {attachmentType}");
			var itemAttachment = item.Scene.Instantiate<Node>();
			boneAttachment.AddChild(itemAttachment);
		}
	}

	private AttachmentType GetAttachmentType(EquipableItem item)
	{
		AttachmentType attachmentType = AttachmentType.Item;
		if (item is Weapon weapon)
		{
			if (weapon.IsTwoHanded)
			{
				attachmentType = AttachmentType.TwoHandedWeapon;
			}
			else
			{
				attachmentType = AttachmentType.OneHandedWeapon;
			}
		}
		else if (item is Armor armor)
		{
			if ((armor.ValidSlots & ValidSlots.ShieldHand) != 0)
			{
				attachmentType = AttachmentType.Shield;
			}
			else
			{
				attachmentType = AttachmentType.Item;
			}
		}
		return attachmentType;
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		var attachmentType = GetAttachmentType(item);
		if (attachmentType == AttachmentType.Item)
		{
			// We don't need to show items on the character
			return;
		}

		AddAttachment(attachmentType, item);
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		var attachmentType = GetAttachmentType(item);
		RemoveAttachments(attachmentType);
	}
}
