using Godot;

/// <summary>
/// Class-selection panel shown when starting a new game: a carousel over the character
/// catalog with a live 3D preview, description, and stat readout. Emits ClassConfirmed
/// with the chosen class, or Canceled to return to slot selection.
/// </summary>
public partial class CharacterPanel : Control
{
	[Signal] public delegate void ClassConfirmedEventHandler(CharacterClass characterClass);
	[Signal] public delegate void CanceledEventHandler();

	[Export] public CharacterCatalog Catalog { get; set; }

	private CharacterPreview _preview;
	private Label _classNameLabel;
	private Label _classDescription;
	private Label _strengthValue;
	private Label _dexterityValue;
	private Label _vitalityValue;
	private Label _intelligenceValue;
	private Label _healthValue;
	private Button _prevButton;
	private Button _nextButton;
	private Button _backButton;
	private Button _confirmButton;
	private int _selectedIndex;

	private CharacterClass SelectedClass =>
		Catalog != null && Catalog.Classes.Count > 0 ? Catalog.Classes[_selectedIndex] : null;

	public override void _Ready()
	{
		_preview = GetNode<CharacterPreview>("%CharacterPreview");
		_classNameLabel = GetNode<Label>("%ClassNameLabel");
		_classDescription = GetNode<Label>("%ClassDescription");
		_strengthValue = GetNode<Label>("%StrengthValue");
		_dexterityValue = GetNode<Label>("%DexterityValue");
		_vitalityValue = GetNode<Label>("%VitalityValue");
		_intelligenceValue = GetNode<Label>("%IntelligenceValue");
		_healthValue = GetNode<Label>("%HealthValue");
		_prevButton = GetNode<Button>("%PrevButton");
		_nextButton = GetNode<Button>("%NextButton");
		_backButton = GetNode<Button>("%BackButton");
		_confirmButton = GetNode<Button>("%ConfirmButton");

		_prevButton.Pressed += () => SelectIndex(_selectedIndex - 1);
		_nextButton.Pressed += () => SelectIndex(_selectedIndex + 1);
		_backButton.Pressed += () => EmitSignalCanceled();
		_confirmButton.Pressed += ConfirmSelection;
	}

	/// <summary>Shows the panel and resumes preview rendering.</summary>
	public void Open()
	{
		Visible = true;
		_preview.SetActive(true);
		SelectIndex(_selectedIndex);
		_confirmButton.CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>Hides the panel and stops paying for the invisible preview viewport.</summary>
	public void Close()
	{
		Visible = false;
		_preview.SetActive(false);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
		{
			return;
		}

		if (@event.IsActionReleased("ui_cancel"))
		{
			EmitSignalCanceled();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed("ui_left"))
		{
			SelectIndex(_selectedIndex - 1);
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed("ui_right"))
		{
			SelectIndex(_selectedIndex + 1);
			GetViewport().SetInputAsHandled();
		}
	}

	private void ConfirmSelection()
	{
		CharacterClass selected = SelectedClass;
		if (selected != null)
		{
			EmitSignalClassConfirmed(selected);
		}
	}

	/// <summary>Wraps the index into the catalog and refreshes preview, text, and stat readout.</summary>
	private void SelectIndex(int index)
	{
		int count = Catalog?.Classes.Count ?? 0;
		if (count == 0)
		{
			GD.PrintErr("CharacterPanel has no catalog classes to show.");
			return;
		}

		_selectedIndex = ((index % count) + count) % count;
		CharacterClass characterClass = Catalog.Classes[_selectedIndex];

		_preview.ShowClass(characterClass);
		_classNameLabel.Text = characterClass.DisplayName;
		_classDescription.Text = characterClass.Description;
		_strengthValue.Text = characterClass.BaseStrength.ToString("0");
		_dexterityValue.Text = characterClass.BaseDexterity.ToString("0");
		_vitalityValue.Text = characterClass.BaseVitality.ToString("0");
		_intelligenceValue.Text = characterClass.BaseIntelligence.ToString("0");

		// Run the class through the real stat derivation so the displayed HP always
		// matches what the player will actually spawn with.
		var stats = new PlayerStats();
		characterClass.ApplyToStats(stats);
		_healthValue.Text = stats.MaxHealth.ToString("0");
	}
}
