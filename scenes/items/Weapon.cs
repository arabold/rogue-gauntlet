using Godot;
using System;

[GlobalClass]
public partial class Weapon : EquippableItem
{
	/// <summary>
	/// Minimum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMin { get; private set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Maximum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMax { get; private set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Critical hit chance for this weapon (stacks up with other items' crit chance)
	/// </summary>
	[Export] public float CritChance { get; private set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Whether this weapon is two-handed
	/// </summary>
	[Export] public bool IsTwoHanded { get; private set => SetValue(ref field, value); } = false;
	/// <summary>
	/// Whether this weapon is ranged
	/// </summary>
	[Export] public bool IsRanged { get; private set => SetValue(ref field, value); } = false;

	Weapon()
	{
		Type = EquippableItemType.Weapon;
	}

	public override void OnEquipped(Player player)
	{
		// Apply stats
		base.OnEquipped(player);
		var stats = player.Stats;
		stats.BaseMinDamage += DamageMin;
		stats.BaseMaxDamage += DamageMax;
		stats.BaseCritChance += CritChance;
	}

	public override void OnUnequipped(Player player)
	{
		// Reset stats
		var stats = player.Stats;
		stats.BaseMinDamage -= DamageMin;
		stats.BaseMaxDamage -= DamageMax;
		stats.BaseCritChance -= CritChance;
		base.OnUnequipped(player);
	}
}
