using Godot;
using System;

/// <summary>
/// Lets designers replay production cues at fixed world positions and watch live voice counts.
/// </summary>
public partial class AudioHarness : Node3D
{
	[Export] public SoundCueProfile[] Cues { get; set; } = [];
	[Export] public SoundCueProfile StressCue { get; set; }

	private AudioController _controller;
	private MusicController _musicController;
	private AudioListener3D _listener;
	private Node3D _leftSource;
	private Node3D _centerSource;
	private Node3D _rightSource;
	private OptionButton _cuePicker;
	private Label _voiceCounts;
	private Label _musicStatus;
	private int _selectedCueIndex;
	private double _statusRefreshRemaining;

	public override void _Ready()
	{
		_controller = GetNode<AudioController>("/root/GameSession/AudioController");
		_musicController = _controller.GetNode<MusicController>("MusicController");
		_listener = GetNode<AudioListener3D>("AudioListener");
		_listener.MakeCurrent();

		Camera3D camera = GetNode<Camera3D>("Camera3D");
		camera.LookAt(Vector3.Zero, Vector3.Up);
		_listener.GlobalTransform = new Transform3D(camera.GlobalBasis, new Vector3(0f, 2f, 10f));

		_leftSource = CreateMarker("LeftMarker", new Vector3(-4f, 0f, 0f), new Color(0.9f, 0.3f, 0.25f));
		_centerSource = CreateMarker("CenterMarker", new Vector3(0f, 0f, 0f), new Color(0.95f, 0.78f, 0.25f));
		_rightSource = CreateMarker("RightMarker", new Vector3(4f, 0f, 0f), new Color(0.25f, 0.65f, 0.95f));
		BuildControls();
	}

	public override void _Process(double delta)
	{
		_statusRefreshRemaining -= delta;
		if (_statusRefreshRemaining > 0) return;
		_statusRefreshRemaining = 0.2;

		if (_controller != null && GodotObject.IsInstanceValid(_controller))
		{
			string summary = _controller.GetVoiceSummary();
			if (_voiceCounts.Text != summary) _voiceCounts.Text = summary;
		}
		if (_musicController != null && GodotObject.IsInstanceValid(_musicController))
		{
			string status = $"Music: {_musicController.CurrentContext}";
			if (_musicStatus.Text != status) _musicStatus.Text = status;
		}
	}

	private Node3D CreateMarker(string markerName, Vector3 position, Color color)
	{
		Node3D source = new() { Name = markerName, Position = position };
		AddChild(source);
		MeshInstance3D mesh = new()
		{
			Name = "Marker",
			Mesh = new SphereMesh { Radius = 0.28f, Height = 0.56f },
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = color
			}
		};
		source.AddChild(mesh);
		Label3D label = new()
		{
			Name = "Label",
			Text = markerName.Replace("Marker", ""),
			Position = new Vector3(0f, 0.6f, 0f),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
		};
		source.AddChild(label);
		return source;
	}

	private void BuildControls()
	{
		CanvasLayer canvas = new() { Name = "Controls", ProcessMode = ProcessModeEnum.Always };
		AddChild(canvas);
		PanelContainer panel = new() { Position = new Vector2(24f, 24f), CustomMinimumSize = new Vector2(460f, 0f) };
		canvas.AddChild(panel);
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 18);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 18);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		panel.AddChild(margin);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 8);
		margin.AddChild(layout);

		Label title = new() { Text = "Audio Playback Workbench" };
		layout.AddChild(title);
		_cuePicker = new OptionButton();
		for (int i = 0; i < Cues.Length; i++)
		{
			string cueName = string.IsNullOrEmpty(Cues[i].ResourceName)
				? System.IO.Path.GetFileNameWithoutExtension(Cues[i].ResourcePath)
				: Cues[i].ResourceName;
			_cuePicker.AddItem($"{cueName} · {Cues[i].Category}", i);
		}
		_cuePicker.ItemSelected += index => _selectedCueIndex = (int)index;
		layout.AddChild(_cuePicker);

		HBoxContainer playback = new();
		layout.AddChild(playback);
		AddButton(playback, "UI", button => PlayUi(button));
		AddButton(playback, "World left", _ => PlayWorld(_leftSource));
		AddButton(playback, "World center", _ => PlayWorld(_centerSource));
		AddButton(playback, "World right", _ => PlayWorld(_rightSource));

		HBoxContainer music = new();
		layout.AddChild(music);
		AddButton(music, "Exploration music", _ => _musicController.PlayExploration());
		AddButton(music, "Boss music", _ => _musicController.PlayBoss());
		AddButton(music, "Fade music", _ => _musicController.StopMusic());
		_musicStatus = new Label { Text = "Music: Silent" };
		layout.AddChild(_musicStatus);

		HBoxContainer tools = new();
		layout.AddChild(tools);
		AddButton(tools, "Stress nearest voice", _ => StressWorldVoices());
		AddButton(tools, GetTree().Paused ? "Resume" : "Pause", button => TogglePause(button));
		_voiceCounts = new Label { Text = "World 0 · UI 0" };
		layout.AddChild(_voiceCounts);
		Label instructions = new()
		{
			Text = "Compare positional cues and exploration/boss music fades. Pause behavior and voice limits are shown below; UI playback remains centered.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		layout.AddChild(instructions);
	}

	private void AddButton(Control parent, string text, Action<Button> callback)
	{
		Button button = new() { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		parent.AddChild(button);
		button.Pressed += () => callback(button);
	}

	private void PlayUi(Button source)
	{
		SoundCueProfile cue = SelectedCue;
		if (cue != null && cue.Category == SoundCueCategory.Ui)
		{
			SignalBus.EmitUiSoundEmitted(cue, source);
		}
	}

	private void PlayWorld(Node3D source)
	{
		SoundCueProfile cue = SelectedCue;
		if (cue != null && cue.Category == SoundCueCategory.World)
		{
			SignalBus.EmitWorldSoundEmitted(cue, source, source.GlobalPosition);
		}
	}

	private void StressWorldVoices()
	{
		if (StressCue == null || StressCue.Category != SoundCueCategory.World)
		{
			return;
		}

		for (int i = 0; i < 36; i++)
		{
			float remainingDistance = 22f - i * 0.55f;
			float side = i % 2 == 0 ? -1.5f : 1.5f;
			Vector3 position = _listener.GlobalPosition + _listener.GlobalBasis * new Vector3(side, -1.8f, -remainingDistance);
			Node3D source = new() { Name = $"StressSource{i + 1}", Position = position };
			GetTree().CurrentScene.AddChild(source);
			SignalBus.EmitWorldSoundEmitted(StressCue, source, position);
			source.QueueFree();
		}
	}

	private void TogglePause(Button button)
	{
		GetTree().Paused = !GetTree().Paused;
		button.Text = GetTree().Paused ? "Resume" : "Pause";
	}

	private SoundCueProfile SelectedCue => _selectedCueIndex >= 0 && _selectedCueIndex < Cues.Length
		? Cues[_selectedCueIndex]
		: null;
}