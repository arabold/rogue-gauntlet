using Godot;
using System.Collections.Generic;
using System.Linq;
using static EffectPreviewControls;

/// <summary>One subject and one production effect, with simple replay and viewing controls.</summary>
public partial class ActorItemEffectPreview : Node3D
{
	[Export] public WorldEffectDefinition[] Effects { get; set; } = [];
	[Export] public WorldEffectDefinition TeleportDeparture { get; set; }
	[Export] public WorldEffectDefinition TeleportArrival { get; set; }
	[Export] public Godot.Collections.Dictionary<string, PackedScene> Subjects { get; set; } = [];
	[Export] public PackedScene ItemHostScene { get; set; }
	[Export] public GridMap FloorTiles { get; set; }
	[Export] public Node3D Stage { get; set; }
	[Export] public Camera3D Camera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	public int SelectedEffect { get; private set; } = 2;
	public int SelectedSubject { get; private set; }
	public string LastResult { get; private set; } = "";
	public int InstanceCount => _host?.ActiveInstanceCount ?? 0;
	private const uint PreviewSeed = 42;
	private readonly List<AnimationPlayer> _animations = [];
	private WorldEffectHost _host;
	private Node3D _subject;
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
		DebugStage.PopulateFloor(FloorTiles);
		BuildControls(); Rebuild();
	}
	private void BuildControls()
	{
		Controls.AddChild(new Label { Text = "ACTOR / ITEM EFFECTS" });
		_selection = Choice(Effects.Select(e => e.ResourceName.Length > 0 ? e.ResourceName : e.Id.ToString()), SelectedEffect, SelectEffect); Row(Controls, "Effect", _selection);
		_subjectSelection = Choice(Subjects.Keys, 0, SetSubject); Row(Controls, "Subject", _subjectSelection);
		_poseChoice = Choice(new[] { "Idle", "Locomotion", "Attack" }, 0, SetAnimation); Row(Controls, "Pose", _poseChoice);
		_facetChoice = new CheckBox { Text = "Faceted shading" }; _facetChoice.Toggled += enabled => { _faceted = enabled; ApplySurfaceStyle(); }; Controls.AddChild(_facetChoice);
		_polishChoice = new CheckBox { Text = "Polished steel" }; _polishChoice.Toggled += enabled => { _polished = enabled; ApplySurfaceStyle(); }; Controls.AddChild(_polishChoice);
		Button(Controls, "Replay", Replay);
		Button(Controls, "Fade effect", FadeEffects);
		Button(Controls, "Clear effect", ClearEffects);
		_teleportButton = Button(Controls, "Preview full teleport", PreviewTeleport);
		Playback(Controls, SetPlaybackSpeed);
		var dark = new CheckBox { Text = "Dark lighting" }; dark.Toggled += SetDarkLighting; Controls.AddChild(dark);
		CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	public void SelectEffect(int index)
	{
		if (index < 0 || index >= Effects.Length) return;
		SelectedEffect = index; _selection.Selected = index; Rebuild();
	}
	public void SetSubject(int index)
	{
		if (index < 0 || index >= Subjects.Count) return;
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
		_host = null; _animations.Clear(); _teleported = false;
		EffectPreviewStage.ClearChildren(Stage);
		_subject = Subjects.Values.ElementAt(SelectedSubject).Instantiate<Node3D>();
		EffectPreviewStage.AddSubject(_subject, Stage, _animations);
		_host = _subject switch { Player player => player.WorldEffects, EnemyBase actor => actor.WorldEffects, _ => null };
		bool actorSubject = _host != null;
		_poseChoice.Disabled = !actorSubject;
		_teleportButton.Visible = actorSubject && (Effects[SelectedEffect] == TeleportDeparture || Effects[SelectedEffect] == TeleportArrival);
		if (_host == null)
		{
			_subject.Position += Vector3.Up;
			_host = ItemHostScene.Instantiate<WorldEffectHost>(); _subject.AddChild(_host);
		}
		_host.ProcessMode = ProcessModeEnum.Always; _host.PlaybackSpeed = _speed;
		PlayAnimations();
		var effect = Effects[SelectedEffect];
		var result = effect.Lifetime == WorldEffectLifetime.Burst
			? _host.PlayBurst(effect, PreviewSeed)
			: _host.SetSustained(this, effect, true, PreviewSeed, instance => instance is not ItemCurseEffect curse || curse.Configure(_subject));
		LastResult = result == WorldEffectResult.Rejected ? "This effect needs a compatible subject." : "";
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
	private void PlayAnimations() => EffectPreviewStage.PlayAnimations(_animations, _animation, _speed);
	/// <summary>Replay rebuilds the single subject and its selected burst or sustained effect.</summary>
	public void Replay() => Rebuild();
	public void ClearEffects() => _host?.Clear();
	/// <summary>Release active effects through their normal exit envelopes for visual inspection.</summary>
	public void FadeEffects() => _host?.Clear(immediate: false);
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
