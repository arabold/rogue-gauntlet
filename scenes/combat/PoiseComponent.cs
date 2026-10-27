using Godot;

public enum PoiseReaction { Flinch, Break }

/// <summary>
/// Per-actor hit-reaction meter. Contacts that leave poise above zero only flinch, so the current action
/// continues; the contact that empties it breaks into a stagger through the action gate. The meter stays
/// empty for the stagger (later hits cannot chain breaks) and refills when it ends. Otherwise it refills
/// after <see cref="PoiseDefinition.RegenDelay"/> without poise damage. Runtime-only: saves, floor entry
/// and simulation resets start from a full meter.
/// </summary>
[GlobalClass]
public partial class PoiseComponent : Node
{
	[Signal] public delegate void PoiseChangedEventHandler(float current, float maximum);
	[Signal] public delegate void PoiseBrokenEventHandler();

	[Export] public PoiseDefinition Definition { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public float Current { get; private set; }
	public float Maximum => Definition?.MaxPoise ?? 0;
	public bool IsBroken { get; private set; }

	/// <summary>An unassigned or invalid definition or gate leaves the actor without poise.</summary>
	public bool IsAvailable => Definition?.IsValid == true && ActionGate != null;

	private double _sinceDamage;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Defense;
		if (!IsAvailable) GD.PushError($"{GetParent()?.Name}/{Name}: assign a valid PoiseDefinition and ActionGate; the actor has no poise.");
		Restore();
	}

	/// <summary>Remove poise for one unprotected contact. Never cancels actions except through a break's stagger.</summary>
	public PoiseReaction Apply(float poiseDamage)
	{
		if (!IsAvailable || IsBroken || ActionGate.IsStaggered || !(poiseDamage > 0)) return PoiseReaction.Flinch;
		if (ActionGate.IsHyperArmored) poiseDamage *= Definition.ArmoredPoiseDamageScale;
		_sinceDamage = 0;
		Current = Mathf.Max(0, Current - poiseDamage);
		SetPhysicsProcess(true);
		if (Current > 0)
		{
			EmitSignalPoiseChanged(Current, Maximum);
			return PoiseReaction.Flinch;
		}

		IsBroken = true;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "poise.broken", ("stagger", Definition.BreakStaggerDuration));
		EmitSignalPoiseChanged(Current, Maximum);
		ActionGate.TryStagger(Definition.BreakStaggerDuration);
		EmitSignalPoiseBroken();
		return PoiseReaction.Break;
	}

	/// <summary>Refill immediately and clear a pending break.</summary>
	public void Restore()
	{
		IsBroken = false;
		_sinceDamage = 0;
		Current = Maximum;
		SetPhysicsProcess(false);
		EmitSignalPoiseChanged(Current, Maximum);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsAvailable) { SetPhysicsProcess(false); return; }
		if (IsBroken)
		{
			if (!ActionGate.IsStaggered) Restore();
			return;
		}
		_sinceDamage += delta;
		if (_sinceDamage < Definition.RegenDelay) return;
		Current = Mathf.Min(Maximum, Current + Definition.RegenPerSecond * (float)delta);
		EmitSignalPoiseChanged(Current, Maximum);
		if (Current >= Maximum) SetPhysicsProcess(false);
	}
}