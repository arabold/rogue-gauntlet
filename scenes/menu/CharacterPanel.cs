using Godot;

/// <summary>
/// The hero picker shown when starting a run: a carousel over the character catalog with the class's name, tagline
/// and stats. The hero itself stands on the menu's stage; <see cref="ClassShown"/> tells the menu which one. Emits
/// <see cref="ClassConfirmed"/> with the chosen class, or <see cref="Canceled"/> to go back.
/// </summary>
public partial class CharacterPanel : Control
{
	[Signal] public delegate void ClassShownEventHandler(CharacterClass characterClass);
	[Signal] public delegate void ClassConfirmedEventHandler(CharacterClass characterClass);
	[Signal] public delegate void CanceledEventHandler();

	[Export] public CharacterCatalog Catalog { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public Label TaglineLabel { get; set; }
	[Export] public Label StrengthValue { get; set; }
	[Export] public Label DexterityValue { get; set; }
	[Export] public Label VitalityValue { get; set; }
	[Export] public Label IntelligenceValue { get; set; }
	[Export] public Label HealthValue { get; set; }
	[Export] public Label ManaValue { get; set; }
	[Export] public Button PreviousButton { get; set; }
	[Export] public Button NextButton { get; set; }
	[Export] public Button BackButton { get; set; }
	[Export] public Button StartButton { get; set; }

	private static readonly StringName Cancel = "ui_cancel", Left = "ui_left", Right = "ui_right";
	private int _selectedIndex;

	public override void _Ready()
	{
		PreviousButton.Pressed += () => SelectIndex(_selectedIndex - 1);
		NextButton.Pressed += () => SelectIndex(_selectedIndex + 1);
		BackButton.Pressed += EmitSignalCanceled;
		StartButton.Pressed += () => EmitSignalClassConfirmed(Catalog.Classes[_selectedIndex]);
	}

	/// <summary>Shows the picker on the class it last showed.</summary>
	public void Open()
	{
		Visible = true;
		SelectIndex(_selectedIndex);
		StartButton.CallDeferred(Control.MethodName.GrabFocus);
	}

	public void Close() => Visible = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
		{
			return;
		}

		if (@event.IsActionReleased(Cancel))
		{
			EmitSignalCanceled();
		}
		else if (@event.IsActionPressed(Left))
		{
			SelectIndex(_selectedIndex - 1);
		}
		else if (@event.IsActionPressed(Right))
		{
			SelectIndex(_selectedIndex + 1);
		}
		else
		{
			return;
		}
		GetViewport().SetInputAsHandled();
	}

	/// <summary>Wraps the index into the catalog and shows that class.</summary>
	private void SelectIndex(int index)
	{
		int count = Catalog?.Classes.Count ?? 0;
		if (count == 0)
		{
			GD.PushError($"{Name}: the catalog has no classes to show.");
			return;
		}

		_selectedIndex = ((index % count) + count) % count;
		CharacterClass characterClass = Catalog.Classes[_selectedIndex];
		NameLabel.Text = characterClass.DisplayName;
		TaglineLabel.Text = characterClass.Tagline;
		StrengthValue.Text = characterClass.BaseStrength.ToString("0");
		DexterityValue.Text = characterClass.BaseDexterity.ToString("0");
		VitalityValue.Text = characterClass.BaseVitality.ToString("0");
		IntelligenceValue.Text = characterClass.BaseIntelligence.ToString("0");

		// Run the class through the real stat derivation so the shown health and mana match what the hero spawns with.
		var stats = new PlayerStats();
		characterClass.ApplyToStats(stats);
		HealthValue.Text = stats.MaxHealth.ToString("0");
		ManaValue.Text = stats.MaxMana.ToString("0");
		EmitSignalClassShown(characterClass);
	}
}
