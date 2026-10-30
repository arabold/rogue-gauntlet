using Godot;

/// <summary>
/// Synchronizes player stat resources with runtime gameplay components.
/// </summary>
public partial class PlayerStatsController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public PlayerStats Stats { get; set; }
	[Export] public Inventory Inventory { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public HurtBoxComponent HurtBoxComponent { get; set; }
	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public ManaComponent ManaComponent { get; set; }

	// The current mana the component last reported. Live mana flows from the component, which regenerates between
	// reports, so a sync pushes Stats.Mana back only when something else wrote it: spawn, a load or a class refill.
	private float _reportedMana = float.NaN;

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		Stats ??= Player.Stats;
		Inventory ??= Player.Inventory;
		MovementComponent ??= Player.MovementComponent;
		HurtBoxComponent ??= Player.HurtBoxComponent;
		HealthComponent ??= Player.HealthComponent;
		ManaComponent ??= Player.ManaComponent;

		this.SubscribeUntilExit(
			HealthComponent,
			healthComponent => healthComponent.HealthChanged += OnHealthChanged,
			healthComponent => healthComponent.HealthChanged -= OnHealthChanged);
		this.SubscribeUntilExit(
			ManaComponent,
			manaComponent => manaComponent.ManaChanged += OnManaChanged,
			manaComponent => manaComponent.ManaChanged -= OnManaChanged);
		this.SubscribeUntilExit(
			Stats,
			stats => stats.Changed += SyncStats,
			stats => stats.Changed -= SyncStats);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.XpRewarded += OnXpRewarded,
			signalBus => signalBus.XpRewarded -= OnXpRewarded);

		SyncStats();
	}

	public void SyncStats()
	{
		MovementComponent.Speed = Stats.Speed;
		HurtBoxComponent.Armor = Stats.Armor;
		HealthComponent.SetHealth(Stats.Health, Stats.MaxHealth);
		SyncMana();

		SignalBus.EmitPlayerStatsChanged(Stats);
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		// Only current health flows back from the component. Max health is derived from
		// stats (base + Vitality + modifiers) and pushed out by SyncStats; writing the
		// derived max back into BaseMaxHealth would re-add the derivation each sync and
		// inflate it without bound.
		Stats.Health = health;
	}

	private void SyncMana()
	{
		ManaComponent.RegenPerSecond = Stats.ManaRegenPerSecond;
		if (Stats.Mana == _reportedMana)
		{
			ManaComponent.MaxMana = Stats.MaxMana;
			return;
		}

		ManaComponent.SetMana(Stats.Mana, Stats.MaxMana);
		// Copy the clamped value back even when the component did not change and so did not report it.
		OnManaChanged(ManaComponent.CurrentMana, ManaComponent.MaxMana);
	}

	private void OnManaChanged(float mana, float maxMana)
	{
		// Like health, only the current value flows back; the maximum is derived from stats.
		_reportedMana = mana;
		Stats.Mana = mana;
	}

	private void OnXpRewarded(int xp)
	{
		Stats.AddXp(xp);
	}
}