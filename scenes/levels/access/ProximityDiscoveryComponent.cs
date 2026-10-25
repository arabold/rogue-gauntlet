using Godot;

/// <summary>
/// Periodically gives each nearby living player with clear sight a stat-based chance to discover
/// <see cref="Subject"/>. Add it to a hidden object's scene and wire the subject in the Inspector; leaving
/// it out (or disabling it) makes the object discoverable only through explicit mechanisms.
/// </summary>
[Tool]
[GlobalClass]
public partial class ProximityDiscoveryComponent : Node
{
	[Signal] public delegate void DiscoveredEventHandler();
	/// <summary>Node implementing <see cref="IProximityDiscoverable"/>.</summary>
	[Export] public Node Subject { get; set; }
	/// <summary>Detection tuning. Generated content may supply the level's profile through <see cref="Initialize"/>.</summary>
	[Export] public SecretDetectionProfile Profile { get; set; }
	private IProximityDiscoverable _subject;
	private RandomNumberGenerator _random;
	private Cooldown _scan;
	public override void _Ready()
	{
		// Tool only so tool scripts can reference it; discovery never runs in the editor.
		if (Engine.IsEditorHint()) { SetPhysicsProcess(false); return; }
		_subject = Subject as IProximityDiscoverable;
		if (_subject == null) GD.PushError($"{Name}: Subject must implement {nameof(IProximityDiscoverable)}.");
		Profile?.Validate();
		_scan.Start(Profile?.Interval ?? 0);
		_random ??= new RandomNumberGenerator { Seed = 42 };
	}
	/// <summary>Seeds the roll stream from the subject's stable identity, so discovery replays for a given floor.</summary>
	public void Initialize(ulong seed, SecretDetectionProfile profile = null)
	{
		if (profile != null)
		{
			profile.Validate();
			Profile = profile;
		}
		_random = new RandomNumberGenerator { Seed = seed };
		_scan.Start(Profile?.Interval ?? 0);
	}
	public override void _PhysicsProcess(double delta)
	{
		// The clock runs only while findable, so hidden time elsewhere never banks an instant roll.
		if (_subject?.IsDiscoverable != true || Profile == null || !_scan.Tick(delta)) return;
		_scan.Start(Profile.Interval);
		foreach (var node in GetTree().GetNodesInGroup("player"))
		{
			if (node is not Player player || player.IsDead) continue;
			var observer = player.GlobalPosition;
			if (!Profile.InRange(observer, _subject.DiscoveryAnchor) || !_subject.CanBeObservedFrom(observer)) continue;
			if (!WorldSight.IsClear(player.GetWorld3D(), observer + Vector3.Up * 2, _subject.SightTarget, _subject.AcceptsSightHit)) continue;
			if (_random.Randf() < Profile.Chance(Profile.Resolve(player.Stats)))
			{
				_subject.Discover();
				EmitSignal(SignalName.Discovered);
			}
			break;
		}
	}
}