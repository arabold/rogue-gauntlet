using Godot;
using System;

public partial class CharacterDialog : PanelContainer
{
	public Button CloseButton;
	public EquipmentPanel EquipmentPanel;
	public InventoryPanel InventoryPanel;
	public QuickStatsPanel QuickStatsPanel;

	public override void _Ready()
	{
		CloseButton = GetNode<Button>("%CloseButton");
		CloseButton.Pressed += OnCloseButtonPressed;

		InventoryPanel = GetNode<InventoryPanel>("%InventoryPanel");
		InventoryPanel.TargetingBanner = GetNode<Label>("%TargetingBanner");
		EquipmentPanel = GetNode<EquipmentPanel>("%EquipmentPanel");
		QuickStatsPanel = GetNode<QuickStatsPanel>("%QuickStatsPanel");
	}

	public void Open(Player player)
	{
		InventoryPanel.Initialize(player.Inventory, player);
		EquipmentPanel.Initialize(player.Inventory);
		QuickStatsPanel.Initialize(player.Stats);

		Show();
	}

	public void Close()
	{
		InventoryPanel.Initialize(null);
		EquipmentPanel.Initialize(null);
		QuickStatsPanel.Initialize(null);

		Hide();
	}

	private void OnCloseButtonPressed()
	{
		Close();
	}
}
