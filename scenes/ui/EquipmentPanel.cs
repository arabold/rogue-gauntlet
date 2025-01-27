using Godot;

public partial class EquipmentPanel : PanelContainer
{
	public EquipmentItemPanel AmuletPanel { get; private set; }
	public EquipmentItemPanel HelmetPanel { get; private set; }
	public EquipmentItemPanel ArrowsPanel { get; private set; }
	public EquipmentItemPanel WeaponPanel { get; private set; }
	public EquipmentItemPanel ArmorPanel { get; private set; }
	public EquipmentItemPanel ShieldPanel { get; private set; }
	public EquipmentItemPanel RightRingPanel { get; private set; }
	public EquipmentItemPanel GlovesPanel { get; private set; }
	public EquipmentItemPanel LeftRingPanel { get; private set; }
	public EquipmentItemPanel LegsPanel { get; private set; }
	public EquipmentItemPanel BootsPanel { get; private set; }

	public override void _Ready()
	{
		AmuletPanel = GetNode<EquipmentItemPanel>("%AmuletPanel");
		HelmetPanel = GetNode<EquipmentItemPanel>("%HelmetPanel");
		ArrowsPanel = GetNode<EquipmentItemPanel>("%ArrowsPanel");
		WeaponPanel = GetNode<EquipmentItemPanel>("%WeaponPanel");
		ArmorPanel = GetNode<EquipmentItemPanel>("%ArmorPanel");
		ShieldPanel = GetNode<EquipmentItemPanel>("%ShieldPanel");
		RightRingPanel = GetNode<EquipmentItemPanel>("%RightRingPanel");
		GlovesPanel = GetNode<EquipmentItemPanel>("%GlovesPanel");
		LeftRingPanel = GetNode<EquipmentItemPanel>("%LeftRingPanel");
		LegsPanel = GetNode<EquipmentItemPanel>("%LegsPanel");
		BootsPanel = GetNode<EquipmentItemPanel>("%BootsPanel");
	}
}
