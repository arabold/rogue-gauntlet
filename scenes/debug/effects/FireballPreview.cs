using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Interactive three-level fire spell workbench with target and wall fixtures.</summary>
public partial class FireballPreview : Node3D
{
	[Export] public FireballSpell Spell { get; set; }
	[Export] public PackedScene TargetVisual { get; set; }
	[Export] public PackedScene PlayerVisual { get; set; }
	[Export] public VBoxContainer Controls { get; set; }
	[Export] public DebugStage DebugStage { get; set; }
	[Export] public WorldEnvironment Lighting { get; set; }
	[Export] public DirectionalLight3D KeyLight { get; set; }
	[Export] public Node3D WallAim { get; set; }
	[Export] public PackedScene FlameBurst { get; set; }
	[Export] public PackedScene EmberGuard { get; set; }
	[Export] public PackedScene SolarFlares { get; set; }
	[Export] public PackedScene MeteorShower { get; set; }
	[Export] public PackedScene MeteorDebris { get; set; }
	[Export] public int[] MeteorTargetCounts { get; set; } = [3, 6, 9];
	[Export] public float[] GuardPreviewCapacity { get; set; } = [100f, 150f, 200f];
	[Export] public float GuardPreviewHit { get; set; } = 25f;
	private readonly List<Node3D> _targets = [];
	private readonly List<AnimationPlayer> _animations = [];
	private int _level = 1, _count = 3, _spellIndex, _guardLevel;
	private Button _castButton, _wallButton, _reflectButton, _clearGuardButton;
	private float _guardRemaining, _guardMaximum;
	private FireSpellVisual _guard;
	private static readonly string[] SpellNames = ["Fireball", "Flame Burst", "Ember Guard", "Solar Flares", "Meteor Shower"];
	private ulong _seed = 1;
	private Label _status;

	public override void _Ready()
	{
		var player = PlayerVisual.Instantiate<Node3D>();
		EffectPreviewStage.AddSubject(player, Spell.Caster, _animations);
		EffectPreviewStage.PlayAnimations(_animations, 0, 1);
		EffectPreviewControls.SpellSelectors(Controls, DebugStage, SpellNames, SetLevel, SetTargetCount, SelectSpell);
		_castButton = EffectPreviewControls.Button(Controls, "Cast Fireball", Cast);
		EffectPreviewControls.Button(Controls, "Reset targets", ResetTargets);
		_wallButton = EffectPreviewControls.Button(Controls, "Test wall impact", () => { Spell.PreviewWall(_level, WallAim.GlobalPosition); _status.Text = $"Level {_level}: wall impact."; });
		DebugStage.ArrangementChanged += () => _wallButton.Visible = _spellIndex == 0 && DebugStage.HasWestWall;
		_reflectButton = EffectPreviewControls.Button(Controls, "Preview reflection", PreviewReflection);
		_reflectButton.Visible = false;
		_clearGuardButton = EffectPreviewControls.Button(Controls, "Clear guard", ClearGuard);
		_clearGuardButton.Visible = false;
		EffectPreviewControls.Playback(Controls, speed => Engine.TimeScale = speed);
		EffectPreviewControls.DarkLighting(Controls, Lighting, KeyLight);
		EffectPreviewControls.CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
		ResetTargets();
	}
	public override void _ExitTree() => Engine.TimeScale = 1;

	private void ResetTargets()
	{
		EffectPreviewStage.ResetSpellTargets(this, TargetVisual, _targets, _animations, _count, true, configureTarget: target =>
		{
			var health = new HealthComponent { Name = "HealthComponent" }; target.AddChild(health); health.SetHealth(100, 100);
			var hurt = new HurtBoxComponent { Name = "HurtBoxComponent", HealthComponent = health, CollisionLayer = 8, Monitoring = false };
			target.AddChild(hurt);
			hurt.AddChild(new CollisionShape3D { Position = Vector3.Up, Shape = new SphereShape3D { Radius = .6f } });
		});
		_status.Text = "Choose a level and cast. Reset restores target health.";
	}
	/// <summary>Rebuilds the stationary targets in the preview.</summary>
	public void SetTargetCount(int count) { _count = Mathf.Clamp(count, 0, 5); ResetTargets(); }

	/// <summary>Selects the preview level.</summary>
	public void SetLevel(int level) { if (level is >= 1 and <= 3) _level = level; }

	/// <summary>Selects a visual study without affecting the player's learned state.</summary>
	public void SelectSpell(int index)
	{
		if (index < 0 || index >= SpellNames.Length) return;
		_spellIndex = index;
		_castButton.Text = $"Cast {SpellNames[index]}";
		_wallButton.Visible = index == 0 && DebugStage.HasWestWall;
		_reflectButton.Visible = index == 2;
		_clearGuardButton.Visible = index == 2;
		_status.Text = "Choose a level and cast. Reset restores target health.";
	}

