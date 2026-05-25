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
	[Export] public WeaponSwingAttack MeleeAttack { get; set; }
	[Export] public RangedWeaponAttack RangedAttack { get; set; }

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

		SyncStats();
	}

	public void SyncStats()
	{
		MovementComponent.Speed = Stats.Speed;
		HurtBoxComponent.Armor = Stats.Armor;
		HurtBoxComponent.Evasion = Stats.Evasion;
		HealthComponent.SetHealth(Stats.Health, Stats.MaxHealth);
		SyncAttackStats();

		SignalBus.EmitPlayerStatsChanged(Stats);
	}

	private void SyncAttackStats()
	{
		if (!Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var item))
		{
			return;
		}

		if (item?.Item is RangedWeapon rangedWeapon)
		{
			RangedAttack.Accuracy = Stats.Accuracy;
			RangedAttack.MinDamage = Stats.MinDamage;
			RangedAttack.MaxDamage = Stats.MaxDamage;
			RangedAttack.CritChance = Stats.CritChance;
			RangedAttack.ProjectileSpeed = rangedWeapon.ProjectileSpeed;
			RangedAttack.Range = rangedWeapon.Range;
			RangedAttack.AimingAngle = rangedWeapon.AimingAngle;
		}
		else if (item?.Item is Weapon)
		{
			MeleeAttack.Accuracy = Stats.Accuracy;
			MeleeAttack.MinDamage = Stats.MinDamage;
			MeleeAttack.MaxDamage = Stats.MaxDamage;
			MeleeAttack.CritChance = Stats.CritChance;
		}
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		Stats.Health = health;
		Stats.BaseMaxHealth = maxHealth;
	}
}
