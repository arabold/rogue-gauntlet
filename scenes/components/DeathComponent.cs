using Godot;

/// <summary>
/// Takes a monster out of the floor for good: it dies (<see cref="HealthComponent.Died"/>), paying its experience and
/// loot, or it leaves without dying (<see cref="Leave"/>). Either way a reloaded floor does not bring it back.
/// </summary>
[GlobalClass]
public partial class DeathComponent : Node
{
	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public LootTableComponent LootTableComponent { get; set; }

	[Export] public int Xp { get; set; } = 100;
	/// <summary>Practice instances suppress both reward outputs while retaining their authored dependencies.</summary>
	[Export] public bool RewardsEnabled { get; set; } = true;
	[Export] public float Delay { get; set; } = 2.0f;

	/// <summary>
	/// Seconds spent fading the corpse out after the death animation, so it eases away
	/// instead of popping. Set to 0 to despawn instantly.
	/// </summary>
	[Export] public float FadeDuration { get; set; } = 0.6f;

	/// <summary>Optional one-shot effect played where the actor dies, such as a mimic's splinters and teeth.</summary>
	[Export] public PackedScene DeathEffect { get; set; }
	/// <summary>Height above the actor's feet at which <see cref="DeathEffect"/> plays.</summary>
	[Export] public float DeathEffectHeight { get; set; } = 0.5f;
	private static readonly StringName PersistentIdMeta = "persistent_id";
	private Level _level;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		this.SubscribeUntilExit(
			HealthComponent,
			healthComponent => healthComponent.Died += OnDied,
			healthComponent => healthComponent.Died -= OnDied);
	}

	private void OnDied()
	{
		var owner = GetOwner<CharacterBody3D>();
		MarkCleared(owner);

		var definition = (owner as EnemyBase)?.Definition;
		CombatTrace.Write(owner, "actor.died", ("monster", definition?.DisplayName ?? owner.Name.ToString()),
			("threat", definition?.ThreatLevel), ("xp", RewardsEnabled ? Xp : 0));
		GD.Print($"{owner.Name} died!");
		// Disable collision detection for character
		owner.CollisionLayer = 0;
		owner.CollisionMask = 1; // only collide with the floor
		owner.SetPhysicsProcess(false);
		if (DeathEffect != null && IsInsideTree())
		{
			var effect = ScenePool.Spawn<Node3D>(DeathEffect, GetTree().CurrentScene ?? GetTree().Root);
			effect.GlobalPosition = owner.GlobalPosition + Vector3.Up * DeathEffectHeight;
			effect.ResetPhysicsInterpolation();
		}

		// Drop loot and announce rewards without depending on a specific player lookup.
		if (RewardsEnabled)
		{
			LootTableComponent?.DropLoot();
			SignalBus.EmitXpRewarded(Xp);
		}

		// Wait for the death animation, then ease the corpse out instead of popping it.
		GetTree().CreateTimer(Delay).Connect("timeout", Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(owner))
			{
				return;
			}

			GD.Print($"{owner.Name} is destroyed!");
			if (FadeDuration > 0f)
			{
				FadeOutComponent.FadeOutAndFree(owner, FadeDuration);
			}
			else
			{
				owner.QueueFree();
			}
		}));
	}

	/// <summary>
	/// Removes the monster without killing it: no death, experience or loot. It stops thinking, moving and taking hits at
	/// once, then fades through <paramref name="effect"/> on its effect host and is removed when that ends, or at once
	/// without one. A thief escaping with its loot leaves this way.
	/// </summary>
	public void Leave(WorldEffectDefinition effect = null)
	{
		var owner = GetOwner<EnemyBase>();
		MarkCleared(owner);
		owner.EnemyBehaviorComponent.AIEnabled = false;
		owner.MovementComponent.Stop();
		owner.CollisionLayer = 0;
		owner.HurtBoxComponent.Invulnerable = true;
		var host = owner.WorldEffects;
		var result = effect != null && host != null
			? host.PlayBurstConfigured(effect, (uint)owner.GetInstanceId(), instance =>
			{
				instance.Finished += Remove;
				return true;
			})
			: WorldEffectResult.Rejected;
		if (result != WorldEffectResult.Started) Remove();

		void Remove()
		{
			if (GodotObject.IsInstanceValid(owner)) owner.QueueFree();
		}
	}

	/// <summary>Records the monster's spawn as cleared, so a reloaded floor does not respawn it; unsaved spawns have no id.</summary>
	private void MarkCleared(Node owner)
	{
		string persistentId = owner.GetMeta(PersistentIdMeta, "").AsString();
		if (!string.IsNullOrEmpty(persistentId)) _level?.Run.MarkCleared(persistentId);
	}
}
