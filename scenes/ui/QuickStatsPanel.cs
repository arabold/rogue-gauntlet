using Godot;
using System;

public partial class QuickStatsPanel : PanelContainer
{
	private PlayerStats _stats;
	private Inventory _inventory;
	private Action _unsubscribeStats = () => { };

	public Label DamageValue;
	public Label ArmorValue;
	public Label CritChanceValue;

	public override void _Ready()
	{
		DamageValue = GetNode<Label>("%DamageValue");
		ArmorValue = GetNode<Label>("%ArmorValue");
		CritChanceValue = GetNode<Label>("%CritChanceValue");
	}

	/// <summary>
	/// Shows <paramref name="stats"/>, with damage and crit chance for the main-hand weapon in
	/// <paramref name="inventory"/>. Equipping or removing a weapon registers or removes its modifiers, so the stats'
	/// <c>Changed</c> also covers weapon swaps.
	/// </summary>
	public void Initialize(PlayerStats stats, Inventory inventory)
	{
		_unsubscribeStats();
		_unsubscribeStats = () => { };

		_stats = stats;
		_inventory = inventory;
		_unsubscribeStats = this.SubscribeUntilExit(
			_stats,
			stats => stats.Changed += Update,
			stats => stats.Changed -= Update);

		Update();
	}

	private void Update()
	{
		if (_stats == null)
		{
			DamageValue.Text = "0";
			ArmorValue.Text = "0";
			CritChanceValue.Text = "0%";
			return;
		}

		var attack = _stats.ResolveAttack(_inventory?.EquippedItems[EquipmentSlot.WeaponHand]?.Item as Weapon);
		DamageValue.Text = $"{attack.MinDamage:F0} - {attack.MaxDamage:F0}";
		ArmorValue.Text = $"{_stats.Armor:F0}";
		CritChanceValue.Text = $"{attack.CritChance * 100:F0}%";
	}
}
