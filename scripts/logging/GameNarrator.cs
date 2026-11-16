using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class GameNarrator : Node
{
	private readonly Dictionary<string, (string DisguisedName, string TrueName)> _pendingReveals = new();
	private int _poisonWorsenCount;

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
				bus.PlayerLeveledUp += OnPlayerLeveledUp;
				bus.PlayerDeath += OnPlayerDeath;
				bus.LevelLoaded += OnLevelLoaded;
				bus.PlayerStatusMessage += OnPlayerStatusMessage;
				bus.PlayerBuffApplied += OnPlayerBuffApplied;
				bus.PlayerBuffRefreshed += OnPlayerBuffRefreshed;
				bus.PlayerBuffEnded += OnPlayerBuffEnded;
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
				bus.PlayerLeveledUp -= OnPlayerLeveledUp;
				bus.PlayerDeath -= OnPlayerDeath;
				bus.LevelLoaded -= OnLevelLoaded;
				bus.PlayerStatusMessage -= OnPlayerStatusMessage;
				bus.PlayerBuffApplied -= OnPlayerBuffApplied;
				bus.PlayerBuffRefreshed -= OnPlayerBuffRefreshed;
				bus.PlayerBuffEnded -= OnPlayerBuffEnded;
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

	private void OnItemIdentityRevealed(string typeId, string disguisedName, string trueName, string source)
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
		string finalName;
		if (item.TypeId != null && _pendingReveals.Remove(item.TypeId, out var reveal))
		{
			GameLog.Player($"{verb} [b]{reveal.DisguisedName}[/b]", LogSeverity.Info);
			AnnounceReveal(reveal.DisguisedName, reveal.TrueName);
			finalName = reveal.TrueName;
		}
		else
		{
			finalName = ItemIdentity.ResolveDisplayName(item);
			GameLog.Player($"{verb} [b]{finalName}[/b]", LogSeverity.Info);
		}

		if (item is FoodItem { IsPoisoned: true })
		{
			GameLog.Player($"Ulch - that [b]{finalName}[/b] was tainted!", LogSeverity.Warning);
		}
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
		if (item.IsCursed)
		{
			GameLog.Player(DescribeCurseEffect(item), LogSeverity.Warning);
		}
	}

	private static string DescribeCurseEffect(EquipableItem item)
	{
		StatModifier primary = item.CurseModifiers.FirstOrDefault();
		return primary?.Stat switch
		{
			StatType.Strength => "A dark curse saps your strength — you suddenly feel weak and exhausted.",
			StatType.Dexterity => "A dark curse stiffens your hands — you suddenly feel clumsy and awkward.",
			StatType.Vitality or StatType.MaxHealth => "A dark curse drains your vitality — you suddenly feel frail and hollow.",
			StatType.Intelligence or StatType.MaxMana => "A dark curse clouds your thoughts — your mind feels dull and drained.",
			StatType.Speed or StatType.DodgeDistance => "A dark curse drags at your limbs — you suddenly feel sluggish and exhausted.",
			StatType.MinDamage or StatType.MaxDamage or StatType.CritChance => "A dark curse numbs your grip — your strikes feel hesitant and weak.",
			StatType.Armor => "A dark curse weakens your defenses — you feel dangerously exposed.",
			_ => "A malevolent chill grips you — you suddenly feel weak and exhausted.",
		};
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

	private void OnPlayerLeveledUp(int level) => GameLog.Player($"Reached [b]level {level}[/b]", LogSeverity.Experience);

	private void OnLevelLoaded(Level level)
	{
		_poisonWorsenCount = 0;
		if (level != null && GameSession.Instance?.IsRestoringSavedFloor != true)
		{
			GameLog.Player($"You reached [b]floor level {level.Depth}[/b]", LogSeverity.Info);
		}
	}

	private void OnPlayerStatusMessage(string message, int severity)
	{
		GameLog.Player(message, (LogSeverity)severity);
	}

	private void OnPlayerBuffRefreshed(Buff buff)
	{
		if (buff != null && IsPoisonBuff(buff))
		{
			AnnouncePoison(buff, worsening: true);
		}
	}

	private void OnPlayerBuffApplied(Buff buff, bool hadCondition)
	{
		if (buff == null)
		{
			return;
		}

		if (IsPoisonBuff(buff))
		{
			AnnouncePoison(buff, worsening: hadCondition);
			return;
		}

		if (buff.Condition != null && !hadCondition)
		{
			AnnounceConditionStarted(buff.Condition);
			return;
		}

		if (buff is TimeScaleBuff { TimeScale: < 1f })
		{
			GameLog.Player("Your limbs feel heavy and sluggish.", LogSeverity.Warning);
			return;
		}

		if (buff is HealingBuff && (buff.CuredConditions.Count > 0 || buff.Name?.Contains("Healing", StringComparison.OrdinalIgnoreCase) == true))
		{
			GameLog.Player("A warm glow suffuses you. You feel better.", LogSeverity.Info);
			return;
		}

		if (buff is StatModifierBuff { Duration: > 0f, Modifiers.Length: > 0 } statBuff)
		{
			StatModifier primary = statBuff.Modifiers[0];
			if (primary.Value < 0f)
			{
				GameLog.Player("Your muscles go limp — you suddenly feel weak and exhausted.", LogSeverity.Warning);
				return;
			}

			string message = primary.Stat switch
			{
				StatType.Strength => "Your muscles surge with power!",
				StatType.Dexterity => "You feel nimble and light on your feet.",
				StatType.Vitality or StatType.MaxHealth => "Vigorous energy courses through your body.",
				StatType.Intelligence or StatType.MaxMana => "Your mind feels sharp and clear.",
				StatType.Speed => "You feel a sudden burst of speed!",
				StatType.Armor => "A protective ward settles over you.",
				StatType.MinDamage or StatType.MaxDamage or StatType.CritChance => "Fierce battle fury fills your veins!",
				_ => "",
			};
			if (message.Length > 0)
			{
				GameLog.Player(message, LogSeverity.Info);
			}
		}
	}

	private void OnPlayerBuffEnded(Buff buff)
	{
		if (buff == null)
		{
			return;
		}

		if (buff.Condition is { } condition)
		{
			if (IsPoisonCondition(condition))
			{
				_poisonWorsenCount = 0;
			}

			string name = condition.ResourceName ?? "";
			string message = name switch
			{
				"Poisoned" => "You are no longer ill.",
				"Blinded" => "Your vision clears.",
				"Confused" => "Your head clears.",
				"Burning" => "The flames die out.",
				"Frozen" => "The ice shatters and you can move again.",
				"Invisible" => "You fade back into view.",
				"Iron Skin" or "Stone Skin" => "Your skin returns to normal.",
				_ when name.StartsWith("Slowed", StringComparison.OrdinalIgnoreCase) => "You feel yourself speed up.",
				_ => "",
			};
			if (message.Length > 0)
			{
				GameLog.Player(message, LogSeverity.Info);
			}
			return;
		}

		if (buff is TimeScaleBuff { TimeScale: < 1f })
		{
			GameLog.Player("You feel yourself speed up.", LogSeverity.Info);
			return;
		}

		if (buff is StatModifierBuff { Duration: > 0f, Modifiers.Length: > 0 } statBuff)
		{
			StatModifier primary = statBuff.Modifiers[0];
			string message = primary.Value < 0f
				? "Your strength returns."
				: primary.Stat switch
				{
					StatType.Strength => "Your surge of strength fades.",
					StatType.Dexterity => "Your heightened agility fades.",
					StatType.Vitality or StatType.MaxHealth => "Your extra vigor fades.",
					StatType.Intelligence or StatType.MaxMana => "Your mental clarity subsides.",
					StatType.Speed => "Your burst of speed wears off.",
					StatType.Armor => "Your protective ward dissipates.",
					StatType.MinDamage or StatType.MaxDamage or StatType.CritChance => "Your battle fury subsides.",
					_ => "",
				};
			if (message.Length > 0)
			{
				GameLog.Player(message, LogSeverity.Info);
			}
		}
	}

	private void AnnouncePoison(Buff buff, bool worsening)
	{
		if (worsening)
		{
			_poisonWorsenCount++;
			GameLog.Player(_poisonWorsenCount <= 1 ? "You feel worse." : "You feel much worse.", LogSeverity.Warning);
			return;
		}

		_poisonWorsenCount = 0;
		string message = buff.Name?.Contains("Venom", StringComparison.OrdinalIgnoreCase) == true
			? "You feel sick as venom courses through your veins."
			: "You feel very sick and nauseous.";
		GameLog.Player(message, LogSeverity.Warning);
	}

	private static void AnnounceConditionStarted(StatusCondition condition)
	{
		string conditionName = condition.ResourceName ?? "";
		string message = conditionName switch
		{
			"Blinded" => "Everything goes dark! You can barely see.",
			"Confused" => "The world spins around you! You feel dizzy and confused.",
			"Burning" => "You catch fire!",
			"Frozen" => "Biting frost encases your limbs — you are frozen stiff!",
			"Invisible" => "Your body fades from sight.",
			"Iron Skin" => "Your skin hardens like iron.",
			"Stone Skin" => "Your skin turns as hard as stone.",
			_ when conditionName.StartsWith("Slowed", StringComparison.OrdinalIgnoreCase) => "Your limbs feel heavy and sluggish.",
			_ => "",
		};
		if (message.Length > 0)
		{
			LogSeverity severity = conditionName is "Invisible" or "Iron Skin" or "Stone Skin"
				? LogSeverity.Info
				: LogSeverity.Warning;
			GameLog.Player(message, severity);
		}
	}

	private static bool IsPoisonCondition(StatusCondition condition) =>
		string.Equals(condition?.ResourceName, "Poisoned", StringComparison.OrdinalIgnoreCase);

	private static bool IsPoisonBuff(Buff buff) =>
		buff is PoisonBuff || IsPoisonCondition(buff?.Condition);

	private void OnPlayerDeath(Player player)
	{
		_poisonWorsenCount = 0;
		GameLog.Player("[b]You died[/b]", LogSeverity.Error);
	}
}