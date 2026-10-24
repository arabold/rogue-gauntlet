using Godot;

/// <summary>
/// Applies the player's game-slowing buffs to <see cref="Engine.TimeScale"/>: the slowest active
/// <see cref="TimeScaleBuff"/> wins, and normal speed returns once none is active. It only writes the
/// global time scale while it is slowing the game (so debug slow motion elsewhere is left alone), and
/// restores normal speed when it leaves the tree (floor travel, quitting a run), so a slowness can
/// never outlive the scene that applied it.
/// </summary>
public partial class TimeScaleComponent : Node
{
	private bool _slowing;

	/// <summary>Observed buffs. Required; assign the player's sibling <see cref="BuffController"/>.</summary>
	[Export] public BuffController Buffs { get; set; }

	public override void _Ready()
	{
		if (Buffs == null)
		{
			GD.PushError($"{Name}: {nameof(TimeScaleComponent)} requires {nameof(Buffs)}.");
			return;
		}

		this.SubscribeUntilExit(Buffs, buffs => buffs.BuffsChanged += Apply, buffs => buffs.BuffsChanged -= Apply);
		Apply();
	}

	public override void _ExitTree() => SetScale(1f);

	private void Apply()
	{
		float scale = 1f;
		foreach (ActiveBuff active in Buffs.ActiveBuffs)
		{
			if (active.Buff is TimeScaleBuff slow)
			{
				scale = Mathf.Min(scale, slow.TimeScale);
			}
		}

		SetScale(scale);
	}

	private void SetScale(float scale)
	{
		if (scale < 1f)
		{
			Engine.TimeScale = scale;
			_slowing = true;
		}
		else if (_slowing)
		{
			Engine.TimeScale = 1.0;
			_slowing = false;
		}
	}
}