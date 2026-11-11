using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Admits sound broadcasts, enforces shared voice limits, and owns voices beyond source lifetimes.
/// </summary>
[GlobalClass]
public partial class AudioController : Node
{
	private const string EffectsBus = "Effects";
	private const string UiBus = "UI";
	private const int MaximumUiVoiceSlots = 16;

	[Export] public PackedScene WorldSoundScene { get; set; }
	[Export] public Node3D WorldVoicesRoot { get; set; }
	[Export] public Node UiVoicesRoot { get; set; }
	[Export(PropertyHint.Range, "1,64,1")] public int MaxWorldVoices { get; set; } = 24;
	[Export(PropertyHint.Range, "1,16,1")] public int MaxUiVoices { get; set; } = 4;

	private readonly List<WorldVoice> _worldVoices = [];
	private readonly Dictionary<WorldSound, WorldVoice> _worldVoicesByNode = [];
	private readonly List<AudioStreamPlayer> _uiVoices = [];
	private readonly Dictionary<SoundKey, double> _lastPlayedAt = [];
	private readonly Dictionary<ulong, int> _lastVariantByCue = [];
	private readonly Dictionary<Node, Action> _sceneExitCallbacks = [];
	private readonly Dictionary<ulong, ulong> _sourceSceneIds = [];
	private readonly RandomNumberGenerator _random = new();
	private long _voiceSequence;
	private bool _warnedMissingListener;
	private bool _warnedMissingPool;
	private bool _warnedMissingWorldBus;
	private bool _warnedMissingUiBus;

	public int ActiveWorldVoiceCount => _worldVoices.Count;
	public int ActiveUiVoiceCount => _uiVoices.Count(voice => GodotObject.IsInstanceValid(voice) && voice.Playing);

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MaxWorldVoices = Mathf.Clamp(MaxWorldVoices, 1, 128);
		MaxUiVoices = Mathf.Clamp(MaxUiVoices, 1, MaximumUiVoiceSlots);
		_random.Randomize();
		AudioPreferences.LoadAndApply();
		CreateUiVoiceSlots();
		ValidateBus(EffectsBus, ref _warnedMissingWorldBus);
		ValidateBus(UiBus, ref _warnedMissingUiBus);

		SignalBus signalBus = SignalBus.Instance;
		if (signalBus == null)
		{
			GD.PushError("AudioController requires the SignalBus autoload.");
			return;
		}

