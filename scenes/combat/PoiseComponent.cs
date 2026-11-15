using Godot;

/// <summary>
/// Per-actor hit-reaction meter. Contacts that leave poise above zero only flinch, so the current action
/// continues; the contact that empties it breaks into a stagger through the action gate. The meter stays
/// empty for the stagger (later hits cannot chain breaks) and refills when it ends, larger by
/// <see cref="PoiseDefinition.BreakGrowth"/> for each recent break. Otherwise it refills after
/// <see cref="PoiseDefinition.RegenDelay"/> without poise damage. Runtime-only: saves, floor entry and
/// simulation resets start from a full meter.
/// </summary>
[GlobalClass]
public partial class PoiseComponent : Node
{
	/// <summary>Fires on damage, breaks and restores, and on each visible step (1% of max) while refilling.</summary>
	[Signal] public delegate void PoiseChangedEventHandler(float current, float maximum);
	[Signal] public delegate void PoiseBrokenEventHandler();

	[Export] public PoiseDefinition Definition { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public float Current { get; private set; }
	/// <summary>The meter's size now: <see cref="PoiseDefinition.MaxPoise"/>, grown by each break within the break memory.</summary>
	public float Maximum => (Definition?.MaxPoise ?? 0) * (1 + (Definition?.BreakGrowth ?? 0) * _recentBreaks);

	/// <summary>Poise only reaches zero through a break, and stays there until the stagger ends.</summary>
	public bool IsBroken => IsAvailable && Current <= 0;

	/// <summary>An unassigned or invalid definition or gate leaves the actor without poise.</summary>
	public bool IsAvailable => Definition?.IsValid == true && ActionGate != null;

	private double _sinceDamage;
	private double _sinceBreak;
	private int _recentBreaks;
	private float _emitted;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Defense;
		if (!IsAvailable) GD.PushError($"{GetParent()?.Name}/{Name}: assign a valid PoiseDefinition and ActionGate; the actor has no poise.");
		Restore();
	}

	/// <summary>
	/// Remove poise for one unprotected contact and return whether it broke. Never cancels actions except
	/// through a break's stagger.
	/// </summary>
	public bool Apply(float poiseDamage)
	{
		if (!IsAvailable || IsBroken || ActionGate.IsStaggered || ActionGate.IsUnstoppable || !(poiseDamage > 0)) return false;
		if (ActionGate.IsHyperArmored) poiseDamage *= Definition.ArmoredPoiseDamageScale;
		_sinceDamage = 0;
		Current = Mathf.Max(0, Current - poiseDamage);
		SetPhysicsProcess(true);
		EmitChanged();
		if (Current > 0) return false;

		_recentBreaks++;
		_sinceBreak = 0;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "poise.broken", ("stagger", Definition.BreakStaggerDuration), ("next", Maximum));
		ActionGate.TryStagger(Definition.BreakStaggerDuration);
		EmitSignalPoiseBroken();
		return true;
	}

	/// <summary>Refill immediately and clear a pending break; recent breaks keep the meter grown.</summary>
	public void Restore()
	{
		_sinceDamage = 0;
		Current = Maximum;
		SetPhysicsProcess(_recentBreaks > 0);
		EmitChanged();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsAvailable) { SetPhysicsProcess(false); return; }
		if (IsBroken)
		{
			if (!ActionGate.IsStaggered) Restore();
			return;
		}
		if (_recentBreaks > 0 && (_sinceBreak += delta) >= Definition.BreakMemory)
		{
			_recentBreaks = 0;
			Current = Mathf.Min(Current, Maximum);
			EmitChanged();
		}
		_sinceDamage += delta;
		if (_sinceDamage >= Definition.RegenDelay && Current < Maximum)
		{
			Current = Mathf.Min(Maximum, Current + Definition.RegenPerSecond * (float)delta);
			// Regeneration reports visible steps only, so idle listeners are not signalled every frame.
			if (Current >= Maximum || Mathf.Abs(Current - _emitted) >= Maximum * .01f) EmitChanged();
		}
		if (Current >= Maximum && _recentBreaks == 0) SetPhysicsProcess(false);
	}

	private void EmitChanged()
	{
		_emitted = Current;
		EmitSignalPoiseChanged(Current, Maximum);
	}
}