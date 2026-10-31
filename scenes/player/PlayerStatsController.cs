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

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		Stats ??= Player.Stats;
		Inventory ??= Player.Inventory;
		MovementComponent ??= Player.MovementComponent;
		HurtBoxComponent ??= Player.HurtBoxComponent;
		HealthComponent ??= Player.HealthComponent;
		ManaComponent ??= Player.ManaComponent;
		// Read before the first sync, whose maximum may clamp the component's default and report it.
		float mana = Stats.Mana;

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
		RestoreMana(mana);
	}

	public void SyncStats()
	{
		MovementComponent.Speed = Stats.Speed;
		HurtBoxComponent.Armor = Stats.Armor;
		HealthComponent.SetHealth(Stats.Health, Stats.MaxHealth);
		// The component owns current mana; stats only set its limits (see RestoreMana).
		ManaComponent.MaxMana = Stats.MaxMana;
		ManaComponent.RegenPerSecond = Stats.ManaRegenPerSecond;

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

	/// <summary>
	/// Hands <paramref name="mana"/> to the component, which owns current mana from then on and reports every change into
	/// <see cref="PlayerStats.Mana"/> for saving and the HUD. Runs at spawn and after a load, once the maximum is final.
	/// </summary>
	public void RestoreMana(float mana)
	{
		ManaComponent.SetMana(mana, Stats.MaxMana);
		// Copied even when nothing changed, since the component only reports changes.
		Stats.Mana = ManaComponent.CurrentMana;
	}

	// Like health, only the current value flows back; the maximum is derived from stats.
	private void OnManaChanged(float mana, float maxMana) => Stats.Mana = mana;

	private void OnXpRewarded(int xp)
	{
		Stats.AddXp(xp);
	}
}