using Godot;

/// <summary>
/// Component that handles the death of a character (enemies).
/// </summary>
public partial class DeathComponent : Node
{
	[Export] public HealthComponent HealthComponent;
	[Export] public LootTableComponent LootTableComponent;

	[Export] public int Xp { get; set; } = 100;
	[Export] public float Delay { get; set; } = 2.0f;

	public override void _Ready()
	{
		this.SubscribeUntilExit(
			HealthComponent,
			healthComponent => healthComponent.Died += OnDied,
			healthComponent => healthComponent.Died -= OnDied);
	}

	private void OnDied()
	{
		// Disable collision detection for character
		var owner = GetOwner<CharacterBody3D>();

		GD.Print($"{owner.Name} died!");
		owner.CollisionLayer = 0;
		owner.CollisionMask = 1; // only collide with the floor
		owner.SetPhysicsProcess(false);

		// Drop loot and add XP
		LootTableComponent?.DropLoot();
		GameManager.Instance.Player?.Stats.AddXp(Xp);

		// Wait for death animation to finish
		GetTree().CreateTimer(Delay).Connect("timeout", Callable.From(() =>
		{
			GD.Print($"{owner.Name} is destroyed!");
			owner.QueueFree();
		}));
	}
}