		this.SubscribeUntilExit(
			signalBus,
			bus => bus.WorldSoundEmitted += OnWorldSoundEmitted,
			bus => bus.WorldSoundEmitted -= OnWorldSoundEmitted);
		this.SubscribeUntilExit(
			signalBus,
			bus => bus.UiSoundEmitted += OnUiSoundEmitted,
			bus => bus.UiSoundEmitted -= OnUiSoundEmitted);
		this.SubscribeUntilExit(
			signalBus,
			_ => SignalBus.CombatContactResolved += OnCombatContactResolved,
			_ => SignalBus.CombatContactResolved -= OnCombatContactResolved);
	}

	public override void _ExitTree()
	{
		foreach (KeyValuePair<Node, Action> binding in _sceneExitCallbacks.ToArray())
		{
			if (GodotObject.IsInstanceValid(binding.Key))
			{
				binding.Key.TreeExiting -= binding.Value;
			}
		}
		_sceneExitCallbacks.Clear();

		foreach (WorldVoice voice in _worldVoices.ToArray())
		{
			ReleaseWorldVoice(voice.Player);
		}
		base._ExitTree();
	}

	/// <summary>Returns a concise counter line for the audio workbench.</summary>
	public string GetVoiceSummary()
	{
		return $"World {_worldVoices.Count}/{MaxWorldVoices} · UI {ActiveUiVoiceCount}/{MaxUiVoices} · {ScenePool.GetDebugSummary()}";
	}

	/// <summary>Stops world voices tied to a scene before its nodes are freed.</summary>
	public void ReleaseScene(Node scene)
	{
		if (scene == null || !GodotObject.IsInstanceValid(scene))
		{
			return;
		}

		ulong sceneId = scene.GetInstanceId();
		if (_sceneExitCallbacks.Remove(scene, out Action callback))
		{
			scene.TreeExiting -= callback;
		}

		foreach (WorldVoice voice in _worldVoices.Where(voice => voice.SceneId == sceneId).ToArray())
		{
			ReleaseWorldVoice(voice.Player);
		}

		HashSet<ulong> expiredSources = _sourceSceneIds
			.Where(pair => pair.Value == sceneId)
			.Select(pair => pair.Key)
			.ToHashSet();
		foreach (ulong sourceId in expiredSources)
		{
			_sourceSceneIds.Remove(sourceId);
		}

		foreach (SoundKey key in _lastPlayedAt.Keys.Where(key => expiredSources.Contains(key.SourceId)).ToArray())
		{
			_lastPlayedAt.Remove(key);
		}
	}

	private void CreateUiVoiceSlots()
	{
		if (UiVoicesRoot == null)
		{
			GD.PushError("AudioController requires an explicitly wired UiVoicesRoot.");
			return;
		}

		for (int i = 0; i < MaxUiVoices; i++)
		{
			AudioStreamPlayer player = new()
			{
				Name = $"UiVoice{i + 1}",
				Bus = UiBus,
				ProcessMode = ProcessModeEnum.Always
			};
			UiVoicesRoot.AddChild(player);
			_uiVoices.Add(player);
		}
	}

	private void OnWorldSoundEmitted(SoundCueProfile cue, Node source, Vector3 position)
	{
		if (!TryValidate(cue, source, SoundCueCategory.World, out Node scene))
		{
			return;
		}

		AudioListener3D listener = GetViewport().GetAudioListener3D();
		if (listener == null || !GodotObject.IsInstanceValid(listener))
		{
			if (!_warnedMissingListener)
			{
				GD.PushWarning("World audio is disabled until the current viewport has an AudioListener3D.");
				_warnedMissingListener = true;
			}
			return;
		}

		float distance = listener.GlobalPosition.DistanceTo(position);
		if (distance > cue.MaxDistance || !CanPlayNow(cue, source))
		{
			return;
		}

		if (ScenePool.Instance == null)
		{
			if (!_warnedMissingPool)
			{
				GD.PushWarning("World audio needs ScenePool; the cue was skipped instead of playing outside the pool.");
				_warnedMissingPool = true;
			}
			return;
		}

		if (WorldSoundScene == null || WorldVoicesRoot == null)
		{
			GD.PushError("AudioController requires WorldSoundScene and WorldVoicesRoot to be wired.");
			return;
		}

		if (!TryAdmitWorldVoice(cue, source, distance))
		{
			return;
		}

		EnsureSceneRelease(scene);
		ulong sourceId = source.GetInstanceId();
		ulong cueId = cue.GetInstanceId();
		_sourceSceneIds[sourceId] = scene.GetInstanceId();
		RecordPlayed(cue, sourceId);
		AudioStream stream = SelectVariant(cue, cueId);
		float pitch = _random.RandfRange(cue.PitchMinimum, cue.PitchMaximum);
		WorldSound voice = ScenePool.Spawn<WorldSound>(WorldSoundScene, WorldVoicesRoot);
		WorldVoice record = new(voice, cue, sourceId, scene.GetInstanceId(), distance, ++_voiceSequence);
		_worldVoices.Add(record);
		_worldVoicesByNode[voice] = record;
		voice.PlayCue(stream, cue, position, pitch, OnWorldVoiceFinished);
	}

	private void OnUiSoundEmitted(SoundCueProfile cue, Node source)
	{
		if (!TryValidate(cue, source, SoundCueCategory.Ui, out _)
			|| !CanPlayNow(cue, source)
			|| _uiVoices.Count == 0)
		{
			return;
		}

		int selectedIndex = _uiVoices.FindIndex(voice => !voice.Playing);
		if (selectedIndex < 0)
		{
			selectedIndex = 0;
			for (int i = 1; i < _uiVoices.Count; i++)
			{
				if (_uiSequence[i] < _uiSequence[selectedIndex])
				{
					selectedIndex = i;
				}
			}
		}

		AudioStreamPlayer player = _uiVoices[selectedIndex];
		player.Stop();
		player.Stream = SelectVariant(cue, cue.GetInstanceId());
		player.VolumeDb = cue.GainDb;
		player.PitchScale = _random.RandfRange(cue.PitchMinimum, cue.PitchMaximum);
		player.Bus = UiBus;
		player.Play();
		_uiSequence[selectedIndex] = ++_voiceSequence;
		RecordPlayed(cue, source.GetInstanceId());
	}

	/// <summary>Plays one target-authored cue for a completed damaging or defended contact.</summary>
	private void OnCombatContactResolved(in CombatContactFeedback feedback)
	{
		if (feedback.Target == null || !GodotObject.IsInstanceValid(feedback.Target) || !feedback.Target.IsInsideTree())
		{
			return;
		}

		ImpactSurfaceProfile surface = feedback.Target.ImpactSurface;
		if (surface == null)
		{
			return;
		}

		SoundCueProfile cue = SelectContactCue(in feedback, surface);
		if (cue != null)
		{
			SignalBus.EmitWorldSoundEmitted(cue, feedback.Target, feedback.Position);
		}
	}

	private static SoundCueProfile SelectContactCue(in CombatContactFeedback feedback, ImpactSurfaceProfile surface) =>
		feedback.Outcome switch
		{
			DamageOutcome.Damaged => feedback.Context.Element != DamageElement.None
				&& surface.ElementDamageSounds != null
				&& surface.ElementDamageSounds.TryGetValue(feedback.Context.Element, out SoundCueProfile elementCue)
				? elementCue ?? surface.DamageSound
				: surface.DamageSound,
			DamageOutcome.Blocked => surface.GuardSound,
			DamageOutcome.Parried => surface.ParrySound,
			DamageOutcome.GuardBroken => surface.GuardBreakSound,
			_ => null
		};

	private readonly long[] _uiSequence = new long[MaximumUiVoiceSlots];

	private bool TryValidate(SoundCueProfile cue, Node source, SoundCueCategory expectedCategory, out Node scene)
	{
		scene = null;
		if (cue == null || source == null || !GodotObject.IsInstanceValid(source) || !source.IsInsideTree())
		{
			return false;
		}

		if (!cue.Validate(out string error))
		{
			GD.PushWarning($"Skipping invalid sound cue '{cue.ResourcePath}': {error}");
			return false;
		}

		if (cue.Category != expectedCategory)
		{
			GD.PushWarning($"Skipping sound cue '{cue.ResourcePath}' routed through the wrong presentation category.");
			return false;
		}

		if (expectedCategory == SoundCueCategory.World && !ValidateBus(EffectsBus, ref _warnedMissingWorldBus))
		{
			return false;
		}
		if (expectedCategory == SoundCueCategory.Ui && !ValidateBus(UiBus, ref _warnedMissingUiBus))
		{
			return false;
		}

		Node currentScene = GetTree().CurrentScene;
		if (currentScene == null || !(source == currentScene || currentScene.IsAncestorOf(source)))
		{
			return false;
		}

		scene = currentScene;
		return true;
	}

	private bool ValidateBus(string busName, ref bool warned)
	{
		if (AudioServer.GetBusIndex(busName) >= 0)
		{
			return true;
		}

		if (!warned)
		{
			GD.PushError($"Audio bus '{busName}' is missing. Cues will be skipped instead of routed to Master.");
			warned = true;
		}
		return false;
	}

	private bool CanPlayNow(SoundCueProfile cue, Node source)
	{
		if (cue.MinIntervalSeconds <= 0f)
		{
			return true;
		}

		double now = Time.GetTicksMsec() / 1000.0;
		SoundKey key = new(cue.GetInstanceId(), source.GetInstanceId());
		if (_lastPlayedAt.TryGetValue(key, out double lastPlayed) && now - lastPlayed < cue.MinIntervalSeconds)
		{
			return false;
		}
		return true;
	}

	private void RecordPlayed(SoundCueProfile cue, ulong sourceId)
	{
		if (cue.MinIntervalSeconds > 0f)
		{
			_lastPlayedAt[new SoundKey(cue.GetInstanceId(), sourceId)] = Time.GetTicksMsec() / 1000.0;
		}

		if (_lastPlayedAt.Count > 512)
		{
			double cutoff = Time.GetTicksMsec() / 1000.0 - 10.0;
			foreach (SoundKey key in _lastPlayedAt.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
			{
				_lastPlayedAt.Remove(key);
			}
		}
	}

	private bool TryAdmitWorldVoice(SoundCueProfile cue, Node source, float distance)
	{
		ulong cueId = cue.GetInstanceId();
		ulong sourceId = source.GetInstanceId();
		int sourceCount = 0;
		int cueCount = 0;
		foreach (WorldVoice voice in _worldVoices)
		{
			if (voice.Cue.GetInstanceId() != cueId) continue;
			cueCount++;
			if (voice.SourceId == sourceId) sourceCount++;
		}

		bool sourceLimited = sourceCount >= cue.MaxVoicesPerSource;
		bool cueLimited = cueCount >= cue.MaxVoicesGlobal;
		bool worldLimited = _worldVoices.Count >= MaxWorldVoices;
		if (!sourceLimited && !cueLimited && !worldLimited)
		{
			return true;
		}

		WorldVoice victim = null;
		for (int i = 0; i < _worldVoices.Count; i++)
		{
			WorldVoice voice = _worldVoices[i];
			bool isCandidate = sourceLimited
				? voice.Cue.GetInstanceId() == cueId && voice.SourceId == sourceId
				: cueLimited ? voice.Cue.GetInstanceId() == cueId : true;
			if (!isCandidate || !(cue.Priority > voice.Cue.Priority
				|| (cue.Priority == voice.Cue.Priority && distance < voice.Distance))) continue;

			if (victim == null
				|| voice.Cue.Priority < victim.Cue.Priority
				|| (voice.Cue.Priority == victim.Cue.Priority && voice.Distance > victim.Distance)
				|| (voice.Cue.Priority == victim.Cue.Priority && voice.Distance == victim.Distance
					&& voice.Sequence < victim.Sequence))
			{
				victim = voice;
			}
		}

		if (victim == null)
		{
			return false;
		}

		ReleaseWorldVoice(victim.Player);
		return true;
	}

	private AudioStream SelectVariant(SoundCueProfile cue, ulong cueId)
	{
		int count = cue.Variants.Length;
		int selected = _random.RandiRange(0, count - 1);
		if (count > 1 && _lastVariantByCue.TryGetValue(cueId, out int last) && selected == last)
		{
			selected = (selected + 1) % count;
		}
		_lastVariantByCue[cueId] = selected;
		return cue.Variants[selected];
	}

	private void EnsureSceneRelease(Node scene)
	{
		if (_sceneExitCallbacks.ContainsKey(scene))
		{
			return;
		}

		Action callback = () => ReleaseScene(scene);
		_sceneExitCallbacks[scene] = callback;
		scene.TreeExiting += callback;
	}

	private void OnWorldVoiceFinished(WorldSound voice)
	{
		ReleaseWorldVoice(voice);
	}

	private void ReleaseWorldVoice(WorldSound voice)
	{
		if (voice == null || !GodotObject.IsInstanceValid(voice) || !_worldVoicesByNode.Remove(voice, out WorldVoice record))
		{
			return;
		}

		_worldVoices.Remove(record);
		ScenePool.Despawn(voice);
	}

	private readonly record struct SoundKey(ulong CueId, ulong SourceId);

	private sealed record WorldVoice(
		WorldSound Player,
		SoundCueProfile Cue,
		ulong SourceId,
		ulong SceneId,
		float Distance,
		long Sequence);
}