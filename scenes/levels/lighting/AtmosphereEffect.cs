using Godot;

/// <summary>Scene-level extension seam for floor atmosphere. Inactive effects neither prepare GPU data nor simulate.</summary>
[GlobalClass]
public abstract partial class AtmosphereEffect : Node3D
{
	[Export]
	public bool Enabled
	{
		get;
		set { field = value; if (IsNodeReady()) ApplyActive(); }
	} = true;

	protected AtmosphereContext Context { get; private set; }
	protected bool IsActive => _prepared && _requestedActive && Enabled;
	private bool _requestedActive;
	private bool _prepared;

	public override void _Ready() => ApplyActive();

	/// <summary>Supplies immutable floor context; preparation waits until the effect is enabled in a lit world.</summary>
	public void Initialize(AtmosphereContext context)
	{
		Clear();
		Context = context;
	}

	public void SetActive(bool active)
	{
		_requestedActive = active;
		ApplyActive();
	}

	public void Clear()
	{
		SetActive(false);
		if (_prepared) Release();
		_prepared = false;
		Context = null;
	}

	private void ApplyActive()
	{
		bool active = _requestedActive && Enabled && Context != null;
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