using Godot;

/// <summary>
/// One save slot as a card: the run's hero, class, a one-line state and when it was last saved, or an invitation to
/// start a run in an empty slot. Pressing it is the menu's business.
/// </summary>
public partial class RunCard : Button
{
	[Export] public CharacterPreview Portrait { get; set; }
	/// <summary>The portrait's frame; greyed for a fallen hero and hidden for an empty slot.</summary>
	[Export] public Control PortraitView { get; set; }
	/// <summary>The mark an empty slot shows in the portrait's place.</summary>
	[Export] public Control EmptyMark { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public Label LineLabel { get; set; }
	/// <summary>When the run was last saved, in the player's local time and date format.</summary>
	[Export] public Label SavedLabel { get; set; }
	[Export] public Color FallenTint { get; set; } = new(0.42f, 0.4f, 0.4f);

	private static readonly StringName Caption = "CaptionLabel", Warning = "WarningLabel";

	private bool _hasPortrait;

	public SaveSlotMetadata Slot { get; private set; }
	/// <summary>The slot holds a run of the current version, in progress or fallen; anything else is free to start in.</summary>
	public bool HoldsRun => Slot is { HasSave: true, CanLoad: true };

	public override void _Ready() => Portrait.SetActive(false);

	/// <summary>The portrait renders only while the card can be seen: Runs stays in the tree behind the other screens.</summary>
	public override void _Notification(int what)
	{
		if (what == NotificationVisibilityChanged)
		{
			Portrait?.SetActive(_hasPortrait && IsVisibleInTree());
		}
	}

	/// <summary>Fills the card from <paramref name="slot"/>; <paramref name="characterClass"/> is its run's class, if it holds one.</summary>
	public void Show(SaveSlotMetadata slot, CharacterClass characterClass)
	{
		Slot = slot;
		bool fallen = HoldsRun && slot.Outcome == RunOutcome.Died;
		bool portrait = HoldsRun && characterClass != null;
		PortraitView.Visible = portrait;
		PortraitView.Modulate = fallen ? FallenTint : Colors.White;
		EmptyMark.Visible = !portrait;
		_hasPortrait = portrait;
		Portrait.SetActive(portrait && IsVisibleInTree());
		if (portrait)
		{
			Portrait.ShowClass(characterClass);
		}

		NameLabel.Text = !HoldsRun ? "New Run" : characterClass?.DisplayName ?? "Hero";
		LineLabel.ThemeTypeVariation = fallen ? Warning : Caption;
		LineLabel.Text = !HoldsRun ? (slot.HasSave ? "Old save" : "")
			: fallen ? $"Fell on Depth {slot.DungeonDepth}"
			: $"Depth {slot.DungeonDepth} · Level {slot.XpLevel}";
		SavedLabel.Text = HoldsRun && slot.SavedAt?.ToLocalTime() is { } saved ? $"{saved:d} · {saved:t}" : "";
	}
}
