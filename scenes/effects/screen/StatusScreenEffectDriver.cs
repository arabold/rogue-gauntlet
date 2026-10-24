using Godot;

/// <summary>
/// Drives a <see cref="ScreenEffect"/> from one <see cref="StatusCondition"/> on a <see cref="BuffController"/>:
/// the effect fades in while any active buff carries the condition and fades out once the last one ends.
/// Status sources only apply buffs; they never touch effects. Each status with a screen effect gets its own
/// driver in <c>screen_effects.tscn</c>.
/// </summary>
[GlobalClass]
public partial class StatusScreenEffectDriver : Node
{
	private BuffController _buffs;

	/// <summary>Effect to drive. Required.</summary>
	[Export] public ScreenEffect Effect { get; set; }

	/// <summary>Condition that shows the effect. Required; reference the shared condition <c>.tres</c>.</summary>
	[Export] public StatusCondition Condition { get; set; }

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

			Unsubscribe();
			_buffs = value;
			if (IsInsideTree())
			{
				Subscribe();
			}
		}
	}

	/// <summary>Seconds to fade in when the condition starts.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float FadeInTime { get; set; } = 0.5f;

	/// <summary>Seconds to fade out after the condition ends, so recovery reads gradually.</summary>
	[Export(PropertyHint.Range, "0,5,0.05,suffix:s")] public float FadeOutTime { get; set; } = 1.5f;

	public override void _Ready()
	{
		if (Effect == null)
		{
			GD.PushError($"{Name}: {nameof(StatusScreenEffectDriver)} requires an {nameof(Effect)}.");
		}

		if (Condition == null)
		{
			GD.PushError($"{Name}: {nameof(StatusScreenEffectDriver)} requires a {nameof(Condition)}.");
		}
	}

	public override void _EnterTree() => Subscribe();

	public override void _ExitTree() => Unsubscribe();

	private void Subscribe()
	{
		if (IsInstanceValid(_buffs))
		{
			_buffs.ConditionGained += OnConditionGained;
			_buffs.ConditionLost += OnConditionLost;
			SetActive(_buffs.HasCondition(Condition));
		}
		else if (Effect != null && Effect.Intensity > 0f)
		{
			SetActive(false);
		}
	}

	private void Unsubscribe()
	{
		if (IsInstanceValid(_buffs))
		{
			_buffs.ConditionGained -= OnConditionGained;
			_buffs.ConditionLost -= OnConditionLost;
		}
	}

	private void OnConditionGained(StatusCondition condition)
	{
		if (condition == Condition)
		{
			SetActive(true);
		}
	}

	private void OnConditionLost(StatusCondition condition)
	{
		if (condition == Condition)
		{
			SetActive(false);
		}
	}

	private void SetActive(bool active)
	{
		Effect?.FadeTo(active ? 1f : 0f, active ? FadeInTime : FadeOutTime);
	}
}