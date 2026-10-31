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
	Cape,
	Chest
}

[GlobalClass]
public partial class BoneAttachmentManager : Node
{
	private const string EquippedMetaKey = "equipped_attachment";

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

	/// <summary>Decorates each held item with its aura: a known curse or a spell weapon's element.</summary>
	[Export] public PackedScene AuraPresentation { get; set; }
	[Export] public bool ResetAttachmentsOnReady { get; set; } = true;

	/// <summary>
	/// Shared off-hand socket for shields. Only some class models ship a shield prop bone, so
	/// mounting on the common hand bone shows shields on every class.
	/// </summary>
	[Export] public WeaponGrip ShieldGrip { get; set; }

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
			RemoveAttachments(AttachmentType.Hat);
			RemoveAttachments(AttachmentType.Chest);

			if (player != null)
			{
				GD.Print($"Setting up BoneAttachmentManager for {player.Name}");
				Inventory playerInventory = player.Inventory;
				this.SubscribeUntilExit(
					playerInventory,
					inventory => inventory.ItemEquipped += OnItemEquipped,
					inventory => inventory.ItemEquipped -= OnItemEquipped);
				this.SubscribeUntilExit(
					playerInventory,
					inventory => inventory.ItemUnequipped += OnItemUnequipped,
					inventory => inventory.ItemUnequipped -= OnItemUnequipped);

				foreach (var (slot, item) in playerInventory.EquippedItems)
				{
					if (item != null)
					{
						OnItemEquipped(item.Item as EquipableItem, slot);
					}
				}
			}
		}
	}

	private readonly System.Collections.Generic.Dictionary<AttachmentType, BoneAttachment3D> _weaponMounts = new();

	/// <summary>Use the shared hand socket rather than a character's weapon- or shield-specific prop bone.</summary>
	private BoneAttachment3D GetWeaponMount(AttachmentType type, WeaponGrip grip)
	{
		Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
		if (skeleton == null || skeleton.FindBone(grip.Bone) < 0)
		{
			GD.PushWarning($"{Name}: missing weapon grip bone '{grip.Bone}'.");
			return null;
		}
		if (!_weaponMounts.TryGetValue(type, out var mount))
		{
			RemoveAttachments(type);
			mount = new BoneAttachment3D { Name = $"Runtime_{type}", BoneName = grip.Bone };
			skeleton.AddChild(mount);
			_weaponMounts[type] = mount;
			AttachmentNodes[type] = mount;
		}
		mount.BoneName = grip.Bone;
		return mount;
	}

	private BoneAttachment3D GetOrCreateBoneAttachment(AttachmentType attachmentType)
	{
		if (AttachmentNodes == null)
		{
			return null;
		}

		if (AttachmentNodes.TryGetValue(attachmentType, out var boneAttachment) && boneAttachment != null)
		{
			return boneAttachment;
		}

		if (player == null)
		{
			return null;
		}

		Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
		if (skeleton == null)
		{
			return null;
		}

		if (attachmentType == AttachmentType.Hat && skeleton.FindBone("head") >= 0)
		{
			boneAttachment = new BoneAttachment3D
			{
				Name = "Runtime_Hat",
				BoneName = "head",
				Position = new Vector3(0f, -0.0255f, 0f)
			};
			skeleton.AddChild(boneAttachment);
			AttachmentNodes[AttachmentType.Hat] = boneAttachment;
			return boneAttachment;
		}

		if (attachmentType == AttachmentType.Chest && skeleton.FindBone("chest") >= 0)
		{
			boneAttachment = new BoneAttachment3D
			{
				Name = "Runtime_Chest",
				BoneName = "chest",
				Position = new Vector3(0f, 0.243f, 0f)
			};
			skeleton.AddChild(boneAttachment);
			AttachmentNodes[AttachmentType.Chest] = boneAttachment;
			return boneAttachment;
		}

		return null;
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
			bool keepBuiltInProps = attachmentType == AttachmentType.Hat || attachmentType == AttachmentType.Cape;
			GD.Print($"Removing attachments from {attachmentType}");
			var children = boneAttachment.GetChildren();
			foreach (Node child in children)
			{
				if (keepBuiltInProps && !child.HasMeta(EquippedMetaKey))
				{
					if (child is Node3D builtInNode)
					{
						builtInNode.Visible = true;
					}
					continue;
				}

				boneAttachment.RemoveChild(child);
				child.QueueFree();
			}
		}
	}

	private void AddAttachment(AttachmentType attachmentType, EquipableItem item, EquipmentSlot slot)
	{
		PackedScene scene = item is PortableLightItem portableLight ? portableLight.HeldScene : ItemIdentity.ResolveScene(item);
		if (scene == null)
		{
			return;
		}

		WeaponGrip grip = item switch
		{
			PortableLightItem light => light.GripFor(slot),
			Weapon weapon => weapon.Grip,
			_ => attachmentType == AttachmentType.Shield ? ShieldGrip : null,
		};
		var boneAttachment = grip != null
			? GetWeaponMount(attachmentType, grip) : GetOrCreateBoneAttachment(attachmentType);
		if (boneAttachment != null)
		{
			RemoveAttachments(attachmentType);
			if (attachmentType == AttachmentType.Hat || attachmentType == AttachmentType.Cape)
			{
				foreach (Node child in boneAttachment.GetChildren())
				{
					if (!child.HasMeta(EquippedMetaKey) && child is Node3D builtInNode)
					{
						builtInNode.Visible = false;
					}
				}
			}

			GD.Print($"Adding attachment {item.Name} to {attachmentType}");
			var itemAttachment = scene.Instantiate<Node>();
			itemAttachment.SetMeta(EquippedMetaKey, true);
			ApplyRenderLayers(itemAttachment, CharacterModel.PlayerRenderLayers);
			if (grip != null)
			{
				var adapter = new Node3D { Name = "Grip", Transform = grip.Transform };
				adapter.SetMeta(EquippedMetaKey, true);
				adapter.AddChild(itemAttachment);
				boneAttachment.AddChild(adapter);
			}
			else
			{
				boneAttachment.AddChild(itemAttachment);
			}
			if (AuraPresentation != null && itemAttachment is Node3D model)
			{
				var aura = AuraPresentation.Instantiate<ItemAuraPresenter>();
				aura.Initialize(item, model, player?.WorldEffects); model.AddChild(aura);
			}
		}
	}

	private static void ApplyRenderLayers(Node node, uint renderLayers)
	{
		if (node is MeshInstance3D mesh)
		{
			mesh.Layers = renderLayers;
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyRenderLayers(child, renderLayers);
		}
	}

	private AttachmentType GetAttachmentType(EquipableItem item, EquipmentSlot slot)
	{
		AttachmentType attachmentType = AttachmentType.Item;
		if (item is PortableLightItem)
		{
			return slot == EquipmentSlot.ShieldHand ? AttachmentType.OffhandWeapon : AttachmentType.OneHandedWeapon;
		}
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
			else if ((armor.ValidSlots & ValidSlots.Head) != 0)
			{
				attachmentType = AttachmentType.Hat;
			}
			else if ((armor.ValidSlots & ValidSlots.Chest) != 0)
			{
				attachmentType = AttachmentType.Chest;
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
		var attachmentType = GetAttachmentType(item, slot);
		if (attachmentType == AttachmentType.Item)
		{
			// We don't need to show items on the character
			return;
		}

		AddAttachment(attachmentType, item, slot);
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		var attachmentType = GetAttachmentType(item, slot);
		RemoveAttachments(attachmentType);
	}
}