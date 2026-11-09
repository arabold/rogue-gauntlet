using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class GameNarrator : Node
{
	private readonly Dictionary<string, (string DisguisedName, string TrueName)> _pendingReveals = new();

	public override void _EnterTree()
	{
		base._EnterTree();
		SetProcess(false);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus =>
			{
				bus.ItemConsumed += OnItemConsumed;
				bus.ItemIdentityRevealed += OnItemIdentityRevealed;
				bus.ItemEquipped += OnItemEquipped;
				bus.ItemUnequipped += OnItemUnequipped;
				bus.ItemDropped += OnItemDropped;
				bus.ItemPickedUp += OnItemPickedUp;
				bus.XpRewarded += OnXpRewarded;
				bus.PlayerDeath += OnPlayerDeath;
			},
			bus =>
			{
				bus.ItemConsumed -= OnItemConsumed;
				bus.ItemIdentityRevealed -= OnItemIdentityRevealed;
				bus.ItemEquipped -= OnItemEquipped;
				bus.ItemUnequipped -= OnItemUnequipped;
				bus.ItemDropped -= OnItemDropped;
				bus.ItemPickedUp -= OnItemPickedUp;
				bus.XpRewarded -= OnXpRewarded;
				bus.PlayerDeath -= OnPlayerDeath;
			});
	}

	public void FlushPendingReveals()
	{
		if (_pendingReveals.Count == 0)
		{
			return;
		}

		foreach (var (_, (disguisedName, trueName)) in _pendingReveals)
		{
			AnnounceReveal(disguisedName, trueName);
		}

		_pendingReveals.Clear();
	}

	private void OnItemIdentityRevealed(string typeId, string disguisedName, string trueName)
	{
		if (string.IsNullOrEmpty(typeId))
		{
			return;
		}

		// Don't announce if the disguise was already identical to the true name
		if (string.Equals(disguisedName, trueName, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (_pendingReveals.Count == 0 && IsInsideTree())
		{
			Callable.From(FlushPendingReveals).CallDeferred();
		}

		_pendingReveals[typeId] = (disguisedName, trueName);
	}

	private void OnItemConsumed(Player player, ConsumableItem item)
	{
		if (item == null)
		{
			return;
		}

		string verb = item switch
		{
			Scroll => "Read",
			FoodItem => "Ate",
			_ when item.TypeId?.StartsWith("potion.", StringComparison.OrdinalIgnoreCase) == true
				|| item.AnimationId == "drink_potion" => "Drank",
			_ => "Used"
		};

		// If this consumption just revealed the item's identity, announce the disguised
		// action first, followed immediately by the identification reveal.
		if (item.TypeId != null && _pendingReveals.Remove(item.TypeId, out var reveal))
		{
			GameLog.Player($"{verb} [b]{reveal.DisguisedName}[/b]", LogSeverity.Info);
			AnnounceReveal(reveal.DisguisedName, reveal.TrueName);
			return;
		}

		string name = ItemIdentity.ResolveDisplayName(item);
		GameLog.Player($"{verb} [b]{name}[/b]", LogSeverity.Info);
	}

	private void AnnounceReveal(string disguisedName, string trueName)
	{
		GameLog.Player($"[b]{disguisedName}[/b] is [b]{trueName}[/b]", LogSeverity.Discovery);
	}

	private void OnItemEquipped(Player player, EquipableItem item)
	{
		if (item == null)
		{
			return;
		}

		string verb = item switch
		{
			_ when item.ValidSlots.HasFlag(ValidSlots.WeaponHand) => "Wielded",
			_ when item.ValidSlots.HasFlag(ValidSlots.LeftRing)
				|| item.ValidSlots.HasFlag(ValidSlots.RightRing)
				|| item.ValidSlots.HasFlag(ValidSlots.Neck) => "Put on",
			_ => "Equipped"
		};

		string name = ItemIdentity.ResolveDisplayName(item);
		GameLog.Player($"{verb} [b]{name}[/b]", LogSeverity.Equipment);
	}

	private void OnItemUnequipped(Player player, EquipableItem item)
	{
		if (item == null)
		{
			return;
		}

		string name = ItemIdentity.ResolveDisplayName(item);
		GameLog.Player($"Unequipped [b]{name}[/b]", LogSeverity.Equipment);
	}

	private void OnItemDropped(Player player, Item item, int quantity)
	{
		if (item == null || quantity <= 0)
		{
			return;
		}

		string name = ItemIdentity.ResolveDisplayName(item);
		string text = quantity > 1 ? $"Dropped [b]{quantity} {name}[/b]" : $"Dropped [b]{name}[/b]";
		GameLog.Player(text, LogSeverity.ItemPickup);
	}

	private void OnItemPickedUp(Player player, Item item, int quantity)
	{
		if (item == null || quantity <= 0)
		{
			return;
		}

		string name = ItemIdentity.ResolveDisplayName(item);
		if (item is Gold || string.Equals(name, "Gold", StringComparison.OrdinalIgnoreCase))
		{
			GameLog.Player($"Picked up [b]{quantity} Gold[/b]", LogSeverity.Gold);
			return;
		}

		string text = quantity > 1 ? $"Picked up [b]{quantity} {name}[/b]" : $"Picked up [b]{name}[/b]";
		GameLog.Player(text, LogSeverity.ItemPickup);
	}

	private void OnXpRewarded(int xp)
	{
		if (xp <= 0)
		{
			return;
		}

		GameLog.Player($"Gained [b]{xp} XP[/b]", LogSeverity.Experience);
	}

	private void OnPlayerDeath(Player player)
	{
		GameLog.Player("[b]You died[/b]", LogSeverity.Error);
	}
}