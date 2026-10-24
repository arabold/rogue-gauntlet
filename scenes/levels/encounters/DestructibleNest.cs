using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>A finite, damageable summon source. Bound minions collapse when the nest is destroyed.</summary>
public partial class DestructibleNest : StaticBody3D, IDamageable
{
	[Export] public PackedScene MinionScene { get; set; }
	[Export] public int MaximumAlive { get; set; } = 2;
	[Export] public int TotalSummons { get; set; } = 4;
	[Export] public double SpawnInterval { get; set; } = 5;
	[Export] public float ActivationRange { get; set; } = 18;
	public int SummonsReleased { get; private set; }
	public int LivingMinions => _minions.Count(m => GodotObject.IsInstanceValid(m) && !m.IsQueuedForDeletion() && !m.IsDead);
	private readonly List<EnemyBase> _minions = [];
	private Level _level;
	private HealthComponent _health;
	private HurtBoxComponent _hurtbox;
	private double _remaining = 1;
	private string SourceId => GetMeta("persistent_id", "").AsString();

	public override void _Ready()
	{
		_level = this.GetAncestorOrNull<Level>();
		_health = GetNode<HealthComponent>("HealthComponent");
		_hurtbox = GetNode<HurtBoxComponent>("HurtBoxComponent");
		this.SubscribeUntilExit(
			_health,
			h => h.Died += OnDestroyed,
			h => h.Died -= OnDestroyed);

		var xray = new OcclusionXrayComponent
		{
			Name = "OcclusionXrayComponent",
			Category = XrayCategory.Monster,
			XrayColor = new Color(1f, 0.32f, 0.32f, 0.85f),
		};
		AddChild(xray);
	}

	public override void _Process(double delta)
	{
		if (_health.IsDead || _level == null) return;
		if (SummonsReleased >= TotalSummons && LivingMinions == 0)
		{
			SetProcess(false);
			return;
		}

		_remaining -= delta;
		if (_remaining > 0) return;
		_remaining = SpawnInterval;
		if (!_level.MapGenerator.IsRevealedAt(GlobalPosition)) return;
		var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (player == null || player.GlobalPosition.DistanceSquaredTo(GlobalPosition) > ActivationRange * ActivationRange) return;
		ReleaseMinion();
	}

	/// <summary>Reconstruct finite summon slots using stable IDs; killed slots never refill.</summary>
	public EnemyBase ReleaseMinion()
	{
		if (_health.IsDead || MinionScene == null || _level == null || string.IsNullOrEmpty(SourceId)
			|| LivingMinions >= MaximumAlive) return null;
		string id;
		do
		{
			if (SummonsReleased >= TotalSummons) return null;
			id = $"{SourceId}:summon:{SummonsReleased++}";
		} while (GameSession.Instance?.IsEntityCleared(id) == true);
		using var rng = GenerationRandom.Create(_level.MapGenerator.Seed, GenerationRandom.Monsters, id);
		float angle = rng.RandfRange(0, Mathf.Tau);
		var position = _level.MapGenerator.FindFreeSpawnPositionNear(GlobalPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2);
		var minion = MinionScene.Instantiate<EnemyBase>();
		minion.SetMeta("persistent_id", id);
		_level.AddWorldNode(minion, position);
		_minions.Add(minion);
		return minion;
	}

	public void TakeDamage(float amount, Vector3 direction, Node attacker = null,
		DamageSourceFlags faction = DamageSourceFlags.None) => _hurtbox.TakeDamage(amount, direction, attacker, faction);

	private void OnDestroyed()
	{
		GameSession.Instance?.ClearEntity(SourceId);
		foreach (var minion in _minions)
			if (GodotObject.IsInstanceValid(minion) && !minion.IsDead) minion.QueueFree();
		QueueFree();
	}
}