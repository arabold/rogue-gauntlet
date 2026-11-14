using Godot;
using Godot.Collections;

public enum AttachmentType
{
	OneHandedWeapon,
	TwoHandedWeapon,
	OffhandWeapon,
	Shield,
	Item,
	/// <summary>Worn body armor: a skinned suit on the character's own skeleton.</summary>
	Chest,
	/// <summary>A worn helmet: rigid on the head bone, or skinned like a suit.</summary>
	Helmet
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
	/// <summary>Default rig adapter for one-handed melee weapons in the off hand (ShieldHand).</summary>
	[Export] public WeaponGrip DefaultMeleeOffhandGrip { get; set; }

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
			RemoveAttachments(AttachmentType.Chest);
			RemoveAttachments(AttachmentType.Helmet);

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
	/// <summary>Skinned armor meshes worn on the character's own skeleton, by the attachment they replace.</summary>
	private readonly System.Collections.Generic.Dictionary<AttachmentType, System.Collections.Generic.List<MeshInstance3D>> _worn = new();

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

		if (!WornBones.TryGetValue(attachmentType, out string bone) || skeleton.FindBone(bone) < 0)
		{
			return null;
		}
		boneAttachment = new BoneAttachment3D { Name = $"Runtime_{attachmentType}", BoneName = bone };
		skeleton.AddChild(boneAttachment);
		AttachmentNodes[attachmentType] = boneAttachment;
		return boneAttachment;
	}

	/// <summary>The bone a rigid worn piece mounts on; its scene seats the model on it.</summary>
	private static readonly System.Collections.Generic.Dictionary<AttachmentType, string> WornBones = new()
	{
		[AttachmentType.Helmet] = CharacterModel.HelmetBone,
	};

	private void RemoveAttachments(AttachmentType attachmentType)
	{
		if (AttachmentNodes == null)
		{
			return;
		}

		if (_worn.Remove(attachmentType, out var worn))
		{
			foreach (MeshInstance3D mesh in worn)
				if (IsInstanceValid(mesh)) { mesh.GetParent()?.RemoveChild(mesh); mesh.QueueFree(); }
		}
		AttachmentNodes.TryGetValue(attachmentType, out var boneAttachment);
		if (boneAttachment != null)
		{
			GD.Print($"Removing attachments from {attachmentType}");
			foreach (Node child in boneAttachment.GetChildren())
			{
				boneAttachment.RemoveChild(child);
				child.QueueFree();
			}
		}
	}

	private void AddAttachment(AttachmentType attachmentType, EquipableItem item, EquipmentSlot slot)
	{
		PackedScene scene = item switch
		{
			PortableLightItem portableLight => portableLight.HeldScene,
			Armor armor when attachmentType is AttachmentType.Helmet or AttachmentType.Chest => armor.WornScene,
			_ => ItemIdentity.ResolveScene(item),
		};
		if (scene == null)
		{
			return;
		}
		if (item is Armor && attachmentType is AttachmentType.Helmet or AttachmentType.Chest)
		{
			if (TryWear(attachmentType, item, scene)) return;
			if (attachmentType == AttachmentType.Chest)
			{
				GD.PushError($"{item.Name}: body armor is a skinned suit, but its WornScene has no skinned mesh.");
				return;
			}
		}

		WeaponGrip grip = item switch
		{
			PortableLightItem light => light.GripFor(slot),
			Weapon weapon when slot == EquipmentSlot.ShieldHand => weapon.OffhandGrip ?? DefaultMeleeOffhandGrip ?? weapon.Grip,
			Weapon weapon => weapon.Grip,
			_ => attachmentType == AttachmentType.Shield ? ShieldGrip : null,
		};
		var boneAttachment = grip != null
			? GetWeaponMount(attachmentType, grip) : GetOrCreateBoneAttachment(attachmentType);
		if (boneAttachment != null)
		{
			RemoveAttachments(attachmentType);
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

	/// <summary>
	/// Wears skinned armor on the character's own skeleton (see <see cref="WornEquipment"/>); false for a rigid model, which
	/// mounts on its bone instead.
	/// </summary>
	private bool TryWear(AttachmentType attachmentType, EquipableItem item, PackedScene scene)
	{
		Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
		if (skeleton == null)
		{
			return false;
		}
		RemoveAttachments(attachmentType);
		var meshes = WornEquipment.Wear(skeleton, scene, CharacterModel.PlayerRenderLayers, EquippedMetaKey);
		if (meshes.Count == 0)
		{
			return false;
		}
		_worn[attachmentType] = meshes;
		GD.Print($"Wearing {item.Name} on the skeleton ({attachmentType})");
		if (AuraPresentation != null)
		{
			var aura = AuraPresentation.Instantiate<ItemAuraPresenter>();
			aura.Initialize(item, meshes[0], player?.WorldEffects); meshes[0].AddChild(aura);
		}
		return true;
	}

	/// <summary>Hides the model parts the worn helmet and body armor cover, and shows the rest.</summary>
	private void ShowCoveredParts()
	{
		Skeleton3D skeleton = CharacterModel.FindSkeleton(player);
		if (skeleton == null || player?.Inventory == null)
		{
			return;
		}
		WornEquipment.ShowModelParts(skeleton, WornEquipment.CoveredParts(
			System.Linq.Enumerable.Select(player.Inventory.EquippedItems.Values, entry => entry?.Item)));
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
			else if (slot == EquipmentSlot.ShieldHand)
			{
				attachmentType = AttachmentType.OffhandWeapon;
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
				attachmentType = AttachmentType.Helmet;
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
		if (attachmentType is AttachmentType.Helmet or AttachmentType.Chest)
		{
			ShowCoveredParts();
		}
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		var attachmentType = GetAttachmentType(item, slot);
		RemoveAttachments(attachmentType);
		if (attachmentType is AttachmentType.Helmet or AttachmentType.Chest)
		{
			ShowCoveredParts();
		}
	}
}