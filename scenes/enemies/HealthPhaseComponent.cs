using Godot;

/// <summary>
/// Latches health phases for any monster: phase 0 above the first threshold, phase 1 at or below it, and so on.
/// Healing never lowers the phase. Actions read it through <see cref="HealthPhaseCondition"/> (gates and weight
/// modifiers), so a boss's later moves are plain data; a phase never interrupts a running action because it only
/// matters when the next action is chosen.
/// </summary>
[GlobalClass]
public partial class HealthPhaseComponent : Node
{
	[Signal] public delegate void PhaseChangedEventHandler(int previous, int current);

	[Export] public HealthComponent Health { get; set; }
	/// <summary>Health fractions that start each later phase, strictly descending in (0, 1); e.g. 0.65, 0.3.</summary>
	[Export] public float[] EnterHealthFractions { get; set; } = [];

	public int CurrentPhase { get; private set; }

	public override void _Ready()
	{
		if (Health == null) return;
		this.SubscribeUntilExit(Health, health => health.HealthChanged += OnHealthChanged, health => health.HealthChanged -= OnHealthChanged);
		OnHealthChanged(Health.CurrentHealth, Health.MaxHealth);
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		if (currentHealth <= 0 || maxHealth <= 0) return;
		float fraction = currentHealth / maxHealth;
		int reached = CurrentPhase;
		while (reached < EnterHealthFractions.Length && fraction <= EnterHealthFractions[reached]) reached++;
		if (reached == CurrentPhase) return;
		int previous = CurrentPhase;
		CurrentPhase = reached;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "enemy.phase_changed", ("previous", previous), ("current", reached));
		EmitSignalPhaseChanged(previous, reached);
	}
}
