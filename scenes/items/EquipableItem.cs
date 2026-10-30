using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;


/// <summary>
/// Flags for equipment slots.
/// </summary>
[Flags]
public enum ValidSlots
{
	Head = EquipmentSlot.Head,
	Chest = EquipmentSlot.Chest,
	Hands = EquipmentSlot.Hands,
	Legs = EquipmentSlot.Legs,
	Feet = EquipmentSlot.Feet,
	Neck = EquipmentSlot.Neck,
	LeftRing = EquipmentSlot.LeftRing,
	RightRing = EquipmentSlot.RightRing,
	WeaponHand = EquipmentSlot.WeaponHand,
	ShieldHand = EquipmentSlot.ShieldHand,
	Arrows = EquipmentSlot.Arrows,
}

[Tool]
[GlobalClass]
public partial class EquipableItem : BuffedItem
{
	/// <summary>
	/// Type of this item
	/// </summary>
	[Export] public GuardDefinition Guard { get; set => SetValue(ref field, value); }
	[Export] public ValidSlots ValidSlots { get; set => SetValue(ref field, value); } = 0;

	/// <summary>Words and affixes this kind of equipment can roll at creation (authored).</summary>
	[Export] public EquipmentRollTable RollTable { get; set => SetValue(ref field, value); }

	/// <summary>
	/// Quality rolled at creation (see <see cref="LootRoller"/>), always visible as color and quality word. Definitions and
	/// fixed creation stay <see cref="ItemQuality.Normal"/>.
	/// </summary>
	[Export] public ItemQuality Quality { get; set => SetValue(ref field, value); } = ItemQuality.Normal;

	/// <summary>The Poor or Superior adjective picked from <see cref="RollTable"/> at creation, e.g. "cracked"; "" otherwise.</summary>
	[Export] public string QualityAdjective { get; set => SetValue(ref field, value); } = "";

	/// <summary>
	/// Multiplier on this instance's intrinsic values (weapon damage, armor points, jewelry bonuses), rolled inside its
	/// quality's band. Definitions and fixed creation keep exactly 1 so authored values show through.
	/// </summary>
	[Export] public float MagnitudeScale { get; set => SetValue(ref field, value); } = 1f;

	/// <summary>
	/// Whether this instance's attributes (affixes and rolled value) are known. Magic and Rare items are created
	/// unknown and revealed by wearing or identifying them; definitions and every other quality stay known.
	/// </summary>
	[Export] public bool AttributesKnown { get; set => SetValue(ref field, value); } = true;

	/// <summary>Harmful types such as the Ring of Frailty are always created cursed (authored).</summary>
	[Export] public bool AlwaysCursed { get; protected set => SetValue(ref field, value); }

	/// <summary>
	/// The hidden penalty a cursed item applies while worn, rolled at creation; empty when uncursed. Definitions and
	/// fixed creation are never cursed.
	/// </summary>
	[Export] public StatModifier[] CursePenalty { get; set => SetValue(ref field, value); } = [];

	/// <summary>A cursed item cannot be removed once worn until the curse is lifted.</summary>
	public bool IsCursed => CursePenalty is { Length: > 0 };

	/// <summary>
	/// Whether the player knows if this item is cursed. Rolled items start unknown; Identify, a blocked removal,
	/// Enchant and Remove Curse reveal it.
	/// </summary>
	[Export] public bool CurseStatusKnown { get; set => SetValue(ref field, value); } = true;

	/// <summary>
	/// The base item's ladder rung (1-5) for weapons and armor, independent of its rolled quality. Loot-table depth
	/// windows fence it (see docs/stats-and-itemization.md). Jewelry does not use it.
	/// </summary>
	[Export] public int Tier { get; protected set => SetValue(ref field, value); } = 1;

	/// <summary>
	/// Instance-level affixes rolled onto this item when it dropped. Empty on shared
	/// definitions; populated on the duplicated instance produced by <see cref="LootRoller"/>.
	/// </summary>
	[Export] public Array<RolledAffix> Affixes { get; set => SetValue(ref field, value); } = [];

	/// <summary>
	/// The lowercase quality word that leads unidentified names: the Poor or Superior adjective, "magic" or "rare"; "" for
	/// Normal.
	/// </summary>
	public string QualityWord => Quality switch
	{
		ItemQuality.Magic => "magic",
		ItemQuality.Rare => "rare",
		_ => QualityAdjective,
	};

	/// <summary>The stats the item shows once identified: its intrinsic stats, then its rolled affixes.</summary>
	public IEnumerable<StatModifier> AttributeModifiers =>
		BuildIntrinsicModifiers().Concat(Affixes?.Where(affix => affix?.Modifiers != null)
			.SelectMany(affix => affix.Modifiers).Where(modifier => modifier != null) ?? []);

