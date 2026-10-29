using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>One focus-owned Frost Nova source; casting and equipment are supplied by its caller.</summary>
public partial class FrostNovaSpell : Node, IStatusRestoreSource, ICombatSpell
{
	public string DisplayName => "Frost Nova";
	public int LevelCount => 3;
	[Export] public FrostNovaDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	private readonly HashSet<BuffController> _affected = [];
	private readonly AreaCastLifecycle _cast = new();
	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Damage; SetPhysicsProcess(false); }
	public override void _PhysicsProcess(double delta) { if (!_cast.Advance(delta)) SetPhysicsProcess(false); }
	/// <summary>Reconnect a restored target so canceling this focus still removes its status.</summary>
	public void StatusRestored(BuffController target) => _affected.Add(target);

	/// <summary>Release one area contact per visible hostile actor; visual failure never causes damage.</summary>
	public bool Cast(Node3D caster, int level, RandomNumberGenerator random)
	{
		if (Definition?.IsValidLevel(level) != true || VisualScene == null) return false;
		float radius = Definition.RadiusByLevel[level - 1];
		if (!AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, radius, out var cast)) return false;
		var node = VisualScene.Instantiate();
		if (node is not WaterSpellVisual visual || !visual.Area || visual.Timeline == null)
		{
			node?.Free();
			return false;
		}
		var visibility = AreaVisibility.Capture(cast.World, cast.Origin, radius);
		visual.ConfigureLevel(level);
		visual.SetAreaRadius(radius);
		VisualRoot.AddChild(visual);
		visual.GlobalPosition = cast.Origin;
		visual.SetVisibility(visibility);
		_cast.Track(visual);
		_affected.RemoveWhere(statuses => !IsInstanceValid(statuses) || !statuses.HasStatusFrom(this));
		float freezeSeconds = Definition.FreezeSecondsByLevel[level - 1];
		StatusCondition frozen = Definition.Frozen;
		_cast.Schedule(cast, Definition.ContactDelay, Definition.DamageByLevel[level - 1], Definition.DefenseTraits, afterContact: (target, result) =>
		{
			if (result.Outcome != DamageOutcome.Damaged || !IsInstanceValid(target) || !target.IsInsideTree()
				|| target is not IStatusActor { BuffController: { } statuses } || statuses.Health == null || statuses.Health.IsDead) return;
			_affected.Add(statuses);
			if (!statuses.ApplyStatus(frozen, this, freezeSeconds)) _affected.Remove(statuses);
		}, element: DamageElement.Water);
		return true;
	}

	/// <summary>End all remaining presentation and conditions owned by this focus.</summary>
	public void Cancel()
	{
		SetPhysicsProcess(false);
		var visuals = _cast.Cancel();
		var affected = _affected.ToArray();
		_affected.Clear();
		foreach (var statuses in affected) if (IsInstanceValid(statuses)) statuses.RemoveStatuses(this);
		AreaCastLifecycle.ClearVisuals(visuals);
	}
	public override void _ExitTree() => Cancel();
}
