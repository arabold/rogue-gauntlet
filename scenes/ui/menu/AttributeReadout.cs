using Godot;

/// <summary>One hero attribute in the Gear tab: its own value, and in gold what gear and buffs add to it.</summary>
public partial class AttributeReadout : VBoxContainer
{
	[Export] public string Caption { get; set { field = value; if (IsNodeReady()) { CaptionLabel.Text = value; } } } = "";
	[Export] public Label CaptionLabel { get; set; }
	[Export] public Label ValueLabel { get; set; }
	[Export] public Label BonusLabel { get; set; }

	public override void _Ready() => CaptionLabel.Text = Caption;

	public void Display(float own, float total)
	{
		int bonus = Mathf.RoundToInt(total) - Mathf.RoundToInt(own);
		ValueLabel.Text = Mathf.RoundToInt(own).ToString();
		BonusLabel.Text = bonus > 0 ? $"+{bonus}" : bonus < 0 ? $"−{-bonus}" : "";
		BonusLabel.Visible = bonus != 0;
	}
}