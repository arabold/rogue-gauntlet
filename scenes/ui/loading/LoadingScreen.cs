using System.Threading.Tasks;
using Godot;

/// <summary>
/// The depth card shown while a floor is built: the depth the hero is headed to and one short tip. It is an autoload,
/// so it stays up across the scene change and the floor's generation. Both block the main thread, so
/// <see cref="Cover"/> finishes its fade before returning: the frozen frames then show a finished card instead of a
/// stalled animation.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
	public static LoadingScreen Instance { get; private set; }

	/// <summary>Everything the screen draws; fading it fades the screen.</summary>
	[Export] public Control Card { get; set; }
	[Export] public Label DepthLabel { get; set; }
	[Export] public Label TipLabel { get; set; }
	/// <summary>One is shown per load, starting at a random tip and then in order, so a session rarely repeats one. A floor
	/// loads in about a second, so keep each to a glance: seven words at most.</summary>
	[Export] public string[] Tips { get; set; } = [];
	[Export(PropertyHint.Range, "0,2,0.05,suffix:s")] public float FadeInSeconds { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "0,2,0.05,suffix:s")] public float FadeOutSeconds { get; set; } = 0.45f;

	/// <summary>True from the start of <see cref="Cover"/> until the screen has faded out or been dismissed.</summary>
	public bool IsCovering => Visible;

	// Picking a tip is cosmetic: its own generator, never one of the run's seeded streams.
	private readonly RandomNumberGenerator _tipRandom = new();
	private int _tipIndex = -1;
	private Tween _tween;

	public override void _Ready()
	{
		if (Instance != null && IsInstanceValid(Instance))
		{
			GD.PrintErr("Multiple LoadingScreen instances detected.");
			QueueFree();
			return;
		}

		Instance = this;
		_tipRandom.Randomize();
		Hide();
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	/// <summary>
	/// Shows the card for travel to <paramref name="depth"/>. Completes once the card has faded in and reached the
	/// screen, so the caller can block on loading next. A card already up is refilled in place.
	/// </summary>
	public async Task Cover(uint depth)
	{
		DepthLabel.Text = $"Depth {depth}";
		TipLabel.Text = NextTip();

		_tween?.Kill();
		if (!Visible)
		{
			Card.Modulate = Colors.Transparent;
		}
		Show();
		_tween = CreateTween();
		_tween.TweenProperty(Card, "modulate:a", 1f, FadeInSeconds);
		await Played(_tween);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	/// <summary>Fades the card out over the arrived floor, then hides it.</summary>
	public async Task Reveal()
	{
		if (!Visible)
		{
			return;
		}

		_tween?.Kill();
		Tween fade = _tween = CreateTween();
		fade.TweenProperty(Card, "modulate:a", 0f, FadeOutSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Played(fade);
		// A cover that started meanwhile owns the card now.
		if (_tween == fade)
		{
			Hide();
		}
	}

	/// <summary>
	/// Waits for <paramref name="tween"/> to finish or be killed. A killed tween never reports finishing, and a caller
	/// left waiting on it would stall the floor change it is part of.
	/// </summary>
	private async Task Played(Tween tween)
	{
		while (tween.IsValid() && tween.IsRunning())
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	/// <summary>Hides the card at once; a <see cref="Cover"/> or <see cref="Reveal"/> still playing ends there.</summary>
	public void Dismiss()
	{
		_tween?.Kill();
		Hide();
	}

	private string NextTip()
	{
		if (Tips.Length == 0)
		{
			return "";
		}

		_tipIndex = _tipIndex < 0 ? _tipRandom.RandiRange(0, Tips.Length - 1) : (_tipIndex + 1) % Tips.Length;
		return Tips[_tipIndex];
	}
}
