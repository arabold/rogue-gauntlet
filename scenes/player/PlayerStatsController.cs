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

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		Stats ??= Player.Stats;
		Inventory ??= Player.Inventory;
		MovementComponent ??= Player.MovementComponent;
		HurtBoxComponent ??= Player.HurtBoxComponent;
		HealthComponent ??= Player.HealthComponent;

		this.SubscribeUntilExit(
			HealthComponent,
			healthComponent => healthComponent.HealthChanged += OnHealthChanged,
			healthComponent => healthComponent.HealthChanged -= OnHealthChanged);
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
		HurtBoxComponent.Evasion = Stats.Evasion;
		HealthComponent.SetHealth(Stats.Health, Stats.MaxHealth);

		SignalBus.EmitPlayerStatsChanged(Stats);
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		Stats.Health = health;
		Stats.BaseMaxHealth = maxHealth;
	}

	private void OnXpRewarded(int xp)
	{
		Stats.AddXp(xp);
	}
}
