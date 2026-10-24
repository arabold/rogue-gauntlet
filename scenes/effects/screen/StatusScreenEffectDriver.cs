using Godot;
using Godot.Collections;
using System;

/// <summary>
/// Drives a <see cref="ScreenEffect"/> from <see cref="StatusCondition"/>s on a <see cref="BuffController"/>.
/// Each listed condition shows the effect at its own intensity, and the effect follows the strongest
/// active one: the poison overlay plays fully while poisoned and faintly while only confused. Status
/// sources only apply buffs; they never touch effects. Each effect that reacts to statuses gets its
/// own driver in <c>screen_effects.tscn</c>.
/// </summary>
[GlobalClass]
public partial class StatusScreenEffectDriver : Node
{
	private BuffController _buffs;
	private Action _unsubscribe = () => { };
	private float _target;

	/// <summary>Effect to drive. Required.</summary>
	[Export] public ScreenEffect Effect { get; set; }

	/// <summary>
	/// Condition → effect intensity while that condition is active. Required; reference the shared
	/// condition <c>.tres</c> files.
	/// </summary>
	[Export] public Dictionary<StatusCondition, float> Conditions { get; set; } = [];

	/// <summary>
	/// Observed buffs. Runtime context, normally assigned by <see cref="ScreenEffectsLayer.Bind"/> once the player
	/// spawns. Each assignment syncs the effect to the new controller's state; unassigning fades the effect out.
	/// </summary>
	[Export]
	public BuffController Buffs
	{
		get => _buffs;
		set
		{
			if (_buffs == value)
			{
				return;
			}

			_buffs = value;
			if (IsInsideTree())
			{
				Bind();
			}
		}
	}

	/// <summary>Seconds to fade in when a condition starts or a stronger one takes over.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float FadeInTime { get; set; } = 0.5f;

	/// <summary>Seconds to fade down after a condition ends, so recovery reads gradually.</summary>
	[Export(PropertyHint.Range, "0,5,0.05,suffix:s")] public float FadeOutTime { get; set; } = 1.5f;

	public override void _Ready()
	{
		if (Effect == null)
		{
			GD.PushError($"{Name}: {nameof(StatusScreenEffectDriver)} requires an {nameof(Effect)}.");
		}

		if (Conditions.Count == 0)
		{
			GD.PushError($"{Name}: {nameof(StatusScreenEffectDriver)} requires at least one entry in {nameof(Conditions)}.");
		}
	}

	public override void _EnterTree() => Bind();

	/// <summary>Replaces the observed controller's subscription (released automatically when leaving the tree) and syncs.</summary>
	private void Bind()
	{
		_unsubscribe();
		_unsubscribe = this.SubscribeUntilExit(_buffs, buffs => buffs.BuffsChanged += Sync, buffs => buffs.BuffsChanged -= Sync);
		Sync();
	}

	/// <summary>Fades the effect toward the intensity of the strongest active condition (0 when none).</summary>
	private void Sync()
	{
		float target = 0f;
		if (IsInstanceValid(_buffs))
		{
			foreach ((StatusCondition condition, float intensity) in Conditions)
			{
				if (_buffs.HasCondition(condition))
				{
					target = Mathf.Max(target, intensity);
				}
			}
		}

		// An unchanged target keeps any fade already heading there instead of restarting it.
		if (Effect == null || Mathf.IsEqualApprox(target, _target))
		{
			return;
		}

		Effect.FadeTo(target, target > _target ? FadeInTime : FadeOutTime);
		_target = target;
	}
}