	/// <summary>Plays the chosen prototype; only the existing Fireball fixture resolves damage.</summary>
	public void Cast()
	{
		if (_spellIndex == 0)
		{
			int fireballTargets = Spell.Cast(_level, _targets, _seed++);
			_status.Text = $"Level {_level}: {fireballTargets} selected, {Spell.TargetCounts[_level - 1] - fireballTargets} cosmetic flights.";
			return;
		}
		if (_spellIndex == 2)
		{
			if (EmberGuard == null || _level > GuardPreviewCapacity.Length || GuardPreviewCapacity[_level - 1] <= 0)
			{ _status.Text = "Invalid guard preview configuration."; return; }
			if (GodotObject.IsInstanceValid(_guard)) _guard.QueueFree();
			_guard = SpawnVisual(EmberGuard, Spell.Caster.GlobalPosition);
			_guardLevel = _level;
			_guardMaximum = _guardRemaining = GuardPreviewCapacity[_level - 1];
			_status.Text = $"Ember Guard: {_guardRemaining:0}/{_guardMaximum:0} shield. Preview hits or clear it.";
			return;
		}
		if (_spellIndex is 1 or 3)
		{
			PackedScene areaScene = _spellIndex == 1 ? FlameBurst : SolarFlares;
			if (areaScene == null) { _status.Text = "Missing area effect scene."; return; }
			var burst = SpawnVisual(areaScene, Spell.Caster.GlobalPosition);
			int inArea = _targets.Count(target => GodotObject.IsInstanceValid(target) && target.IsInsideTree()
				&& !target.GetNode<HealthComponent>("HealthComponent").IsDead
				&& target.GlobalPosition.DistanceTo(Spell.Caster.GlobalPosition) <= burst.PreviewRadius
				&& WorldSight.IsClear(Spell.Caster.GetWorld3D(), Spell.Caster.GlobalPosition + Vector3.Up, target.GlobalPosition + Vector3.Up));
			_status.Text = $"{SpellNames[_spellIndex]} level {_level}: radius {burst.PreviewRadius:0.0}, {inArea} target(s) in area.";
			return;
		}
		PackedScene effect = MeteorShower;
		if (effect == null || MeteorDebris == null) { _status.Text = "Missing meteor or debris scene."; return; }
		int[] caps = MeteorTargetCounts;
		if (_level > caps.Length || caps[_level - 1] < 1) { _status.Text = "Invalid target count for this level."; return; }
		int cap = caps[_level - 1];
		ulong seed = _seed++;
		{
			var living = _targets.Where(t => GodotObject.IsInstanceValid(t) && t.IsInsideTree() && !t.GetNode<HealthComponent>("HealthComponent").IsDead);
			var placements = ShowerPreview.Build(GetWorld3D(), Spell.Caster.GlobalPosition, living, cap, 3 * (_level + 1), seed);
			for (int i = 0; i < placements.Count; i++) SpawnVisual(placements[i].Cosmetic ? MeteorDebris : effect, placements[i].Position, i * .08f, placements[i].Cosmetic);
			_status.Text = $"Meteor Shower level {_level}: {placements.Count(p => !p.Cosmetic)} strikes, {placements.Count(p => p.Cosmetic)} cosmetic debris.";
			return;
		}
	}

	private FireSpellVisual SpawnVisual(PackedScene scene, Vector3 position, float delay = 0, bool cosmetic = false)
	{
		var effect = scene.Instantiate<FireSpellVisual>();
		effect.ConfigureLevel(_level);
		effect.StartDelay = delay;
		effect.Cosmetic = cosmetic;
		if (cosmetic) effect.Scale *= .55f;
		AddChild(effect);
		effect.GlobalPosition = position;
		if (effect.PreviewRadius > 0)
			effect.SetVisibility(AreaVisibility.Capture(GetWorld3D(), position, effect.PreviewRadius + 8));
		return effect;
	}

	public void PreviewReflection()
	{
		if (!GodotObject.IsInstanceValid(_guard) || _guard.IsQueuedForDeletion()) { _status.Text = "Cast Ember Guard first."; return; }
		var target = _targets.FirstOrDefault(t => GodotObject.IsInstanceValid(t) && t.IsInsideTree() && !t.GetNode<HealthComponent>("HealthComponent").IsDead);
		if (target == null) { _status.Text = "Add a target to preview reflection."; return; }
		if (GuardPreviewHit <= 0) { _status.Text = "Invalid preview hit amount."; return; }
		_guard.Flash();
		Spell.PreviewWall(_guardLevel, target.GlobalPosition + Vector3.Up);
		_guardRemaining = Mathf.Max(0, _guardRemaining - GuardPreviewHit);
		if (_guardRemaining <= 0)
		{
			_guard.Visible = false;
			_guard.QueueFree();
			_guard = null;
			_status.Text = "Guard depleted after reflecting the final hit.";
		}
		else _status.Text = $"Reflected hit. Shield: {_guardRemaining:0}/{_guardMaximum:0}.";
	}
	/// <summary>Ends the guard in the workbench.</summary>
	public void ClearGuard()
	{
		if (GodotObject.IsInstanceValid(_guard)) { _guard.Visible = false; _guard.QueueFree(); }
		_guard = null;
		_guardRemaining = 0;
		_status.Text = "Guard cleared.";
	}

}
