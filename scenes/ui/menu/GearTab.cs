using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The run menu's Gear tab: what the hero wears on the left, the hero wearing it in the middle, and every wearable
/// thing in the pack on the right. Standing on a worn slot dims the gear that does not fit it. Choosing an empty slot
/// moves to the first thing that does, and gear equipped from there goes into that slot; otherwise gear goes where it
/// belongs, a hand item to the main hand unless the off hand is asked for. The hero is a <see cref="HeroShowcase"/>
/// on the run's inventory, so it changes as gear does.
/// </summary>
public partial class GearTab : RunMenuTab
{
	[ExportGroup("Worn")]
	[Export] public ItemSlotPanel HelmetSlot { get; set; }
	[Export] public ItemSlotPanel MainHandSlot { get; set; }
	[Export] public ItemSlotPanel ArmorSlot { get; set; }
	[Export] public ItemSlotPanel OffHandSlot { get; set; }
	[Export] public ItemSlotPanel LeftRingSlot { get; set; }
	[Export] public ItemSlotPanel AmuletSlot { get; set; }
	[Export] public ItemSlotPanel RightRingSlot { get; set; }

	[ExportGroup("Hero")]
	[Export] public HeroShowcase Hero { get; set; }
	/// <summary>The hero shown when the run has no class, as when the gameplay scene runs on its own.</summary>
	[Export] public CharacterClass FallbackClass { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public ItemDetailsView Details { get; set; }
	/// <summary>Tint of an empty slot's name.</summary>
	[Export] public Color EmptyTint { get; set; } = new(1f, 1f, 1f, 0.45f);

	[ExportGroup("Stats")]
	[Export] public AttributeReadout Strength { get; set; }
	[Export] public AttributeReadout Dexterity { get; set; }
	[Export] public AttributeReadout Vitality { get; set; }
	[Export] public AttributeReadout Intelligence { get; set; }
	[Export] public Label DamageLabel { get; set; }
	[Export] public Label ArmorLabel { get; set; }
	[Export] public Label CritLabel { get; set; }

	[ExportGroup("Pack")]
	[Export] public Label GearHeading { get; set; }
	[Export] public GridContainer GearGrid { get; set; }
	/// <summary>Shown instead of the list while the pack holds nothing to wear.</summary>
	[Export] public Control EmptyHint { get; set; }

	private static readonly Dictionary<EquipmentSlot, string> SlotNames = new()
	{
		[EquipmentSlot.Head] = "Helmet",
		[EquipmentSlot.Chest] = "Armor",
		[EquipmentSlot.WeaponHand] = "Main Hand",
		[EquipmentSlot.ShieldHand] = "Off Hand",
		[EquipmentSlot.LeftRing] = "Ring",
		[EquipmentSlot.RightRing] = "Ring",
		[EquipmentSlot.Neck] = "Amulet",
	};

	private readonly Dictionary<ItemSlotPanel, EquipmentSlot> _worn = [];
	/// <summary>
	/// The worn slot chosen to be filled, so gear equipped from the list goes into it. Standing on a slot only shows
	/// what fits: the pointer crosses slots on its way to the list without meaning any of them.
	/// </summary>
	private EquipmentSlot? _filling;

	/// <summary>The worn slot whose fit the list shows by dimming the rest: the one stood on, else the one being filled.</summary>
	private EquipmentSlot? Fitting => Selected != null && _worn.TryGetValue(Selected, out EquipmentSlot slot) ? slot : _filling;

	public override void _Ready()
	{
		_worn[HelmetSlot] = EquipmentSlot.Head;
		_worn[MainHandSlot] = EquipmentSlot.WeaponHand;
		_worn[ArmorSlot] = EquipmentSlot.Chest;
		_worn[OffHandSlot] = EquipmentSlot.ShieldHand;
		_worn[LeftRingSlot] = EquipmentSlot.LeftRing;
		_worn[AmuletSlot] = EquipmentSlot.Neck;
		_worn[RightRingSlot] = EquipmentSlot.RightRing;
		foreach (ItemSlotPanel panel in _worn.Keys)
		{
			Connect(panel);
		}
	}

	public override void Open(Player player)
	{
		base.Open(player);
		Watch(player.Inventory, Refresh);
		// Worn gear and buffs register their modifiers with the stats, so this covers every change of gear too.
		Watch(player.Stats, RefreshStats);
		Hero.Show(GameSession.Instance?.ActiveCharacterClass ?? FallbackClass, player.Inventory);
		_filling = null;
		Refresh();
		RefreshStats();
	}

	public override void Close()
	{
		Hero.Clear();
		base.Close();
	}

	public override void FocusDefault() => MainHandSlot.FocusSlot();

	public override IEnumerable<MenuPrompt> Prompts
	{
		get
		{
			if (Player == null || Selected == null)
			{
				yield break;
			}
			if (Selected.Slot is not { Item: EquipableItem item } entry)
			{
				if (FirstFitting() != null)
				{
					yield return new MenuPrompt("Choose", Accept);
				}
				yield break;
			}

			foreach (MenuPrompt prompt in WearPrompts(entry, item, _filling != null))
			{
				yield return prompt;
			}
			if (CanDrop(entry))
			{
				yield return new MenuPrompt("Drop", Drop);
			}
		}
	}

	public override void Invoke(StringName action)
	{
		if (Player == null || Selected == null)
		{
			return;
		}
		if (Selected.Slot is not { } entry)
		{
			// An empty worn slot: on to the first thing in the pack that fits it, which then goes into this slot.
			if (action == Accept && FirstFitting() is { } first)
			{
				_filling = Fitting;
				first.FocusSlot();
			}
			return;
		}

		if (action == Accept)
		{
			ToggleEquip(entry, _filling);
		}
		else
		{
			InvokeCommon(action, entry);
		}
	}

	/// <summary>The first gear in the list that fits the slot stood on or being filled and is not already worn.</summary>
	private ItemSlotPanel FirstFitting() => Fitting is not { } slot ? null : GearGrid.GetChildren().OfType<ItemSlotPanel>()
		.FirstOrDefault(panel => panel.Slot?.Item is EquipableItem item && Inventory.Fits(item, slot) && !panel.IsEquipped);

	protected override void Select(ItemSlotPanel panel)
	{
		base.Select(panel);
		// Filling a slot lasts only while the selection stays on gear in the list that fits it.
		if (_filling is { } slot && (_worn.ContainsKey(panel) || panel.Slot?.Item is not EquipableItem item || !Inventory.Fits(item, slot)))
		{
			_filling = null;
		}
		ApplyFitting();
		Describe();
		EmitSignalPromptsChanged();
	}

	private void Refresh()
	{
		if (Player == null)
		{
			return;
		}

		Inventory inventory = Player.Inventory;
		foreach ((ItemSlotPanel panel, EquipmentSlot slot) in _worn)
		{
			InventoryItemSlot entry = inventory.EquippedItems[slot];
			// A two-handed weapon fills both hands: the off hand shows it too, faded.
			bool isSecondHand = entry == null && slot == EquipmentSlot.ShieldHand && inventory.EquippedWeapon is { IsTwoHanded: true };
			if (isSecondHand)
			{
				entry = inventory.EquippedItems[EquipmentSlot.WeaponHand];
			}
			panel.SetItem(entry, entry != null);
			panel.Dimmed = isSecondHand;
		}

		List<InventoryItemSlot> gear = [.. inventory.Items.Where(entry => entry.Item is EquipableItem)];
		bool lostSelection = SyncSlots(GearGrid, gear.Count);
		for (int i = 0; i < gear.Count; i++)
		{
			GearGrid.GetChild<ItemSlotPanel>(i).SetItem(gear[i], inventory.IsEquipped(gear[i]));
		}
		EmptyHint.Visible = gear.Count == 0;

		ApplyFitting();
		if (lostSelection)
		{
			(gear.Count > 0 ? GearGrid.GetChild<ItemSlotPanel>(gear.Count - 1) : MainHandSlot).FocusSlot();
		}
		Describe();
		EmitSignalPromptsChanged();
	}

	private void ApplyFitting()
	{
		EquipmentSlot? fitting = Fitting;
		GearHeading.Text = fitting is { } named ? SlotNames[named] : "Gear";
		foreach (ItemSlotPanel panel in GearGrid.GetChildren().OfType<ItemSlotPanel>())
		{
			panel.Dimmed = fitting is { } slot && panel.Slot?.Item is EquipableItem item && !Inventory.Fits(item, slot);
		}
	}

	/// <summary>Names the selected item, or the empty slot stood on, with the item's lines beneath.</summary>
	private void Describe()
	{
		Item item = Selected?.Slot?.Item;
		ShowName(NameLabel, item);
		if (item == null && Selected != null && _worn.TryGetValue(Selected, out EquipmentSlot slot))
		{
			NameLabel.Text = SlotNames[slot];
			NameLabel.SelfModulate = EmptyTint;
		}
		Details.SetItem(item, Player);
	}

	private void RefreshStats()
	{
		if (Player == null)
		{
			return;
		}

		PlayerStats stats = Player.Stats;
		Strength.Display(stats.BaseStrength, stats.Strength);
		Dexterity.Display(stats.BaseDexterity, stats.Dexterity);
		Vitality.Display(stats.BaseVitality, stats.Vitality);
		Intelligence.Display(stats.BaseIntelligence, stats.Intelligence);

		AttackStats attack = stats.ResolveAttack(Player.Inventory.EquippedWeapon, Player.Inventory.EquippedOffhandWeapon);
		DamageLabel.Text = $"{attack.MinDamage:F0}–{attack.MaxDamage:F0}";
		ArmorLabel.Text = $"{stats.Armor:F0}";
		CritLabel.Text = $"{attack.CritChance * 100:F0}%";
	}
}