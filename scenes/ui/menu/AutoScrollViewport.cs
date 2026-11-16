using Godot;

/// <summary>
/// Fixes a single UI text area's bounds while clipping overflow and smoothly auto-scrolling its child
/// when the content exceeds the viewport width or height. Prevents long titles or modifier lists from
/// resizing parent containers in menus.
/// </summary>
[GlobalClass]
public partial class AutoScrollViewport : Control
{
	[Export] public bool ScrollHorizontal { get; set; }
	[Export] public bool ScrollVertical { get; set; }
	[Export(PropertyHint.Range, "0,5,0.1,suffix:s")] public float EdgePauseSeconds { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "5,200,1,suffix:px/s")] public float ScrollSpeed { get; set; } = 48f;

	private float _elapsed;

	public override void _Ready()
	{
		ClipContents = true;
		ProcessMode = ProcessModeEnum.Always;
		Resized += ApplyScroll;
		VisibilityChanged += ApplyScroll;
		if (GetChildOrNull<Control>(0) is { } content)
		{
			content.MinimumSizeChanged += ResetScroll;
		}
		ResetScroll();
	}

	/// <summary>Rewinds scroll to the start edge and updates layout once text or child controls change.</summary>
	public void ResetScroll()
	{
		_elapsed = 0f;
		ApplyScroll();
		if (IsInsideTree())
		{
			Callable.From(ApplyScroll).CallDeferred();
		}
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		ApplyScroll();
	}

	private void ApplyScroll()
	{
		Control content = GetChildOrNull<Control>(0);
		if (content == null)
		{
			SetProcess(false);
			return;
		}

		Vector2 minSize = content.GetCombinedMinimumSize();
		float width = ScrollHorizontal ? Mathf.Max(Size.X, minSize.X) : Size.X;
		float height = ScrollVertical ? Mathf.Max(Size.Y, minSize.Y) : Size.Y;
		content.Size = new Vector2(width, height);

		float overflowX = ScrollHorizontal ? minSize.X - Size.X : 0f;
		float overflowY = ScrollVertical ? minSize.Y - Size.Y : 0f;
		content.Position = new Vector2(ComputeOffset(overflowX), ComputeOffset(overflowY));
		SetProcess(IsVisibleInTree() && (overflowX > 1f || overflowY > 1f));
	}

	private float ComputeOffset(float overflow)
	{
		if (overflow <= 1f)
		{
			return 0f;
		}

		float travel = overflow / Mathf.Max(1f, ScrollSpeed);
		float halfCycle = EdgePauseSeconds + travel;
		float cycle = halfCycle * 2f;
		if (cycle <= 0f)
		{
			return 0f;
		}

		float t = _elapsed % cycle;
		float progress;
		if (t < EdgePauseSeconds)
		{
			progress = 0f;
		}
		else if (t < halfCycle)
		{
			progress = (t - EdgePauseSeconds) / travel;
		}
		else if (t < halfCycle + EdgePauseSeconds)
		{
			progress = 1f;
		}
		else
		{
			progress = 1f - (t - halfCycle - EdgePauseSeconds) / travel;
		}

		return -overflow * Mathf.Clamp(progress, 0f, 1f);
	}
}