using Godot;

public partial class HealthComponent : Node
{
    [Export] public int MaxHitPoints { get; set; } = 10;
    [Export] public PackedScene HitEffect { get; set; }

    private Node3D _owner;
    private EnemyBehavior _enemyBehavior;
    private int _currentHitPoints;

    public override void _Ready()
    {
        _currentHitPoints = MaxHitPoints;

        _owner = GetParent<Node3D>(); // Assume the parent is the Enemy node
        _enemyBehavior = _owner.GetNode<EnemyBehavior>("EnemyBehavior");
        if (_enemyBehavior == null)
        {
            GD.PrintErr("EnemyBehavior node not found!");
            QueueFree();
            return;
        }
    }

    public void TakeDamage(int amount)
    {
        if (_currentHitPoints > 0)
        {
            _currentHitPoints -= amount;
            SpawnHitEffect();

            if (_currentHitPoints <= 0)
            {
                Die();
            }
        }
    }

    private void Die()
    {
        if (_enemyBehavior != null)
        {
            _enemyBehavior.Die();
        }
    }

    private void SpawnHitEffect()
    {
        if (HitEffect == null)
        {
            GD.PrintErr("HitEffectScene is not set!");
            return;
        }

        var hitEffect = HitEffect.Instantiate<GpuParticles3D>();
        hitEffect.GlobalTransform = GetParent<Node3D>().GlobalTransform; // Position the effect at the enemy's location
        hitEffect.OneShot = true;

        // Add to the scene
        GetParent().GetParent().AddChild(hitEffect);
    }
}
