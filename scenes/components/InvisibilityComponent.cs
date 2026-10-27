using Godot;

/// <summary>Gameplay cloak state adapter; the normal actor-effect host owns presentation, fading and material restoration.</summary>
[GlobalClass]
public partial class InvisibilityComponent : Node
{
	[Signal] public delegate void ChangedEventHandler();
	[Export] public WorldEffectHost Host { get; set; }
	[Export] public WorldEffectDefinition Effect { get; set; }
	[Export] public bool StartInvisible { get; set; }
	public Node3D Target => Host?.SurfaceController?.Target;
	public bool IsInvisible => Host?.SurfaceController?.HasCloak == true;
	public float Amount => Host?.SurfaceController?.CloakAmount ?? 0;
	private bool _requested;
	public override void _Ready()
	{
		if (Host?.SurfaceController?.Target == null || Effect?.Scene == null)
		{ GD.PushError($"{Name}: assign the actor effect host and invisibility definition."); return; }
		this.SubscribeUntilExit(Host.SurfaceController, c => c.StateChanged += OnStateChanged, c => c.StateChanged -= OnStateChanged);
		if (Host.IsNodeReady()) Initialize();
		else this.SubscribeUntilExit(Host, h => h.Ready += Initialize, h => h.Ready -= Initialize);
	}
	private void Initialize() { if (StartInvisible) SetInvisible(true, true); }
	private void OnStateChanged() { if (!IsInvisible) _requested = false; EmitSignalChanged(); }
	/// <summary>Request the shared sustained effect. Other presentation and targeting use the same cloak state.</summary>
	public void SetInvisible(bool invisible, bool immediate = false)
	{
		if (Host == null || Effect == null || !IsInsideTree() || (invisible && Host.IsStopped)) return;
		if (!immediate && _requested == invisible) return;
		_requested = invisible;
		var result = Host.SetSustained(this, Effect, invisible, 0, immediate: immediate);
		if (result == WorldEffectResult.Rejected) _requested = false;
	}
	public override void _ExitTree()
	{
		_requested = false;
		if (IsInstanceValid(Host) && Host.IsInsideTree()) Host.SetSustained(this, Effect, false, 0);
	}
}