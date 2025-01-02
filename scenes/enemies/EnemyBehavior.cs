using Godot;
using System;
using System.Collections.Generic;

public partial class EnemyBehavior : Node3D, IDamageable
{
    public enum BehaviorState
    {
        Sleeping,
        Idle,
        Guarding,
        Patrolling,
        Searching,
        Chasing,
        Fleeing,
        Dead
    }

    public enum ActionState
    {
        None,
        Hit,
        Attacking,
        Dying
    }

    private static readonly Dictionary<BehaviorState, string> BehaviorAnimations = new()
    {
        { BehaviorState.Sleeping, "Lie_Idle" },
        { BehaviorState.Idle, "Idle" },
        { BehaviorState.Guarding, "Walking_A" },
        { BehaviorState.Patrolling, "Walking_A" },
        { BehaviorState.Searching, "Walking_A" },
        { BehaviorState.Chasing, "Walking_A" },
        { BehaviorState.Fleeing, "Walking_A" },
        { BehaviorState.Dead, "Death_A" }
    };

    private static readonly Dictionary<ActionState, string> ActionAnimations = new()
    {
        { ActionState.None, null },
        { ActionState.Hit, "Hit_A" },
        { ActionState.Attacking, "1H_Melee_Attack_Chop" },
        { ActionState.Dying, "Death_A" }
    };

    private AnimationTree _animationTree;
    private AnimationNodeStateMachinePlayback _animationStateMachine;
    private int _currentHitPoints;
    private int _maxHitPoints;

    public BehaviorState CurrentBehavior { get; private set; } = BehaviorState.Guarding;
    public ActionState CurrentAction { get; private set; } = ActionState.None;

    public override void _Ready()
    {
        base._Ready();

        _animationTree = GetNode<AnimationTree>("AnimationTree");
        if (_animationTree == null)
        {
            GD.PrintErr("AnimationTree not found!");
            GetParent().QueueFree();
            return;
        }

        _animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
        UpdateAnimation();

        _maxHitPoints = ((Enemy)GetParent()).MaxHitPoints;
        _currentHitPoints = _maxHitPoints;
    }

    private void UpdateAnimation()
    {
        string targetAnimation = CurrentAction != ActionState.None
            ? ActionAnimations[CurrentAction]
            : BehaviorAnimations[CurrentBehavior];

        _animationStateMachine.Travel(targetAnimation);
    }

    public void SetBehavior(BehaviorState newBehavior)
    {
        if (CurrentBehavior == BehaviorState.Dead)
        {
            return;
        }

        if (CurrentBehavior != newBehavior)
        {
            CurrentBehavior = newBehavior;
            if (CurrentAction == ActionState.None)
            {
                UpdateAnimation();
            }
        }
    }

    public void SetAction(ActionState newAction)
    {
        if (CurrentBehavior == BehaviorState.Dead)
        {
            return;
        }

        if (CurrentAction != newAction)
        {
            CurrentAction = newAction;
            UpdateAnimation();
        }
    }

    public void TakeDamage(int amount)
    {
        // Prevent taking damage if already dead
        if (CurrentBehavior == BehaviorState.Dead)
        {
            return;
        }

        _currentHitPoints -= amount;

        SetAction(ActionState.Hit);
        SpawnHitEffect();

        if (_currentHitPoints <= 0)
        {
            Die();
        }
        else
        {
            // Reset action state after a short delay
            GetTree().CreateTimer(0.3f).Connect("timeout", Callable.From(() => SetAction(ActionState.None)));
        }
    }

    private void Die()
    {
        SetAction(ActionState.Dying);
        SetBehavior(BehaviorState.Dead);

        // Stop movement immediately
        ((Enemy)GetParent()).Velocity = Vector3.Zero;

        // Wait for death animation to finish
        GetTree().CreateTimer(1.0f).Connect("timeout", Callable.From(() =>
        {
            GD.Print($"{GetParent().Name} is destroyed!");
            GetParent().QueueFree();
        }));
    }

    private void SpawnHitEffect()
    {
        // Load the HitEffect scene
        var hitEffect = ResourceLoader.Load<PackedScene>("res://scenes/effects/hit_effect.tscn").Instantiate<GpuParticles3D>();
        hitEffect.GlobalTransform = GlobalTransform; // Position the effect at the enemy's location
        hitEffect.OneShot = true;

        // Add to the scene
        GetParent().GetParent().AddChild(hitEffect);
    }
}
