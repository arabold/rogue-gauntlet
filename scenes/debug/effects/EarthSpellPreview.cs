using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Interactive earth spell visual studies with stationary targets and a wall.</summary>
public partial class EarthSpellPreview : Node3D
{
	[Export] public Node3D Caster { get; set; }
	[Export] public PackedScene TargetVisual { get; set; }
	[Export] public PackedScene PlayerVisual { get; set; }
	[Export] public VBoxContainer Controls { get; set; }
	[Export] public DebugStage DebugStage { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public Camera3D Camera { get; set; }
	public Node3D PreviewWall => DebugStage.WestWall;
	[Export] public PackedScene[] Effects { get; set; } = [];
	[Export] public float ShakeStrength { get; set; } = .09f;
	private readonly List<Node3D> _targets = [];
	private readonly List<AnimationPlayer> _animations = [];
	private readonly string[] _names = ["Rockfall", "Tremor", "Rock Spikes", "Stone Barrier", "Spiked Armor"];
	private int _level = 1, _count = 3, _selected;
	private EarthSpellVisual _guard;
	private int _guardHp;
	private Button _cast, _reflect, _clear;
	private Label _status;
	private float _shakeTime;
	private readonly Dictionary<StaticBody3D, GpuParticles3D> _wallDust = [];
	private ulong _seed = 1;

	public override void _Ready()
	{
		EffectPreviewStage.AddSubject(PlayerVisual.Instantiate<Node3D>(), Caster, _animations);
		EffectPreviewControls.SpellSelectors(Controls, DebugStage, _names, SetLevel, SetTargetCount, SelectSpell);
		_cast = EffectPreviewControls.Button(Controls, "Cast Rockfall", Cast);
		EffectPreviewControls.Button(Controls, "Reset targets", ResetTargets);
		_reflect = EffectPreviewControls.Button(Controls, "Preview reflection", Reflect); _reflect.Visible = false;
		_clear = EffectPreviewControls.Button(Controls, "Clear guard", ClearGuard); _clear.Visible = false;
		EffectPreviewControls.Playback(Controls, speed => Engine.TimeScale = speed);
		EffectPreviewControls.DarkLighting(Controls, Lighting, KeyLight); EffectPreviewControls.CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
		CreateWallDust(); ResetTargets();
	}
	public override void _ExitTree() => Engine.TimeScale = 1;
	public override void _Process(double delta)
	{
		if (_shakeTime <= 0) return;
		_shakeTime = Mathf.Max(0, _shakeTime - (float)delta);
		float strength = ShakeStrength * (_level + 1) * Mathf.Clamp(_shakeTime / .45f, 0, 1);
		Camera.HOffset = Mathf.Sin(_shakeTime * 72) * strength;
		Camera.VOffset = Mathf.Cos(_shakeTime * 57) * strength * .6f;
	}
	private void CreateWallDust()
	{
		var mat = EarthDust.Material(.35f);
		var process = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.3f, .15f, 2.8f), Direction = Vector3.Down, Spread = 25, InitialVelocityMin = .2f, InitialVelocityMax = .8f, Gravity = new Vector3(0, -.6f, 0), ScaleMin = .3f, ScaleMax = 1f };
		foreach (var wall in new[] { DebugStage.WestWall }.Concat(DebugStage.DefaultWalls).Concat(DebugStage.RoomWalls))
		{
			var dust = new GpuParticles3D { Amount = 80, Lifetime = 2.2, OneShot = true, Explosiveness = .4f, Emitting = false, Position = new Vector3(.4f, 2, 0), ProcessMaterial = process, DrawPass1 = new QuadMesh { Size = Vector2.One, Material = mat } };
			wall.AddChild(dust); _wallDust.Add(wall, dust);
		}
	}
	private void ResetTargets()
	{
		EffectPreviewStage.ResetSpellTargets(this, TargetVisual, _targets, _animations, _count, false);
		_status.Text = "Choose a spell and level, then cast.";
	}
	public void SetTargetCount(int count) { _count = Mathf.Clamp(count, 0, 5); ResetTargets(); }
	public void SetLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public void SelectSpell(int index)
	{
		if (index < 0 || index >= _names.Length) return;
		_selected = index; _cast.Text = $"Cast {_names[index]}"; _reflect.Visible = _clear.Visible = index >= 3;
	}
	public void Cast()
	{
		if (_selected >= Effects.Length || Effects[_selected] == null) { _status.Text = "Missing preview effect."; return; }
		if (_selected >= 3)
		{
			ClearGuard(); _guard = Spawn(Caster.GlobalPosition); _guardHp = 3 + _level + (_selected == 4 ? 2 : 0);
			_status.Text = $"{_names[_selected]}: {_guardHp} preview hits remaining."; return;
		}
		if (_selected == 0)
		{
			var placements = ShowerPreview.Build(GetWorld3D(), Caster.GlobalPosition, _targets, _level * 3, _level + 1, _seed++);
			for (int i = 0; i < placements.Count; i++) Spawn(placements[i].Position, i * .11f, placements[i].Cosmetic);
			_status.Text = $"Rockfall level {_level}: {placements.Count(p => !p.Cosmetic)} strikes, {placements.Count(p => p.Cosmetic)} cosmetic rocks."; return;
		}
		var effect = Spawn(Caster.GlobalPosition);
		if (_selected == 1) { foreach (var wall in DebugStage.BoundaryWalls()) { var dust = _wallDust[wall]; dust.Restart(); dust.Emitting = true; } _shakeTime = .55f; }
		_status.Text = $"{_names[_selected]} level {_level}: radius {effect.PreviewRadius:0.0}.";
	}
	private EarthSpellVisual Spawn(Vector3 position, float delay = 0, bool cosmetic = false)
	{
		var effect = Effects[_selected].Instantiate<EarthSpellVisual>(); effect.ConfigureLevel(_level); effect.StartDelay = delay; effect.Cosmetic = cosmetic;
		if (cosmetic) effect.Scale = Vector3.One * .55f;
		AddChild(effect); effect.GlobalPosition = position;
		if (effect.Kind is EarthSpellVisual.EarthKind.Tremor or EarthSpellVisual.EarthKind.Spikes)
			effect.SetVisibility(AreaVisibility.Capture(GetWorld3D(), position, effect.PreviewRadius + 8));
		return effect;
	}
	public void Reflect()
	{
		if (!GodotObject.IsInstanceValid(_guard)) { _status.Text = "Cast a guard first."; return; }
		var target = _targets.FirstOrDefault(t => GodotObject.IsInstanceValid(t) && t.IsInsideTree());
		if (target == null) { _status.Text = "Add a target to preview reflection."; return; }
		var material = new StandardMaterial3D { AlbedoColor = new Color(.38f, .28f, .18f), EmissionEnabled = true, Emission = new Color(.5f, .2f, .04f), EmissionEnergyMultiplier = .5f };
		var shard = new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(.35f, .65f, .35f), Material = material } };
		AddChild(shard); shard.GlobalPosition = Caster.GlobalPosition + Vector3.Up;
		var tween = CreateTween();
		tween.TweenProperty(shard, "global_position", target.GlobalPosition + Vector3.Up, .35);
		tween.TweenCallback(Callable.From(shard.QueueFree));
		_guardHp--;
		if (_guardHp <= 0) { ClearGuard(); _status.Text = "Guard depleted."; }
		else _status.Text = $"Reflected hit. {_guardHp} preview hits remaining.";
	}
	public void ClearGuard()
	{
		if (GodotObject.IsInstanceValid(_guard)) { _guard.Visible = false; _guard.QueueFree(); }
		_guard = null; _guardHp = 0;
	}
}
