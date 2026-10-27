using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
		_selection = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (var scroll in _scrolls) _selection.AddItem(scroll.Name);
		_selection.ItemSelected += i => SelectScroll((int)i); Row("Scroll", _selection);
		Controls.AddChild(new Label { Text = "Assigned scroll appearance" }); Controls.MoveChild(PaperPreview, Controls.GetChildCount() - 1);
		Button("Replay", Replay);
		_speedChoice = new OptionButton(); foreach (string label in new[] { "Paused", "0.25×", "0.5×", "1×" }) _speedChoice.AddItem(label);
		_speedChoice.Selected = 3; _speedChoice.ItemSelected += i => SetSpeed(new[] { 0f, .25f, .5f, 1f }[(int)i]); Row("Playback", _speedChoice);
		_playhead = new HSlider { MinValue = 0, MaxValue = .9, Step = .01, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_playhead.ValueChanged += v => SetTime((float)v); Row("Still frame", _playhead);
		var dark = new CheckBox { Text = "Dark lighting" }; dark.Toggled += SetDark; Controls.AddChild(dark);
		Controls.AddChild(new Label { Text = "Right-drag to orbit · wheel zoom\nCamera keys follow game bindings.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	private void Row(string text, Control control)
	{
		var row = new HBoxContainer(); row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(100, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); Controls.AddChild(row);
	}
	private void Button(string text, Action action)
	{
		var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None }; button.Pressed += action; Controls.AddChild(button);
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
	private static void ClearChildren(Node root) { foreach (var child in root.GetChildren()) { root.RemoveChild(child); child.QueueFree(); } }
	public void Rebuild()
	{
		RuneRing = null; _animations.Clear(); ClearChildren(Stage); ClearChildren(PaperRoot);
		Appearance = _identity.GetAppearance(SelectedScroll); PackedScene paper = Appearance?.GetModel(SelectedScroll.AppearanceForm);
		if (paper == null || Appearance.GlyphMesh == null) { LastResult = "Scroll appearance has no assigned glyph."; _status.Text = LastResult; return; }
		PaperRoot.AddChild(paper.Instantiate<Node3D>());
		var column = new Node3D(); Stage.AddChild(column);
		var actor = PlayerScene.Instantiate<Player>(); actor.ProcessMode = ProcessModeEnum.Disabled;
		actor.CollisionLayer = actor.CollisionMask = 0; actor.Rotation = Vector3.Up * Mathf.Pi; column.AddChild(actor); actor.RemoveFromGroup("player");
		foreach (var child in actor.FindChildren("*", "", true, false))
		{
			if (child is AnimationTree tree) tree.Active = false;
			if (child is Light3D light && !VisualHelper.IsInHelperSubtree(light)) light.Visible = false;
			if (child is AnimationPlayer animation)
			{
				var idle = animation.GetAnimationList().FirstOrDefault(n => n.Contains("Idle", StringComparison.OrdinalIgnoreCase));
				if (idle != null) { animation.ProcessMode = ProcessModeEnum.Always; animation.Play(idle); animation.SpeedScale = _speed; _animations.Add(animation); }
			}
		}
		var host = EffectHostScene.Instantiate<WorldEffectHost>(); host.PlaybackSpeed = 0; column.AddChild(host);
		host.PlayBurstConfigured(ReadingEffect, 42, instance => { if (instance is not RuneRingEffect ring) return false; RuneRing = ring; return ring.Configure(Appearance); });
		LastResult = Appearance.Descriptor; _status.Text = LastResult; _clock = _speed == 0 ? .275f : 0; ApplyFrame();
	}
	private void ApplyFrame() { RuneRing?.Seek(_clock); _playhead?.SetValueNoSignal(Mathf.Clamp(_clock, 0, .9f)); }
	public override void _Process(double delta) { _clock += (float)delta * _speed; ApplyFrame(); }
}