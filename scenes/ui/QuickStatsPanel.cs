using Godot;
using System;

public partial class QuickStatsPanel : PanelContainer
{
	private PlayerStats _stats;

	public Label DamageValue;
	public Label ArmorValue;
	public Label CritChanceValue;

	public override void _Ready()
	{
		DamageValue = GetNode<Label>("%DamageValue");
		ArmorValue = GetNode<Label>("%ArmorValue");
		CritChanceValue = GetNode<Label>("%CritChanceValue");
	}

	public void Initialize(PlayerStats stats)
	{
		if (_stats != null)
		{
			_stats.Changed -= Update;
		}
		_stats = stats;
		if (_stats != null)
		{
			_stats.Changed += Update;
		}

		Update();
	}

	public override void _ExitTree()
	{
		if (_stats != null)
		{
			_stats.Changed -= Update;
			_stats = null;
		}

		base._ExitTree();
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

		DamageValue.Text = $"{_stats.MinDamage:F0} - {_stats.MaxDamage:F0}";
		ArmorValue.Text = $"{_stats.Armor:F0}";
		CritChanceValue.Text = $"{_stats.CritChance * 100:F0}%";
	}
}
