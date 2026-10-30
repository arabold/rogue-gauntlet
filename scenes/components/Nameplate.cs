using Godot;

/// <summary>
/// Floats a name, and optionally a health bar, above an actor or pickup as one unit: the plate's single
/// <see cref="ConstantScreenSize3D"/> scales its parts and the gaps between them together, so their spacing stays
/// constant on screen at any zoom. The name fades in only while the player is near, so passing something shows what
/// it is without flooding the screen. Text grows upward from the label (bottom-aligned and wrapped). Everything on the
/// plate draws over the black fog caps that conceal ordinary actors by depth, so the plate hides itself while the tile
/// under it is undiscovered. Standalone previews have no fog. The owner assigns the label's text.
/// </summary>
[GlobalClass]
public partial class Nameplate : Node3D
{
	/// <summary>The name starts showing or hiding, e.g. so a health bar can appear with it.</summary>
	[Signal] public delegate void ShownChangedEventHandler(bool shown);

	private static readonly StringName PlayerGroup = "player";
	/// <summary>Extra distance before a shown name hides again, so a player at the edge doesn't make it flicker.</summary>
	private const float HideMargin = 0.5f;
	private const float PlayerLookupInterval = 0.5f;

	/// <summary>Required: the name this plate fades in and out.</summary>
	[Export] public Label3D NameLabel { get; set; }
	/// <summary>Horizontal distance from the player within which the name shows.</summary>
	[Export(PropertyHint.Range, "0,40,0.1,or_greater")] public float ShowDistance { get; set; } = 8f;
	/// <summary>Seconds a full fade in or out takes.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float FadeDuration { get; set; } = 0.15f;
	/// <summary>Optional: hides the name once the actor dies.</summary>
	[Export] public HealthComponent Health { get; set; }
	/// <summary>Optional actor cloak that hides the name while invisible.</summary>
	[Export] public InvisibilityComponent Invisibility { get; set; }
	/// <summary>Lets the owner hold the name back, e.g. while a thrown drop is still in the air.</summary>
	[Export] public bool Enabled { get; set; } = true;

	private Node3D _player;
	private ExplorationVisibility _exploration;
	private Cooldown _playerLookup;
	private float _outlineAlpha;
	private AlphaFade _fade;

	/// <summary>True while the name is showing or fading in.</summary>
	public bool IsShown => _fade?.IsShown == true;

	public override void _Ready()
	{
		if (NameLabel == null)
		{
			GD.PushError($"{Name}: a nameplate requires an assigned NameLabel.");
			SetProcess(false);
			return;
		}
		_outlineAlpha = NameLabel.OutlineModulate.A;
		_exploration = new ExplorationVisibility(this);
		_fade = new AlphaFade(NameLabel, ApplyFade);
		RefreshFog();
	}

	public override void _Process(double delta)
	{
		bool allowed = RefreshFog() && Enabled && Health?.IsDead != true && Invisibility?.IsInvisible != true;
		Node3D player = allowed ? FindPlayer(delta) : null;
		bool shown = player != null
			&& player.GlobalPosition.HorizontalDistanceTo(GlobalPosition) <= ShowDistance + (IsShown ? HideMargin : 0f);
		if (_fade.Set(shown, FadeDuration)) EmitSignalShownChanged(shown);
	}

	/// <summary>Hides the whole plate, bar included, while the tile under it is undiscovered.</summary>
	private bool RefreshFog()
	{
		bool revealed = _exploration.IsRevealed(GlobalPosition);
		if (Visible != revealed) Visible = revealed;
		return revealed;
	}

	/// <summary>Caches the player; a missing one is looked up again only every <see cref="PlayerLookupInterval"/>.</summary>
	private Node3D FindPlayer(double delta)
	{
		if (IsInstanceValid(_player) && _player.IsInsideTree()) return _player;
		if (!_playerLookup.Tick(delta)) return null;
		_playerLookup.Start(PlayerLookupInterval);
		return _player = GetTree().GetFirstNodeInGroup(PlayerGroup) as Node3D;
	}

	/// <summary>Fades through alpha only, keeping the color the owner or scene gave the name.</summary>
	private void ApplyFade(float alpha)
	{
		NameLabel.Modulate = NameLabel.Modulate with { A = alpha };
		NameLabel.OutlineModulate = NameLabel.OutlineModulate with { A = alpha * _outlineAlpha };
	}
}