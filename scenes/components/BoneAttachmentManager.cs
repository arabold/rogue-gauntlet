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

public enum AttachmentEquipmentSlot
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

	private Dictionary<string, Dictionary<AttachmentEquipmentSlot, string>> _characterEquipmentPaths;
	private Dictionary<string, BoneAttachment3D> _attachmentNodes;
	private Dictionary<AttachmentEquipmentSlot, AttachmentType> _slotTypes;
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
		Equip(AttachmentEquipmentSlot.OneHandAxe);
		Equip(AttachmentEquipmentSlot.BarbarianHat);
		Equip(AttachmentEquipmentSlot.BarbarianCape);
	}

	private void InitializeEquipmentMappings()
	{
		_characterEquipmentPaths = new Dictionary<string, Dictionary<AttachmentEquipmentSlot, string>>
			{
				{ "Barbarian", new Dictionary<AttachmentEquipmentSlot, string>
					{
						{ AttachmentEquipmentSlot.OneHandAxe, "1H_Axe" },
						{ AttachmentEquipmentSlot.TwoHandAxe, "2H_Axe" },
						{ AttachmentEquipmentSlot.RoundShield, "Barbarian_Round_Shield" },
						{ AttachmentEquipmentSlot.OffhandAxe, "1H_Axe_Offhand" },
						{ AttachmentEquipmentSlot.Mug, "Mug" },
						{ AttachmentEquipmentSlot.BarbarianHat, "Barbarian_Hat" },
						{ AttachmentEquipmentSlot.BarbarianCape, "Barbarian_Cape" }
					}
				}
			};
	}

	private void InitializeSlotTypes()
	{
		_slotTypes = new Dictionary<AttachmentEquipmentSlot, AttachmentType>
			{
				{ AttachmentEquipmentSlot.OneHandAxe, AttachmentType.OneHandedWeapon },
				{ AttachmentEquipmentSlot.TwoHandAxe, AttachmentType.TwoHandedWeapon },
				{ AttachmentEquipmentSlot.RoundShield, AttachmentType.OffhandWeapon },
				{ AttachmentEquipmentSlot.OffhandAxe, AttachmentType.OffhandWeapon },
				{ AttachmentEquipmentSlot.Mug, AttachmentType.Item },
				{ AttachmentEquipmentSlot.BarbarianHat, AttachmentType.Hat },
				{ AttachmentEquipmentSlot.BarbarianCape, AttachmentType.Cape }
			};
	}

	private void ShowAttachment(AttachmentEquipmentSlot slot, bool isVisible)
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

	public void Equip(AttachmentEquipmentSlot slot)
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
