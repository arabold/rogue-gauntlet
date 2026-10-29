using Godot;
using System.Collections.Generic;
using System.Linq;
using static EffectPreviewControls;

/// <summary>Preview the production scroll-reading effect beside its assigned paper appearance.</summary>
public partial class ScrollRuneStudy : Node3D
{
	[Export] public IdentityCatalog Catalog { get; set; }
	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public Node3D Stage { get; set; }
	[Export] public SubViewportContainer PaperPreview { get; set; }
	[Export] public Node3D PaperRoot { get; set; }
	[Export] public Camera3D Camera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public PackedScene EffectHostScene { get; set; }
	[Export] public WorldEffectDefinition ReadingEffect { get; set; }
	public ItemAppearance Appearance { get; private set; }
	public Scroll SelectedScroll => _scrolls[_selected];
	public ulong RunSeedUsed { get; private set; } = 42;
	public string LastResult { get; private set; } = "";
	public Mesh MatchedGlyph => Appearance?.GlyphMesh;
	public RuneRingEffect RuneRing { get; private set; }
	public OmniLight3D RingLight => RuneRing?.AccentLight;
	public int RingGlyphCount => RuneRing?.Glyphs.Length ?? 0;
	private readonly IdentificationService _identity = new();
	private readonly List<AnimationPlayer> _animations = [];
	private Scroll[] _scrolls = [];
	private OptionButton _selection, _speedChoice;
	private HSlider _playhead;
	private Label _status;
	private int _selected;
	private float _clock, _speed = 1;
	public override void _Ready()
	{
		_scrolls = Catalog.Categories.First(c => c.Category == "scroll").Types.OfType<Scroll>().ToArray();
		_identity.Initialize(RunSeedUsed); BuildControls();
		if (GameSession.Instance?.Run != null) UseSessionAssignment(); else Rebuild();
	}
	private void BuildControls()
	{
		Controls.AddChild(new Label { Text = "SCROLL READING" });
		_selection = Choice(_scrolls.Select(scroll => scroll.Name), 0, SelectScroll); Row(Controls, "Scroll", _selection);
		Controls.AddChild(new Label { Text = "Assigned scroll appearance" }); Controls.MoveChild(PaperPreview, Controls.GetChildCount() - 1);
		Button(Controls, "Replay", Replay);
		_speedChoice = Playback(Controls, SetSpeed);
		_playhead = new HSlider { MinValue = 0, MaxValue = .9, Step = .01, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_playhead.ValueChanged += v => SetTime((float)v); Row(Controls, "Still frame", _playhead);
		var dark = new CheckBox { Text = "Dark lighting" }; dark.Toggled += SetDark; Controls.AddChild(dark);
		CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	public void SelectScroll(int index)
	{
		if (index < 0 || index >= _scrolls.Length) return;
		_selected = index; _selection.Selected = index; Rebuild();
	}
	/// <summary>Preview/test command using the same assignment algorithm; it does not mutate the current run.</summary>
	public void SetSeed(uint seed) { RunSeedUsed = seed; _identity.Initialize(seed); Rebuild(); }
	public void UseSessionAssignment()
	{
		RunState run = GameSession.Instance?.Run; if (run == null) return;
		RunSeedUsed = run.RunSeed; _identity.Initialize(run.RunSeed, persistedAssignments: run.Identification.GetAssignmentDescriptors()); Rebuild();
	}
	public void Replay() { _clock = 0; foreach (var animation in _animations) animation.Seek(0, true); ApplyFrame(); }
	public void SetSpeed(float speed) { _speed = Mathf.Clamp(speed, 0, 1); foreach (var animation in _animations) animation.SpeedScale = _speed; }
	public void SetTime(float time) { SetSpeed(0); _speedChoice.Selected = 0; _clock = Mathf.Clamp(time, 0, .9f); ApplyFrame(); }
	public void SetDark(bool dark) { Lighting.Environment.AmbientLightEnergy = dark ? .07f : .55f; KeyLight.LightEnergy = dark ? .16f : 1.1f; }
	public void Rebuild()
	{
		RuneRing = null; _animations.Clear(); EffectPreviewStage.ClearChildren(Stage); EffectPreviewStage.ClearChildren(PaperRoot);
		Appearance = _identity.GetAppearance(SelectedScroll); PackedScene paper = Appearance?.GetModel(SelectedScroll.AppearanceForm);
		if (paper == null || Appearance.GlyphMesh == null) { LastResult = "Scroll appearance has no assigned glyph."; _status.Text = LastResult; return; }
		PaperRoot.AddChild(paper.Instantiate<Node3D>());
		var column = new Node3D(); Stage.AddChild(column);
		var actor = PlayerScene.Instantiate<Player>();
		EffectPreviewStage.AddSubject(actor, column, _animations);
		EffectPreviewStage.PlayAnimations(_animations, 0, _speed);
		var host = EffectHostScene.Instantiate<WorldEffectHost>(); host.PlaybackSpeed = 0; column.AddChild(host);
		host.PlayBurstConfigured(ReadingEffect, 42, instance => { if (instance is not RuneRingEffect ring) return false; RuneRing = ring; return ring.Configure(Appearance); });
		LastResult = Appearance.Descriptor; _status.Text = LastResult; _clock = _speed == 0 ? .275f : 0; ApplyFrame();
	}
	private void ApplyFrame() { RuneRing?.Seek(_clock); _playhead?.SetValueNoSignal(Mathf.Clamp(_clock, 0, .9f)); }
	public override void _Process(double delta)
	{
		if (_speed <= 0 || RuneRing == null || _clock >= RuneRing.Duration) return;
		_clock = Mathf.Min(_clock + (float)delta * _speed, RuneRing.Duration); ApplyFrame();
	}
}