	/// <summary>The curse penalty's stats while cursed; hidden in the tooltip until the curse is revealed.</summary>
	public IEnumerable<StatModifier> CurseModifiers => CursePenalty?.Where(modifier => modifier != null) ?? [];

	/// <summary>Everything this item grants while equipped: its attributes and any curse penalty.</summary>
	public IEnumerable<StatModifier> StatModifiers => AttributeModifiers.Concat(CurseModifiers);

	/// <summary>Wearing an item reveals its attributes; its curse shows only when the player tries to remove it.</summary>
	public virtual void OnEquipped(Player player)
	{
		RevealAttributes();
		ApplyBuff(player);
		RegisterStats(player.Stats);
	}

	/// <summary>Re-registers a worn item's stats after an in-place change such as enchanting or lifting a curse.</summary>
	public void RefreshStats(Player player)
	{
		player.Stats.RemoveModifiersFrom(this);
		RegisterStats(player.Stats);
	}

	/// <summary>Registers everything this item grants while worn under the item instance.</summary>
	protected virtual void RegisterStats(PlayerStats stats) => stats.AddModifiers(this, StatModifiers);

	/// <summary>Reveals this item's attributes (affixes and rolled value); emits <c>Changed</c> only if they were hidden.</summary>
	public void RevealAttributes() => AttributesKnown = true;

	/// <summary>Reveals whether this item is cursed; emits <c>Changed</c> only if that was unknown.</summary>
	public void RevealCurseStatus() => CurseStatusKnown = true;

	/// <summary>
	/// Lifts any curse and leaves the status known as uncursed. The result is the same whether or not the item was
	/// cursed, so lifting never reveals a curse it removed.
	/// </summary>
	public void LiftCurse()
	{
		CursePenalty = [];
		RevealCurseStatus();
	}

	public virtual void OnUnequipped(Player player)
	{
		player.Stats.RemoveModifiersFrom(this);
		RemoveBuff(player);
	}

	/// <summary>
	/// The item's own stats (damage, armor, jewelry bonuses) after quality scaling, all registered under the item
	/// instance so unequipping reverses exactly them.
	/// </summary>
	protected virtual IEnumerable<StatModifier> BuildIntrinsicModifiers() => [];

	/// <summary>
	/// The identified display name: the lead word, the base name and the tail. Poor and Superior lead with their word
	/// ("Cracked Broadsword"); Magic and Rare wrap the base name in their first prefix and first suffix ("Vicious
	/// Broadsword of the Ox"). Further affixes add stats but no name.
	/// </summary>
	public string ComposeName(string baseName) => TextExtensions.JoinWords(LeadWord, baseName, NameTail);

	/// <summary>The name while attributes are hidden: the quality word and base name, e.g. "Magic Broadsword".</summary>
	public string ComposeUnidentifiedName(string baseName) => TextExtensions.JoinWords(QualityWord.UpperFirst(), baseName);

	/// <summary>The word before the base name: the first prefix for Magic and Rare, else the Poor or Superior adjective.</summary>
	protected string LeadWord => Quality >= ItemQuality.Magic ? FirstAffixName(AffixKind.Prefix) : QualityAdjective.UpperFirst();

	/// <summary>The words after the base name: the first suffix for Magic and Rare.</summary>
	protected virtual string NameTail => Quality >= ItemQuality.Magic ? FirstAffixName(AffixKind.Suffix) : null;

	/// <summary>The name fragment of the first rolled affix of <paramref name="kind"/>, or null.</summary>
	protected string FirstAffixName(AffixKind kind) =>
		Affixes?.FirstOrDefault(affix => affix?.Kind == kind && !string.IsNullOrEmpty(affix.NameFragment))?.NameFragment;

	/// <summary>Scales an authored whole value, keeping a nonzero result at least 1 in magnitude.</summary>
	protected float ScaleWhole(float value) => Scale(value, 1f);

	/// <summary><paramref name="modifier"/> scaled by <see cref="MagnitudeScale"/>; the authored modifier itself at scale 1.</summary>
	protected StatModifier Scale(StatModifier modifier) => MagnitudeScale == 1f ? modifier : new()
	{
		Stat = modifier.Stat,
		Op = modifier.Op,
		Value = ScaledValue(modifier),
	};

	/// <summary>A modifier's value scaled by <see cref="MagnitudeScale"/> and rounded to its unit.</summary>
	protected float ScaledValue(StatModifier modifier) => Scale(modifier.Value, modifier.Unit);

	/// <summary>Rounds half away from zero to <paramref name="unit"/>, keeping a nonzero value at least one unit. Scale 1
	/// returns the authored value untouched.</summary>
	private float Scale(float value, float unit)
	{
		if (value == 0f || MagnitudeScale == 1f) { return value; }
		float steps = MathF.Round(value * MagnitudeScale / unit, MidpointRounding.AwayFromZero);
		return (steps == 0f ? MathF.Sign(value) : steps) * unit;
	}
}
