using Godot;
using System.Collections.Generic;

/// <summary>Creates independently owned persistent area fields, canceled with their focus.</summary>
public partial class LingeringAreaSpell : Node, ICombatSpell
{
	[Export] public LingeringAreaDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	public IEnumerable<string> DescribeLevel(in SpellPower power) => Definition?.DescribeLevel(power) ?? [];
	private readonly List<LingeringAreaField> _fields = [];
	private readonly Dictionary<BuffController, BurningContact> _burns = [];
	private ulong _generation;

	public bool Cast(Node3D caster, in SpellPower power, RandomNumberGenerator random)
	{
		int level = power.Level;
		if (Definition?.IsValidLevel(level) != true || VisualScene == null
			|| !AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, Definition.RadiusByLevel[level - 1], out var cast)) return false;
		Node node = VisualScene.Instantiate();
		bool valid = Definition.Kind switch
		{
			LingeringAreaKind.SolarFlares => node is FireSpellVisual { Sweep: not null, Timeline: not null },
			LingeringAreaKind.Hailstorm => node is WaterSpellVisual { Area: true, Hailstorm: not null, Timeline: not null },
			LingeringAreaKind.IceWall => node is WaterSpellVisual { Area: true, Crystals: not null, Timeline: not null },
			_ => false
		};
		if (!valid) { node?.Free(); return false; }
		var visual = (VisualEffectInstance)node;
		if (visual is FireSpellVisual fire) fire.ConfigureLevel(level);
		if (visual is WaterSpellVisual water && !water.ConfigureArea(level, Definition.RadiusByLevel[level - 1])) { node.Free(); return false; }
		var field = new LingeringAreaField();
		field.Configure(this, cast, Definition, power, visual);
		VisualRoot.AddChild(field); field.GlobalPosition = cast.Origin; field.AddChild(visual);
		AreaVisibility visibility = AreaVisibility.Capture(cast.World, cast.Origin, Definition.RadiusByLevel[level - 1]);
		if (visual is VisualEffectInstance effect) effect.SetVisibility(visibility);
		_fields.RemoveAll(value => !IsInstanceValid(value)); _fields.Add(field);
		return true;
	}
	public void Cancel()
	{
		_generation++;
		var fields = _fields.ToArray(); _fields.Clear();
		foreach (var field in fields) if (IsInstanceValid(field)) field.End();
		var burns = new List<BurningContact>(_burns.Values); _burns.Clear();
		foreach (var burn in burns) if (IsInstanceValid(burn)) burn.End();
	}
	/// <summary>Refresh this focus's burn on a surviving target without stacking its own casts; <paramref name="damage"/> is per tick.</summary>
	public void Ignite(Node3D caster, Node3D target, RandomNumberGenerator random, float damage)
	{
		if (target is not ICombatDamageable || target is not IStatusActor { BuffController: { } statuses } || target is not IBurningActor { BurningStatus: not null }
			|| statuses.Health?.IsDead != false) return;
		ulong generation = _generation;
		if (_burns.TryGetValue(statuses, out var existing) && IsInstanceValid(existing) && !existing.IsQueuedForDeletion()
			&& existing.Refresh(caster, random, Definition.BurnDuration, Definition.BurnTickSeconds, damage)) return;
		if (_generation != generation) return;
		var burn = new BurningContact(); AddChild(burn);
		_burns[statuses] = burn;
		burn.Ended += ended => { if (_burns.TryGetValue(statuses, out var current) && ReferenceEquals(current, ended)) _burns.Remove(statuses); };
		if (!burn.Begin(caster, target, statuses, Definition.BurnCondition, random, Definition.BurnDuration, Definition.BurnTickSeconds, damage)) burn.End();
	}
	public override void _ExitTree() => Cancel();
}
