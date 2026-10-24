using Godot;
using System;

/// <summary>
/// Drives a <see cref="ScreenEffect"/> from a <see cref="HealthComponent"/>: the effect stays off while the
/// health fraction is above <see cref="Threshold"/> and ramps to full intensity as health approaches zero.
/// Used for the near-death overlay; works for any health-linked screen effect.
/// </summary>
[GlobalClass]
public partial class HealthScreenEffectDriver : Node
{
	private HealthComponent _health;
	private Action _unsubscribe = () => { };
	private float _target;

	/// <summary>Effect to drive. Required.</summary>
	[Export] public ScreenEffect Effect { get; set; }

	/// <summary>
	/// Observed health. Assign in the Inspector when health and effect share a scene, or at runtime once the
	/// owning actor (e.g. a procedurally spawned player) exists.
	/// </summary>
	[Export]
	public HealthComponent Health
	{
		get => _health;
		set
		{
			if (_health == value)
			{
				return;
			}

			_health = value;
			if (IsInsideTree())
			{
				Bind();
			}
		}
	}

	/// <summary>Health fraction at and below which the effect starts.</summary>
	[Export(PropertyHint.Range, "0.01,1,0.01")] public float Threshold { get; set; } = 0.35f;

	/// <summary>Seconds used to fade toward each new intensity so damage spikes read smoothly.</summary>
	[Export(PropertyHint.Range, "0,2,0.05,suffix:s")] public float ResponseTime { get; set; } = 0.35f;

	public override void _Ready()
	{
		if (Effect == null)
		{
			GD.PushError($"{Name}: {nameof(HealthScreenEffectDriver)} requires an {nameof(Effect)}.");
		}
	}

	public override void _EnterTree() => Bind();

	/// <summary>Replaces the observed health's subscription (released automatically when leaving the tree) and syncs.</summary>
	private void Bind()
	{
		_unsubscribe();
		_unsubscribe = this.SubscribeUntilExit(_health, health => health.HealthChanged += OnHealthChanged, health => health.HealthChanged -= OnHealthChanged);
		if (IsInstanceValid(_health))
		{
			OnHealthChanged(_health.CurrentHealth, _health.MaxHealth);
		}
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		if (Effect == null || maxHealth <= 0f)
		{
			return;
		}

		float fraction = Mathf.Clamp(currentHealth / maxHealth, 0f, 1f);
		float target = 1f - Mathf.Clamp(fraction / Threshold, 0f, 1f);

		// An unchanged target (e.g. every hit while healthy) keeps any running fade instead of restarting it.
		if (Mathf.IsEqualApprox(target, _target))
		{
			return;
		}

		_target = target;
		Effect.FadeTo(target, ResponseTime);
	}
}