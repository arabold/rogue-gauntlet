using Godot;
using System;
using System.Linq;

[GlobalClass]
public partial class HandActionPanel : PanelContainer
{
	private EquipmentSlot _hand = EquipmentSlot.WeaponHand;
	[Export]
	public EquipmentSlot Hand
	{
		get => _hand;
		set
		{
			_hand = value;
			if (!IsNodeReady()) return;
			ConfigureHand();
			Refresh();
		}
	}
	[Export] public Preview Preview { get; set; }
	[Export] public SubViewportContainer ViewportContainer { get; set; }
	[Export] public KeyBinding FirstActionKey { get; set; }
	[Export] public KeyBinding SecondActionKey { get; set; }
	[Export] public ProgressBar FirstActionCooldownBar { get; set; }
	[Export] public ProgressBar SecondActionCooldownBar { get; set; }
	[Export] public Label ManaCostLabel { get; set; }
	[Export] public Color ManaCostColor { get; set; } = new("8fb8ff");

	private Player _player;
	private Action _unsubscribePlayer = () => { };
	private bool _secondaryOnCooldown;
	private bool _offhandOnCooldown;

	public override void _Ready()
	{
		Preview ??= GetNodeOrNull<Preview>("%Preview");
		ViewportContainer ??= GetNodeOrNull<SubViewportContainer>("%SubViewportContainer") ?? GetNodeOrNull<SubViewportContainer>("SubViewportContainer");
		FirstActionKey ??= GetNodeOrNull<KeyBinding>("%FirstActionKey");
		SecondActionKey ??= GetNodeOrNull<KeyBinding>("%SecondActionKey");
		FirstActionCooldownBar ??= GetNodeOrNull<ProgressBar>("%FirstActionCooldownBar");
		SecondActionCooldownBar ??= GetNodeOrNull<ProgressBar>("%SecondActionCooldownBar");
		ManaCostLabel ??= GetNodeOrNull<Label>("%ManaCostLabel");

		ConfigureHand();

		this.SubscribeUntilExit(
			SignalBus.Instance,
			sb => sb.PlayerSpawned += OnPlayerSpawned,
			sb => sb.PlayerSpawned -= OnPlayerSpawned);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			sb => sb.CooldownUpdated += OnCooldownUpdated,
			sb => sb.CooldownUpdated -= OnCooldownUpdated);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			sb => sb.PlayerActionSlotChanged += OnPlayerActionSlotChanged,
			sb => sb.PlayerActionSlotChanged -= OnPlayerActionSlotChanged);

		var player = GetTree()?.GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			BindPlayer(player);
		}
		else
		{
			Refresh();
		}
	}

	private void ConfigureHand()
	{
		if (FirstActionKey != null)
		{
			FirstActionKey.ActionBinding = Hand == EquipmentSlot.WeaponHand ? "primary_action" : "offhand_action";
		}
		if (SecondActionKey != null)
		{
			SecondActionKey.ActionBinding = Hand == EquipmentSlot.WeaponHand ? "secondary_action" : "";
			SecondActionKey.Visible = Hand == EquipmentSlot.WeaponHand;
		}
		if (SecondActionCooldownBar != null)
		{
			SecondActionCooldownBar.Visible = Hand == EquipmentSlot.WeaponHand;
		}
	}

	private void OnPlayerSpawned(Player player) => BindPlayer(player);

	public void BindPlayer(Player player)
	{
		_unsubscribePlayer();
		_player = player;
		if (player?.Inventory != null)
		{
			var inv = player.Inventory;
			var mana = player.ManaComponent;
			_unsubscribePlayer = () =>
			{
				inv.ItemEquipped -= OnEquipmentChanged;
				inv.ItemUnequipped -= OnEquipmentChanged;
				inv.EquippedItemChanged -= OnEquippedItemChanged;
				if (mana != null) mana.ManaChanged -= OnManaChanged;
			};
			this.SubscribeUntilExit(inv, i => i.ItemEquipped += OnEquipmentChanged, i => i.ItemEquipped -= OnEquipmentChanged);
			this.SubscribeUntilExit(inv, i => i.ItemUnequipped += OnEquipmentChanged, i => i.ItemUnequipped -= OnEquipmentChanged);
			this.SubscribeUntilExit(inv, i => i.EquippedItemChanged += OnEquippedItemChanged, i => i.EquippedItemChanged -= OnEquippedItemChanged);
			if (mana != null)
			{
				this.SubscribeUntilExit(mana, m => m.ManaChanged += OnManaChanged, m => m.ManaChanged -= OnManaChanged);
			}
		}
		Refresh();
	}

	private void OnEquipmentChanged(EquipableItem item, EquipmentSlot slot) => Refresh();
	private void OnEquippedItemChanged(EquipableItem item) => Refresh();
	private void OnManaChanged(float current, float max) => Refresh();

	private void OnPlayerActionSlotChanged(int slotIndex, ActionSlot slot)
	{
		if (Hand == EquipmentSlot.WeaponHand && (slotIndex == ActionManager.PrimarySlotIndex || slotIndex == ActionManager.SecondarySlotIndex))
		{
			Refresh();
		}
		else if (Hand == EquipmentSlot.ShieldHand && slotIndex == ActionManager.OffhandSlotIndex)
		{
			Refresh();
		}
	}

	private void OnCooldownUpdated(int slotIndex, float remainingTime, float totalTime)
	{
		float pct = totalTime > 0 ? 100f * remainingTime / totalTime : 0f;
		if (Hand == EquipmentSlot.WeaponHand)
		{
			if (slotIndex == ActionManager.PrimarySlotIndex && FirstActionCooldownBar != null)
			{
				FirstActionCooldownBar.Value = pct;
			}
			else if (slotIndex == ActionManager.SecondarySlotIndex)
			{
				if (SecondActionCooldownBar != null) SecondActionCooldownBar.Value = pct;
				bool wasCooldown = _secondaryOnCooldown;
				_secondaryOnCooldown = remainingTime > 0;
				if (wasCooldown != _secondaryOnCooldown) Refresh();
			}
		}
		else if (Hand == EquipmentSlot.ShieldHand)
		{
			if (slotIndex == ActionManager.OffhandSlotIndex)
			{
				if (FirstActionCooldownBar != null) FirstActionCooldownBar.Value = pct;
				bool wasCooldown = _offhandOnCooldown;
				_offhandOnCooldown = remainingTime > 0;
				if (wasCooldown != _offhandOnCooldown) Refresh();
			}
		}
	}

	public void Refresh()
	{
		if (!IsNodeReady()) return;

		Item displayedItem = null;
		if (_player?.Inventory != null)
		{
			if (Hand == EquipmentSlot.WeaponHand)
			{
				displayedItem = _player.Inventory.EquippedWeapon;
			}
			else
			{
				Weapon weapon = _player.Inventory.EquippedWeapon;
				if (weapon != null && weapon.IsTwoHanded)
				{
					// Two-handed equipment projected into off hand
					displayedItem = weapon;
				}
				else
				{
					displayedItem = _player.Inventory.EquippedOffhandWeapon
						?? (_player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.ShieldHand, out var s) ? s?.Item : null);
				}
			}
		}

		if (Preview != null)
		{
			Preview.Scene = displayedItem != null ? ItemIdentity.ResolveScene(displayedItem) : null;
			Preview.Diagonal = displayedItem is Weapon or Armor;
		}

		if (Hand == EquipmentSlot.WeaponHand)
		{
			RefreshMainHand();
		}
		else
		{
			RefreshOffHand(displayedItem);
		}
	}

	private void RefreshMainHand()
	{
		if (ViewportContainer != null)
		{
			ViewportContainer.Modulate = (_player != null && _player.IsDead) ? new Color(0.4f, 0.4f, 0.4f, 0.6f) : Colors.White;
		}

		if (FirstActionKey != null)
		{
			FirstActionKey.IsDisabled = _player == null || _player.IsDead;
		}

		ActionSlot secondarySlot = _player?.ActionManager?.SecondaryTechniqueSlot;
		IPlayerAction secondaryAction = secondarySlot?.AssignedAction;
		float manaCost = (secondaryAction as SpellCastAction)?.ManaCost ?? 0;
		bool canAfford = _player?.ManaComponent?.CanSpend(manaCost) ?? (manaCost == 0);

		if (SecondActionKey != null)
		{
			SecondActionKey.IsDisabled = secondaryAction == null || !canAfford || _secondaryOnCooldown || (_player != null && _player.IsDead);
		}

		if (ManaCostLabel != null)
		{
			ManaCostLabel.Text = manaCost > 0 ? manaCost.ToString("0") : "";
			ManaCostLabel.SelfModulate = canAfford ? ManaCostColor : new Color(0.4f, 0.4f, 0.4f, 0.6f);
		}
	}

	private void RefreshOffHand(Item displayedItem)
	{
		Weapon twoHanded = _player?.Inventory?.EquippedWeapon is { IsTwoHanded: true } w ? w : null;
		bool isStaff = twoHanded is MagicStaff;
		bool isMeleeOffhand = displayedItem is Weapon { IsTwoHanded: false } && !ReferenceEquals(displayedItem, twoHanded);
		bool hasGuard = _player?.GuardComponent?.HasGuard == true;

		ActionSlot offhandSlot = _player?.ActionManager?.OffhandActionSlot;
		IPlayerAction offhandAction = offhandSlot?.AssignedAction;
		float manaCost = (offhandAction as SpellCastAction)?.ManaCost ?? 0;
		bool canAfford = _player?.ManaComponent?.CanSpend(manaCost) ?? (manaCost == 0);

		bool actionAvailable = false;
		bool panelDimmed = false;

		if (isStaff)
		{
			// Staff has no L action
			actionAvailable = false;
			panelDimmed = true;
		}
		else if (isMeleeOffhand)
		{
			// Paired melee off-hand dagger has no L action; dims preview and L
			actionAvailable = false;
			panelDimmed = true;
		}
		else if (offhandAction != null)
		{
			// Off-hand spell action (e.g. wand in Slice 2)
			actionAvailable = canAfford && !_offhandOnCooldown && (_player == null || !_player.IsDead);
			panelDimmed = !canAfford || _offhandOnCooldown;
		}
		else if (hasGuard)
		{
			// Guard action available (shield, weapon guard, or fallback innate guard)
			actionAvailable = _player == null || !_player.IsDead;
			panelDimmed = false;
		}
		else
		{
			actionAvailable = false;
			panelDimmed = displayedItem != null;
		}

		if (ViewportContainer != null)
		{
			ViewportContainer.Modulate = panelDimmed ? new Color(0.4f, 0.4f, 0.4f, 0.6f) : Colors.White;
		}

		if (FirstActionKey != null)
		{
			FirstActionKey.IsDisabled = !actionAvailable;
		}

		if (ManaCostLabel != null)
		{
			ManaCostLabel.Text = manaCost > 0 ? manaCost.ToString("0") : "";
			ManaCostLabel.SelfModulate = canAfford ? ManaCostColor : new Color(0.4f, 0.4f, 0.4f, 0.6f);
		}
	}
}