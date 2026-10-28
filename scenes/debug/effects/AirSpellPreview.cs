using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Visual-only studies of the five air spells with stationary targets and wall fixtures.</summary>
public partial class AirSpellPreview : Node3D
{
	[Export] public Node3D Caster { get; set; }
	[Export] public PackedScene TargetVisual { get; set; }
	[Export] public PackedScene PlayerVisual { get; set; }
	[Export] public VBoxContainer Controls { get; set; }
	[Export] public DebugStage DebugStage { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public PackedScene ArcScene { get; set; }
	[Export] public PackedScene LightningBallScene { get; set; }
	[Export] public PreviewTargetStrategy[] TargetStrategies { get; set; } = [];
	[Export] public PackedScene GustScene { get; set; }
	[Export] public PackedScene ThunderstormScene { get; set; }
	[Export] public PackedScene LightningCracksScene { get; set; }
	[Export] public PackedScene GuardScene { get; set; }
	private static readonly string[] Names = ["Ball Lightning", "Chain Lightning", "Gust", "Thunderstorm", "Storm Guard"];
	private readonly List<Node3D> _targets = [];
	private readonly List<AnimationPlayer> _animations = [];
	private int _level = 1, _count = 3, _selected, _guardHits;
	private ulong _seed = 1;
	private VisualEffectInstance _guard;
	private Button _cast, _reflect, _clear;
	private Label _status;

	public override void _Ready()
	{
		EffectPreviewStage.AddSubject(PlayerVisual.Instantiate<Node3D>(), Caster, _animations);
		EffectPreviewControls.Row(Controls, "Arrangement", EffectPreviewControls.Choice(DebugStage.ArrangementNames, 0, DebugStage.SetArrangement));
		EffectPreviewControls.Row(Controls, "Spell level", EffectPreviewControls.Choice(new[] { "1", "2", "3" }, 0, i => SetLevel(i + 1)));
		EffectPreviewControls.Row(Controls, "Targets", EffectPreviewControls.Choice(new[] { "0", "1", "2", "3", "4", "5" }, 3, SetTargetCount));
		EffectPreviewControls.Row(Controls, "Spell", EffectPreviewControls.Choice(Names, 0, SelectSpell));
		_cast = EffectPreviewControls.Button(Controls, "Cast Ball Lightning", Cast);
		EffectPreviewControls.Button(Controls, "Reset targets", ResetTargets);
		_reflect = EffectPreviewControls.Button(Controls, "Preview reflection", Reflect); _reflect.Visible = false;
		_clear = EffectPreviewControls.Button(Controls, "Clear guard", ClearGuard); _clear.Visible = false;
		EffectPreviewControls.Playback(Controls, speed => Engine.TimeScale = speed);
		EffectPreviewControls.DarkLighting(Controls, Lighting, KeyLight);
		EffectPreviewControls.CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
		ResetTargets();
	}
	public override void _ExitTree() => Engine.TimeScale = 1;
	private void ResetTargets()
	{
		EffectPreviewStage.ResetSpellTargets(this, TargetVisual, _targets, _animations, _count, true);
		_status.Text = "Choose a spell and level, then cast.";
	}
	public void SetTargetCount(int count) { _count = Mathf.Clamp(count, 0, 5); ResetTargets(); }
	public void SetLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public void SelectSpell(int index)
	{
		if (index < 0 || index >= Names.Length) return;
		_selected = index; _cast.Text = $"Cast {Names[index]}"; _reflect.Visible = _clear.Visible = index == 4;
	}
	public void Cast()
	{
		PackedScene required = _selected switch { 0 => LightningBallScene, 2 => GustScene, 3 => ThunderstormScene, 4 => GuardScene, _ => ArcScene };
		if (required == null || (_selected == 3 && (ArcScene == null || LightningCracksScene == null)) || (_selected < 2 && (TargetStrategies.Length <= _selected || TargetStrategies[_selected] == null))) { _status.Text = "Missing preview effect scene."; return; }
		if (_selected == 4)
		{
			ClearGuard();
			var guard = GuardScene.Instantiate<StormGuardVisual>();
			guard.Configure(_level, _seed++); guard.Position = Caster.GlobalPosition; AddChild(guard);
			guard.SetAreaVisibility(AreaVisibility.Capture(GetWorld3D(), Caster.GlobalPosition, 6));
			_guard = guard; _guardHits = 3 + _level;
			_status.Text = $"Storm Guard: {_guardHits} preview hits remaining."; return;
		}
		Vector3 origin = Caster.GlobalPosition;
		float radius = 3.5f + (_level - 1) * 1.5f;
		var visibility = AreaVisibility.Capture(GetWorld3D(), origin, 15);
		var random = new RandomNumberGenerator { Seed = _seed++ };
		if (_selected < 2)
		{
			int cap = _selected == 0 ? _level : _level + 1;
			var segments = PreviewTargets.Select(GetWorld3D(), origin, _targets, visibility, random, TargetStrategies[_selected], cap);
			int selected = segments.Count;
			for (int i = 0; i < segments.Count; i++)
			{
				if (_selected == 0) SpawnBall(segments[i].Start, segments[i].End, visibility);
				else SpawnArc(segments[i].Start, segments[i].End, random.Randi(), i * .12f, visibility);
			}
			for (int i = selected; i < cap; i++)
			{
				if (PreviewTargets.TryCosmetic(GetWorld3D(), origin, visibility, random, out var destination))
				{
					if (_selected == 0) SpawnBall(origin + Vector3.Up * 1.3f, destination, visibility);
					else SpawnArc(origin + Vector3.Up * 1.3f, destination, random.Randi(), i * .08f, visibility);
				}
			}
			_status.Text = $"{Names[_selected]} level {_level}: {selected} distinct target(s); unused slots produce cosmetic effects."; return;
		}
		var area = SpawnEffect(required, origin, visibility);
		area.Scale = new Vector3(radius / 9f, 1, radius / 9f);
		if (_selected == 3)
		{
			var points = _targets.Where(t => visibility.Contains(t.GlobalPosition) && t.GlobalPosition.DistanceTo(origin) <= radius).Select(t => t.GlobalPosition).ToList();
			int count = 3 + _level * 2;
			for (int i = 0; i < count; i++)
			{
				float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(.5f, radius);
				Vector3 p = i < points.Count ? points[i] : origin + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
				if (!visibility.Contains(p)) continue;
				float delay = i * .1f;
				SpawnArc(p + Vector3.Up * 5, p + Vector3.Up * .15f, random.Randi(), delay, visibility);
				SpawnEffect(LightningCracksScene, p, visibility, delay);
			}
		}
		_status.Text = $"{Names[_selected]} level {_level}: radius {radius:0.0}. Visual study only.";
	}
	private VisualEffectInstance SpawnEffect(PackedScene scene, Vector3 position, AreaVisibility visibility, float delay = 0)
	{
		var effect = scene.Instantiate<VisualEffectInstance>(); effect.Position = position; effect.Timeline.StartDelay = delay; AddChild(effect);
		if (visibility != null) effect.SetVisibility(visibility);
		return effect;
	}
	private void SpawnBall(Vector3 from, Vector3 to, AreaVisibility visibility)
	{
		var ball = LightningBallScene.Instantiate<TravelingEffectVisual>();
		ball.Configure(from, to, _level, visibility); AddChild(ball);
	}
	private void SpawnArc(Vector3 from, Vector3 to, ulong seed, float delay, AreaVisibility visibility)
	{
		var arc = ArcScene.Instantiate<LightningArc>(); arc.Configure(from, to, seed); arc.Timeline.StartDelay = delay;
		arc.Width *= .75f + .2f * _level; AddChild(arc);
		if (visibility != null) arc.Timeline.SetVisibility(visibility);
	}
	public void Reflect()
	{
		if (!GodotObject.IsInstanceValid(_guard)) { _status.Text = "Cast Storm Guard first."; return; }
		if (ArcScene == null) { _status.Text = "Missing lightning arc scene."; return; }
		var target = _targets.FirstOrDefault(t => WorldSight.IsClear(GetWorld3D(), Caster.GlobalPosition + Vector3.Up, t.GlobalPosition + Vector3.Up));
		if (target == null) { _status.Text = "Add a visible target to preview reflection."; return; }
		var visibility = AreaVisibility.Capture(GetWorld3D(), Caster.GlobalPosition, 15);
		SpawnArc(Caster.GlobalPosition + Vector3.Up, target.GlobalPosition + Vector3.Up, _seed++, 0, visibility);
		_guardHits--;
		if (_guardHits <= 0) { ClearGuard(); _status.Text = "Guard depleted."; }
		else _status.Text = $"Reflected hit. {_guardHits} preview hits remaining.";
	}
	public void ClearGuard()
	{
		if (GodotObject.IsInstanceValid(_guard)) { _guard.Visible = false; _guard.QueueFree(); }
		_guard = null; _guardHits = 0;
	}
}