using Godot;
using System.Collections.Generic;
using System.Linq;

public enum AttachmentType
{
	OneHandedWeapon,
	TwoHandedWeapon,
	OffhandWeapon,
	Item,
	Hat,
	Cape
}

public enum EquipmentSlot
{
	OneHandAxe,
	TwoHandAxe,
	RoundShield,
	OffhandAxe,
	Mug,
	BarbarianHat,
	BarbarianCape
}

public partial class BoneAttachmentManager : Node
{
	[Export]
	public Node3D CharacterNode { get; set; }

	private Dictionary<string, Dictionary<EquipmentSlot, string>> _characterEquipmentPaths;
	private Dictionary<string, BoneAttachment3D> _attachmentNodes;
	private Dictionary<EquipmentSlot, AttachmentType> _slotTypes;
	private string _currentCharacterType = "Barbarian";

	public override void _Ready()
	{
		InitializeEquipmentMappings();
		InitializeSlotTypes();
		_attachmentNodes = new Dictionary<string, BoneAttachment3D>();

		// Cache all attachment nodes
		foreach (var attachmentPath in _characterEquipmentPaths[_currentCharacterType].Values)
		{
			var node = GetNodeOrNull<BoneAttachment3D>($"{CharacterNode.GetPath()}/Rig/Skeleton3D/{attachmentPath}");
			if (node != null)
			{
				_attachmentNodes[attachmentPath] = node;
			}
		}

		HideAllAttachments();
		Equip(EquipmentSlot.OneHandAxe);
		Equip(EquipmentSlot.BarbarianHat);
		Equip(EquipmentSlot.BarbarianCape);
	}

	private void InitializeEquipmentMappings()
	{
		_characterEquipmentPaths = new Dictionary<string, Dictionary<EquipmentSlot, string>>
			{
				{ "Barbarian", new Dictionary<EquipmentSlot, string>
					{
						{ EquipmentSlot.OneHandAxe, "1H_Axe" },
						{ EquipmentSlot.TwoHandAxe, "2H_Axe" },
						{ EquipmentSlot.RoundShield, "Barbarian_Round_Shield" },
						{ EquipmentSlot.OffhandAxe, "1H_Axe_Offhand" },
						{ EquipmentSlot.Mug, "Mug" },
						{ EquipmentSlot.BarbarianHat, "Barbarian_Hat" },
						{ EquipmentSlot.BarbarianCape, "Barbarian_Cape" }
					}
				}
			};
	}

	private void InitializeSlotTypes()
	{
		_slotTypes = new Dictionary<EquipmentSlot, AttachmentType>
			{
				{ EquipmentSlot.OneHandAxe, AttachmentType.OneHandedWeapon },
				{ EquipmentSlot.TwoHandAxe, AttachmentType.TwoHandedWeapon },
				{ EquipmentSlot.RoundShield, AttachmentType.OffhandWeapon },
				{ EquipmentSlot.OffhandAxe, AttachmentType.OffhandWeapon },
				{ EquipmentSlot.Mug, AttachmentType.Item },
				{ EquipmentSlot.BarbarianHat, AttachmentType.Hat },
				{ EquipmentSlot.BarbarianCape, AttachmentType.Cape }
			};
	}

	private void ShowAttachment(EquipmentSlot slot, bool isVisible)
	{
		var path = _characterEquipmentPaths[_currentCharacterType][slot];
		if (_attachmentNodes.TryGetValue(path, out var node))
		{
			node.Visible = isVisible;
		}
	}

	public void HideAllAttachments()
	{
		foreach (var node in _attachmentNodes.Values)
		{
			node.Visible = false;
		}
	}

	public void Equip(EquipmentSlot slot)
	{
		var type = _slotTypes[slot];

		switch (type)
		{
			case AttachmentType.TwoHandedWeapon:
				// Hide all weapons and items and all other two-handed weapons
				foreach (var equipSlot in _slotTypes.Where(x =>
					x.Value == AttachmentType.OneHandedWeapon ||
					x.Value == AttachmentType.OffhandWeapon ||
					x.Value == AttachmentType.Item ||
					(x.Value == AttachmentType.TwoHandedWeapon && x.Key != slot)))
				{
					ShowAttachment(equipSlot.Key, false);
				}
				break;

			case AttachmentType.OneHandedWeapon:
				// Hide two-handed weapons and other one-handed weapons in same slots
				foreach (var equipSlot in _slotTypes.Where(x =>
					x.Value == AttachmentType.TwoHandedWeapon ||
					(x.Value == AttachmentType.OneHandedWeapon && x.Key != slot)))
				{
					ShowAttachment(equipSlot.Key, false);
				}
				break;

			case AttachmentType.OffhandWeapon:
				// Hide two-handed weapons and other offhand items
				foreach (var equipSlot in _slotTypes.Where(x =>
					x.Value == AttachmentType.TwoHandedWeapon ||
					(x.Value == AttachmentType.OffhandWeapon && x.Key != slot)))
				{
					ShowAttachment(equipSlot.Key, false);
				}
				break;

			case AttachmentType.Item:
				// Hide two-handed weapons and other items in same slot
				foreach (var equipSlot in _slotTypes.Where(x =>
					x.Value == AttachmentType.TwoHandedWeapon ||
					(x.Value == AttachmentType.Item && x.Key != slot)))
				{
					ShowAttachment(equipSlot.Key, false);
				}
				break;

			default:
				// For other types, just hide items of same type in different slots
				foreach (var equipSlot in _slotTypes.Where(x =>
					x.Value == type && x.Key != slot))
				{
					ShowAttachment(equipSlot.Key, false);
				}
				break;
		}

		// Show the requested equipment
		ShowAttachment(slot, true);
	}
}
