using Godot;

/// <summary>Scene-level extension seam for floor atmosphere. Inactive effects neither prepare GPU data nor simulate.</summary>
[GlobalClass]
public abstract partial class AtmosphereEffect : Node3D
{
	/// <summary>Optional diagnostic category; new scenes opt into a switch without controller changes.</summary>
	[Export] public EnvironmentalEffect Category { get; set; }

	[Export]
	public bool Enabled
	{
		get;
		set { field = value; if (IsNodeReady()) ApplyActive(); }
	} = true;

	/// <summary>Physical effects such as dripping persist when environmental lighting is off.</summary>
	[Export]
	public bool ActiveInDarkness
	{
		get;
		set { field = value; if (IsNodeReady()) ApplyActive(); }
	}

	private bool _isDark;
	protected AtmosphereContext Context { get; private set; }
	protected bool IsActive => _prepared && ShouldActivate;
	private bool ShouldActivate => _requestedActive && (!_isDark || ActiveInDarkness) && Enabled && Context != null;
	private bool _requestedActive;
	private bool _prepared;

	public override void _Ready() => ApplyActive();

	/// <summary>Supplies immutable floor context; preparation waits until the effect is enabled and its lighting policy allows it.</summary>
	public void Initialize(AtmosphereContext context)
	{
		Clear();
		Context = context;
	}

	public void SetActive(bool active, bool isDark = false)
	{
		_requestedActive = active;
		_isDark = isDark;
		ApplyActive();
	}

	public void Clear()
	{
		SetActive(false);
		ReleasePreparation();
		Context = null;
	}

	/// <summary>Discards stale content and prepares the new settings when this effect can run.</summary>
	protected void InvalidatePreparation()
	{
		if (!IsNodeReady()) return;
		ReleasePreparation();
		ApplyActive();
	}

	private void ReleasePreparation()
	{
		if (_prepared) Release();
		_prepared = false;
	}

	private void ApplyActive()
	{
		bool active = ShouldActivate;
		if (active && !_prepared) _prepared = Prepare(Context);
		active &= _prepared;
		Visible = active;
		ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
		OnActiveChanged(active);
	}

	/// <summary>Validates this effect's own dependencies and creates only instance-local mutable resources.</summary>
	protected abstract bool Prepare(AtmosphereContext context);
	protected virtual void OnActiveChanged(bool active) { }
	protected virtual void Release() { }
	public override void _ExitTree() => Clear();
}