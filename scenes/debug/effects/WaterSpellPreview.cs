using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Visual-only water and ice spells with reversible frozen target previews.</summary>
public partial class WaterSpellPreview : Node3D
{
	[Export] public Node3D Caster { get; set; }
	[Export] public PackedScene TargetVisual { get; set; }
	[Export] public PackedScene PlayerVisual { get; set; }
	[Export] public VBoxContainer Controls { get; set; }
	[Export] public DebugStage DebugStage { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public PackedScene LanceScene { get; set; }
	[Export] public PackedScene NovaScene { get; set; }
	[Export] public PackedScene HailstormScene { get; set; }
	[Export] public LingeringAreaDefinition HailstormDefinition { get; set; }
	[Export] public PackedScene GuardScene { get; set; }
	[Export] public PackedScene IceWallScene { get; set; }
	[Export] public LingeringAreaDefinition IceWallDefinition { get; set; }
	[Export] public PreviewTargetStrategy TargetStrategy { get; set; }
	[Export] public WorldEffectDefinition FrozenEffect { get; set; }
	private static readonly string[] Names = ["Ice Lance", "Frost Nova", "Frost Guard", "Hailstorm", "Ice Wall"];
	private readonly List<Node3D> _targets = [];
	private readonly List<AnimationPlayer> _animations = [];
	private int _level = 1, _count = 3, _selected, _guardHits;
	private ulong _seed = 1;
	private WaterSpellVisual _guard;
	private Button _cast, _reflect, _clear;
	private Label _status;

	public override void _Ready()
	{
		EffectPreviewStage.AddSubject(PlayerVisual.Instantiate<Node3D>(), Caster, _animations);
		EffectPreviewControls.SpellSelectors(Controls, DebugStage, Names, SetLevel, SetTargetCount, SelectSpell);
		_cast = EffectPreviewControls.Button(Controls, "Cast Ice Lance", Cast);
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
		EffectPreviewStage.ResetSpellTargets(this, TargetVisual, _targets, _animations, _count, true, 3f);
		_status.Text = "Choose a spell and level, then cast.";
	}
	public void SetTargetCount(int count) { _count = Mathf.Clamp(count, 0, 5); ResetTargets(); }
	public void SetLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public void SelectSpell(int index)
	{
		if (index < 0 || index >= Names.Length) return;
		_selected = index; _cast.Text = $"Cast {Names[index]}"; _reflect.Visible = _clear.Visible = index == 2;
	}
	public void Cast()
	{
		if (_selected == 0 && (LanceScene == null || TargetStrategy == null)) { _status.Text = "Missing lance or target strategy."; return; }
		if (_selected == 1 && (NovaScene == null || FrozenEffect == null)) { _status.Text = "Missing frost nova or frozen effect."; return; }
		if (_selected == 3 && (HailstormScene == null || HailstormDefinition == null)) { _status.Text = "Missing hailstorm scene or definition."; return; }
		if (_selected == 2 && GuardScene == null) { _status.Text = "Missing guard scene."; return; }
		if (_selected == 4 && (IceWallScene == null || IceWallDefinition == null)) { _status.Text = "Missing ice wall scene or definition."; return; }
		Vector3 origin = Caster.GlobalPosition;
		var visibility = AreaVisibility.Capture(GetWorld3D(), origin, 15);
		if (_selected == 2)
		{
			ClearGuard(); _guard = SpawnEffect(GuardScene, origin, visibility);
			_guardHits = 3 + _level;
			_status.Text = $"{Names[_selected]}: {_guardHits} preview hits remaining."; return;
		}
		if (_selected == 0)
		{
			var random = new RandomNumberGenerator { Seed = _seed++ };
			var segments = PreviewTargets.Select(GetWorld3D(), origin, _targets, visibility, random, TargetStrategy, _level);
			foreach (var segment in segments) SpawnLance(segment.Start, segment.End, visibility);
			for (int i = segments.Count; i < _level; i++)
			{
				if (PreviewTargets.TryCosmetic(GetWorld3D(), origin, visibility, random, out var destination))
					SpawnLance(origin + Vector3.Up * 1.3f, destination, visibility);
			}
			_status.Text = $"Ice Lance level {_level}: {segments.Count} distinct target(s); unused flights are cosmetic."; return;
		}
		if (_selected == 3)
		{
			var storm = SpawnEffect(HailstormScene, origin, visibility);
			storm.Hailstorm.PreviewDrops(origin, storm.PreviewRadius, HailstormDefinition.ShardsPerSecondByLevel[_level - 1], _seed++);
			int affected = _targets.Count(target => target.GlobalPosition.DistanceTo(origin) <= storm.PreviewRadius
				&& visibility.Contains(target.GlobalPosition) && WorldSight.IsClear(GetWorld3D(), origin + Vector3.Up, target.GlobalPosition + Vector3.Up));
			_status.Text = $"Hailstorm level {_level}: radius {storm.PreviewRadius:0.0}, {affected} target(s) in area. Damage and slow are not connected.";
			return;
		}
		if (_selected == 4)
		{
			var wall = SpawnEffect(IceWallScene, origin, visibility, IceWallDefinition.RadiusByLevel[_level - 1]);
			int inside = _targets.Count(target => target.GlobalPosition.DistanceTo(origin) <= wall.PreviewRadius);
			_status.Text = $"Ice Wall level {_level}: radius {wall.PreviewRadius:0.0}, {inside} target(s) inside. Damage and push are not connected.";
			return;
		}
		var nova = SpawnEffect(NovaScene, origin, visibility);
		int frozen = 0;
		foreach (var target in _targets)
		{
			float distance = target.GlobalPosition.DistanceTo(origin);
			if (distance > nova.PreviewRadius || !visibility.Contains(target.GlobalPosition) || !WorldSight.IsClear(GetWorld3D(), origin + Vector3.Up, target.GlobalPosition + Vector3.Up)) continue;
			var existing = target.GetChildren().OfType<FrozenPreview>().FirstOrDefault();
			if (existing != null) { existing.Refresh(); frozen++; continue; }
			var actor = target.GetChildren().OfType<EnemyBase>().FirstOrDefault();
			var host = actor?.WorldEffects;
			if (host == null) continue;
			var effect = new FrozenPreview { Host = host, Effect = FrozenEffect, Visual = actor, StartDelay = .35f * distance / nova.PreviewRadius };
			target.AddChild(effect); frozen++;
		}
		_status.Text = $"Frost Nova level {_level}: radius {nova.PreviewRadius:0.0}, {frozen} frozen appearance(s). Visual study only.";
	}
	private WaterSpellVisual SpawnEffect(PackedScene scene, Vector3 position, AreaVisibility visibility, float radius = 0)
	{
		var effect = scene.Instantiate<WaterSpellVisual>(); effect.ConfigureLevel(_level);
		if (radius > 0) effect.SetAreaRadius(radius);
		effect.Position = position; AddChild(effect);
		effect.SetVisibility(visibility); return effect;
	}
	private void SpawnLance(Vector3 from, Vector3 to, AreaVisibility visibility)
	{
		var lance = LanceScene.Instantiate<TravelingEffectVisual>();
		lance.Configure(from, to, _level, visibility); lance.ImpactGroundHeight = Caster.GlobalPosition.Y; AddChild(lance);
	}
	public void Reflect()
	{
		if (!GodotObject.IsInstanceValid(_guard)) { _status.Text = "Cast a guard first."; return; }
		if (LanceScene == null) { _status.Text = "Missing reflection visual."; return; }
		var target = _targets.FirstOrDefault(t => WorldSight.IsClear(GetWorld3D(), Caster.GlobalPosition + Vector3.Up, t.GlobalPosition + Vector3.Up));
		if (target == null) { _status.Text = "Add a visible target to preview reflection."; return; }
		var visibility = AreaVisibility.Capture(GetWorld3D(), Caster.GlobalPosition, 15);
		SpawnLance(Caster.GlobalPosition + Vector3.Up, target.GlobalPosition + Vector3.Up, visibility);
		_guard.FlashGuard();
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
