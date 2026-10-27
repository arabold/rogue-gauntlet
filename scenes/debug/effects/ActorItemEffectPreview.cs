using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>One subject and one production effect, with simple replay and viewing controls.</summary>
public partial class ActorItemEffectPreview : Node3D
{
	[Export] public WorldEffectDefinition[] Effects { get; set; } = [];
	[Export] public WorldEffectDefinition TeleportDeparture { get; set; }
	[Export] public WorldEffectDefinition TeleportArrival { get; set; }
	[Export] public PackedScene[] Subjects { get; set; } = [];
	[Export] public PackedScene ItemHostScene { get; set; }
	[Export] public GridMap FloorTiles { get; set; }
	[Export] public Node3D Stage { get; set; }
	[Export] public Camera3D Camera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public PackedScene CursePresentation { get; set; }
	[Export] public EquipableItem[] ItemDefinitions { get; set; } = [];
	[Export] public WorldEnvironment Lighting { get; set; }
	public int SelectedEffect { get; private set; } = 2;
	public int SelectedSubject { get; private set; }
	public string LastResult { get; private set; } = "";
	public int InstanceCount => _host?.ActiveInstanceCount ?? 0;
	private const uint PreviewSeed = 42;
	private readonly List<AnimationPlayer> _animations = [];
	private WorldEffectHost _host;
	private Node3D _subject;
	private EquipableItem _item;
	private OptionButton _selection, _subjectSelection, _poseChoice;
	private Button _teleportButton;
	private CheckBox _facetChoice, _polishChoice;
	private bool _faceted, _polished;
	private Label _status;
	private bool _teleported;
	private int _animation;
	private float _speed = 1;
	public override void _Ready()
	{
		for (int x = -8; x <= 8; x += 4) for (int z = -8; z <= 8; z += 4) FloorTiles.SetCellItem(new Vector3I(x, 0, z), 0);
		BuildControls(); Rebuild();
	}
	private void BuildControls()
	{
		Controls.AddChild(new Label { Text = "ACTOR / ITEM EFFECTS" });
		_selection = Choice(Effects.Select(e => e.ResourceName.Length > 0 ? e.ResourceName : e.Id.ToString()), SelectedEffect, SelectEffect); Row("Effect", _selection);
		_subjectSelection = Choice(new[] { "Player", "Skeleton", "Warden", "Sword", "Ring" }, 0, SetSubject); Row("Subject", _subjectSelection);
		_poseChoice = Choice(new[] { "Idle", "Locomotion", "Attack" }, 0, SetAnimation); Row("Pose", _poseChoice);
		_facetChoice = new CheckBox { Text = "Faceted shading" }; _facetChoice.Toggled += enabled => { _faceted = enabled; ApplySurfaceStyle(); }; Controls.AddChild(_facetChoice);
		_polishChoice = new CheckBox { Text = "Polished steel" }; _polishChoice.Toggled += enabled => { _polished = enabled; ApplySurfaceStyle(); }; Controls.AddChild(_polishChoice);
		Button("Replay", Replay);
		Button("Clear effect", ClearEffects);
		_teleportButton = Button("Preview full teleport", PreviewTeleport);
		Row("Playback", Choice(new[] { "Paused", "0.25×", "0.5×", "1×" }, 3, i => SetPlaybackSpeed(new[] { 0f, .25f, .5f, 1f }[i])));
		var dark = new CheckBox { Text = "Dark lighting" }; dark.Toggled += SetDarkLighting; Controls.AddChild(dark);
		Controls.AddChild(new Label { Text = "Right-drag to orbit · wheel zoom\nCamera keys follow game bindings.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	private OptionButton Choice(IEnumerable<string> values, int selected, Action<int> callback)
	{
		var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (var value in values) choice.AddItem(value);
		choice.Selected = selected; choice.ItemSelected += i => callback((int)i); return choice;
	}
	private void Row(string label, Control value)
	{
		var row = new HBoxContainer(); row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100, 0) }); row.AddChild(value); Controls.AddChild(row);
	}
	private Button Button(string label, Action callback)
	{
		var button = new Button { Text = label, FocusMode = Control.FocusModeEnum.None }; button.Pressed += callback; Controls.AddChild(button); return button;
	}
	public void SelectEffect(int index)
	{
		if (index < 0 || index >= Effects.Length) return;
		SelectedEffect = index; _selection.Selected = index; Rebuild();
	}
	public void SetSubject(int index)
	{
		if (index < 0 || index >= Subjects.Length) return;
		SelectedSubject = index; _subjectSelection.Selected = index; Rebuild();
	}
	public void SetAnimation(int index) { _animation = Mathf.Clamp(index, 0, 2); PlayAnimations(); }
	public void SetDarkLighting(bool dark) { Lighting.Environment.AmbientLightEnergy = dark ? .06f : .65f; KeyLight.LightEnergy = dark ? .12f : 1.2f; }
	public void SetPlaybackSpeed(float speed)
	{
		_speed = Mathf.Clamp(speed, 0, 1);
		if (_host != null) _host.PlaybackSpeed = _speed;
		foreach (var animation in _animations) animation.SpeedScale = _speed;
	}
	public void Rebuild()
	{
		_host = null; _item = null; _animations.Clear(); _teleported = false;
		foreach (var child in Stage.GetChildren()) { Stage.RemoveChild(child); child.QueueFree(); }
		_subject = Subjects[SelectedSubject].Instantiate<Node3D>(); _subject.ProcessMode = ProcessModeEnum.Disabled;
		if (_subject is CharacterBody3D body) { body.CollisionLayer = body.CollisionMask = 0; body.Rotation = Vector3.Up * Mathf.Pi; }
		Stage.AddChild(_subject);
		if (_subject is EnemyBase enemy) enemy.DeathComponent.LootTableComponent = null;
		_subject.Visible = true; _subject.RemoveFromGroup("player"); _subject.RemoveFromGroup("enemy");
		_host = _subject switch { Player player => player.WorldEffects, EnemyBase actor => actor.WorldEffects, _ => null };
		bool actorSubject = _host != null;
		_poseChoice.Disabled = !actorSubject;
		_teleportButton.Visible = actorSubject && (Effects[SelectedEffect] == TeleportDeparture || Effects[SelectedEffect] == TeleportArrival);
		if (_host == null)
		{
			_subject.Position += Vector3.Up;
			if (Effects[SelectedEffect].Id == "cursed_item" && CursePresentation != null)
			{
				_item = (EquipableItem)ItemFactory.CopyDefinition(ItemDefinitions[SelectedSubject - 3]);
				_item.CursePenalty = [new StatModifier()]; _item.CurseStatusKnown = true;
				var presenter = CursePresentation.Instantiate<ItemCurseWorldEffectPresenter>();
				presenter.Initialize(_item, _subject); _subject.AddChild(presenter); _host = presenter.Host;
			}
			else { _host = ItemHostScene.Instantiate<WorldEffectHost>(); _subject.AddChild(_host); }
		}
		_host.ProcessMode = ProcessModeEnum.Always; _host.PlaybackSpeed = _speed;
		foreach (var node in _subject.FindChildren("*", "", true, false))
		{
			if (node is MeshInstance3D mesh && !VisualHelper.IsHelper(mesh) && actorSubject) mesh.Layers = _subject is Player ? 1u << 2 : 1u << 3;
			if (node is Label3D label) { label.Visible = false; label.Layers = VisualHelper.DebugRenderLayer; }
			if (node is Sprite3D sprite) { sprite.Visible = false; sprite.Layers = VisualHelper.DebugRenderLayer; }
			if (node is AnimationTree tree) tree.Active = false;
			if (node is AnimationPlayer animation) { animation.ProcessMode = ProcessModeEnum.Always; _animations.Add(animation); }
			if (node is Light3D light && !VisualHelper.IsInHelperSubtree(light)) light.Visible = false;
		}
		PlayAnimations();
		var effect = Effects[SelectedEffect];
		if (_item == null)
		{
			var result = effect.Lifetime == WorldEffectLifetime.Burst ? _host.PlayBurst(effect, PreviewSeed) : _host.SetSustained(this, effect, true, PreviewSeed);
			LastResult = result == WorldEffectResult.Rejected ? "This effect needs a compatible subject." : "";
		}
		else LastResult = "";
		_facetChoice.Visible = Effects[SelectedEffect].Id == "iron_skin" || Effects[SelectedEffect].Id == "stone_skin";
		_polishChoice.Visible = Effects[SelectedEffect].Id == "iron_skin";
		ApplySurfaceStyle(); _status.Text = LastResult;
	}
	/// <summary>Change only the preview's private shader copies; the original meshes and shared definitions stay intact.</summary>
	public void SetSurfaceStyle(bool faceted, bool polished)
	{
		_faceted = faceted; _polished = polished;
		_facetChoice.SetPressedNoSignal(faceted); _polishChoice.SetPressedNoSignal(polished); ApplySurfaceStyle();
	}
	private void ApplySurfaceStyle()
	{
		if (_subject == null || !_facetChoice.Visible) return;
		foreach (var mesh in _subject.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				if (mesh.GetActiveMaterial(i) is ShaderMaterial material && material.GetShaderParameter("skin_kind").VariantType == Variant.Type.Int)
				{ material.SetShaderParameter("faceted", _faceted); material.SetShaderParameter("polish", _polished ? 1f : 0f); }
	}
	private void PlayAnimations()
	{
		string[] fragments = _animation switch { 1 => new[] { "Running", "Run", "Walk" }, 2 => new[] { "Melee_Attack", "Attack", "Spellcast" }, _ => new[] { "Idle" } };
		foreach (var animation in _animations)
			foreach (string fragment in fragments)
			{
				var clip = animation.GetAnimationList().FirstOrDefault(n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase));
				if (clip == null) continue;
				animation.Play(clip); animation.SpeedScale = _speed; break;
			}
	}
	/// <summary>Replay rebuilds the one fixture, including sustained effects and known item curses.</summary>
	public void Replay() => Rebuild();
	public void ClearEffects()
	{
		if (_item != null) _item.CurseStatusKnown = false;
		_host?.Clear();
	}
	/// <summary>Exercise both ends only when teleportation is selected.</summary>
	public void PreviewTeleport()
	{
		if (_subject is not Player and not EnemyBase) return;
		ClearEffects();
		Vector3 destination = _subject.GlobalPosition + Vector3.Right * (_teleported ? -3 : 3);
		TeleportEffect.Play(_subject, _host, destination, TeleportDeparture, TeleportArrival, PreviewSeed);
		_teleported = !_teleported;
	}
}