using Godot;

/// <summary>Keeps exploration and boss music alive across scene changes and crossfades between two decks.</summary>
[GlobalClass]
public partial class MusicController : Node
{
	private const string MusicBus = "Music";
	private const float SilentVolumeDb = -60f;

	[Export] public AudioStreamPlayer DeckA { get; set; }
	[Export] public AudioStreamPlayer DeckB { get; set; }
	[Export] public MusicProfile ExplorationMusic { get; set; }
	[Export] public MusicProfile BossMusic { get; set; }
	[Export(PropertyHint.Range, "0.1,8,0.1")] public float TransitionSeconds { get; set; } = 1.5f;

	private AudioStreamPlayer _targetDeck;
	private MusicProfile _currentProfile;
	private Tween _transition;
	private bool _warnedMissingBus;

	/// <summary>The target context, exposed for the audio workbench.</summary>
	public string CurrentContext => _currentProfile == null ? "Silent"
		: _currentProfile == BossMusic ? "Boss"
		: _currentProfile == ExplorationMusic ? "Exploration"
		: "Other";

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		if (DeckA == null || DeckB == null)
		{
			GD.PushError("MusicController requires two explicitly wired music decks.");
			return;
		}

		ConfigureDeck(DeckA);
		ConfigureDeck(DeckB);
		if (AudioServer.GetBusIndex(MusicBus) < 0)
		{
			GD.PushError("MusicController requires the Music audio bus.");
			_warnedMissingBus = true;
			return;
		}

		SignalBus signalBus = SignalBus.Instance;
		if (signalBus == null)
		{
			GD.PushError("MusicController requires the SignalBus autoload.");
			return;
		}
		this.SubscribeUntilExit(signalBus,
			bus =>
			{
				bus.MainMenuEntered += OnMainMenuEntered;
				bus.LevelLoaded += OnLevelLoaded;
				bus.BossEncounterChanged += OnBossEncounterChanged;
				bus.PlayerDeath += OnPlayerDeath;
			},
			bus =>
			{
				bus.MainMenuEntered -= OnMainMenuEntered;
				bus.LevelLoaded -= OnLevelLoaded;
				bus.BossEncounterChanged -= OnBossEncounterChanged;
				bus.PlayerDeath -= OnPlayerDeath;
			});
	}

	/// <summary>Plays the shared menu/exploration loop without restarting it across scene or floor changes.</summary>
	public void PlayExploration() => TransitionTo(ExplorationMusic);

	/// <summary>Plays the pressure context for a live boss encounter.</summary>
	public void PlayBoss() => TransitionTo(BossMusic);

	/// <summary>Fades both decks to silence, for the workbench and player-death flow.</summary>
	public void StopMusic() => TransitionTo(null);

	private void OnLevelLoaded(Level level)
	{
		PlayExploration();
	}

	private void OnMainMenuEntered() => PlayExploration();

	private void OnBossEncounterChanged(bool active)
	{
		if (active) PlayBoss();
		else if (_currentProfile != null) PlayExploration();
	}

	private void OnPlayerDeath(Player player)
	{
		StopMusic();
	}

	private void TransitionTo(MusicProfile profile)
	{
		if (DeckA == null || DeckB == null || _warnedMissingBus) return;
		if (profile != null && !profile.Validate(out string error))
		{
			GD.PushWarning($"Skipping invalid music profile '{profile.ResourcePath}': {error}");
			return;
		}
		if (profile == _currentProfile && (_targetDeck?.Playing == true || profile == null)) return;

		_transition?.Kill();
		_transition = null;
		if (profile == null)
		{
			_currentProfile = null;
			_targetDeck = null;
			FadeOutBothDecks();
			return;
		}

		AudioStreamPlayer incoming = FindPlayingDeck(profile.Stream) ?? ChooseIncomingDeck();
		if (incoming.Stream != profile.Stream)
		{
			incoming.Stop();
			incoming.Stream = profile.Stream;
		}
		if (!incoming.Playing)
		{
			incoming.VolumeDb = SilentVolumeDb;
			incoming.Play();
		}

		_currentProfile = profile;
		_targetDeck = incoming;
		var transition = CreateTween();
		transition.SetParallel(true);
		transition.TweenProperty(incoming, "volume_db", profile.GainDb, TransitionSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		TweenPropertyToSilence(transition, DeckA, incoming);
		TweenPropertyToSilence(transition, DeckB, incoming);
		transition.Finished += () =>
		{
			StopIfNotTarget(DeckA, incoming);
			StopIfNotTarget(DeckB, incoming);
		};
		_transition = transition;
	}

	private void FadeOutBothDecks()
	{
		var transition = CreateTween();
		transition.SetParallel(true);
		TweenPropertyToSilence(transition, DeckA, null);
		TweenPropertyToSilence(transition, DeckB, null);
		transition.Finished += () =>
		{
			DeckA.Stop();
			DeckB.Stop();
		};
		_transition = transition;
	}

	private void TweenPropertyToSilence(Tween transition, AudioStreamPlayer deck, AudioStreamPlayer target)
	{
		if (deck == target || !deck.Playing) return;
		transition.TweenProperty(deck, "volume_db", SilentVolumeDb, TransitionSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}

	private AudioStreamPlayer FindPlayingDeck(AudioStream stream)
	{
		if (DeckA.Playing && DeckA.Stream == stream) return DeckA;
		if (DeckB.Playing && DeckB.Stream == stream) return DeckB;
		return null;
	}

	private AudioStreamPlayer ChooseIncomingDeck()
	{
		if (_targetDeck == DeckA) return DeckB;
		if (_targetDeck == DeckB) return DeckA;
		if (!DeckA.Playing) return DeckA;
		if (!DeckB.Playing) return DeckB;
		return DeckA.VolumeDb <= DeckB.VolumeDb ? DeckA : DeckB;
	}

	private void StopIfNotTarget(AudioStreamPlayer deck, AudioStreamPlayer target)
	{
		if (deck != target) deck.Stop();
	}

	private void ConfigureDeck(AudioStreamPlayer deck)
	{
		deck.Bus = MusicBus;
		deck.VolumeDb = SilentVolumeDb;
		deck.ProcessMode = ProcessModeEnum.Always;
	}
}