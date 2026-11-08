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
		_unsubscribePlayer = () => { };
		_player = player;
		if (player?.Inventory != null)
		{
			var inv = player.Inventory;
			var mana = player.ManaComponent;
			var u1 = this.SubscribeUntilExit(inv, i => i.ItemEquipped += OnEquipmentChanged, i => i.ItemEquipped -= OnEquipmentChanged);
			var u2 = this.SubscribeUntilExit(inv, i => i.ItemUnequipped += OnEquipmentChanged, i => i.ItemUnequipped -= OnEquipmentChanged);
			var u3 = this.SubscribeUntilExit(inv, i => i.EquippedItemChanged += OnEquippedItemChanged, i => i.EquippedItemChanged -= OnEquippedItemChanged);
			Action u4 = mana != null
				? this.SubscribeUntilExit(mana, m => m.ManaChanged += OnManaChanged, m => m.ManaChanged -= OnManaChanged)
				: () => { };
			_unsubscribePlayer = () =>
			{
				u1();
				u2();
				u3();
				u4();
			};
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
		if (Hand == EquipmentSlot.WeaponHand)
		{
			if (slotIndex != ActionManager.PrimarySlotIndex && slotIndex != ActionManager.SecondarySlotIndex) return;
			float pct = totalTime > 0 ? 100f * remainingTime / totalTime : 0f;
			if (slotIndex == ActionManager.PrimarySlotIndex)
			{
				if (FirstActionCooldownBar != null) FirstActionCooldownBar.Value = pct;
			}
			else
			{
				if (SecondActionCooldownBar != null) SecondActionCooldownBar.Value = pct;
				bool wasCooldown = _secondaryOnCooldown;
				_secondaryOnCooldown = remainingTime > 0;
				if (wasCooldown != _secondaryOnCooldown) Refresh();
			}
		}
		else if (Hand == EquipmentSlot.ShieldHand)
		{
			if (slotIndex != ActionManager.OffhandSlotIndex) return;
			float pct = totalTime > 0 ? 100f * remainingTime / totalTime : 0f;
			if (FirstActionCooldownBar != null) FirstActionCooldownBar.Value = pct;
			bool wasCooldown = _offhandOnCooldown;
			_offhandOnCooldown = remainingTime > 0;
			if (wasCooldown != _offhandOnCooldown) Refresh();
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
				else if (_player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.ShieldHand, out var shieldSlot))
				{
					displayedItem = shieldSlot?.Item;
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
		bool hasGuard = _player?.GuardComponent?.HasGuard == true;

		ActionSlot offhandSlot = _player?.ActionManager?.OffhandActionSlot;
		IPlayerAction offhandAction = offhandSlot?.AssignedAction;
		float manaCost = (offhandAction as SpellCastAction)?.ManaCost ?? 0;
		bool canAfford = _player?.ManaComponent?.CanSpend(manaCost) ?? (manaCost == 0);

		bool actionAvailable = false;
		bool panelDimmed = false;

		if (offhandAction != null)
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