using Godot;

/// <summary>
/// Shows a floating number above an actor for every contact its hurtbox resolves: the damage dealt, or a
/// short word for dodges, parries and blocks. Numbers live in the world, so they outlast the actor's death.
/// </summary>
[GlobalClass]
public partial class DamageNumbersComponent : Node3D
{
	[Export] public HurtBoxComponent HurtBox { get; set; }
	[Export] public PackedScene NumberScene { get; set; }
	/// <summary>Where numbers start, usually the floating health bar above the head. Unassigned uses this node.</summary>
	[Export] public Node3D Anchor { get; set; }
	/// <summary>Optional cloak; an invisible actor shows no numbers, matching its hidden health bar.</summary>
	[Export] public InvisibilityComponent Invisibility { get; set; }
	/// <summary>Height above the anchor where numbers start.</summary>
	[Export] public float Lift { get; set; } = 0.3f;
	/// <summary>Screen-sideways spread between consecutive numbers, so rapid hits do not stack on one spot.</summary>
	[Export] public float Spread { get; set; } = 0.35f;

	// Consecutive numbers alternate sides on screen; a fixed pattern keeps presentation free of randomness.
	private static readonly float[] DriftPattern = [0f, 1f, -1f, 0.5f, -0.5f];
	private int _next;

	public override void _Ready()
	{
		if (HurtBox == null || NumberScene == null)
		{
			GD.PushError($"{GetParent()?.Name}/{Name}: assign HurtBox and NumberScene; the actor shows no damage numbers.");
			return;
		}
		this.SubscribeUntilExit(HurtBox, h => h.DamageResolved += OnDamageResolved, h => h.DamageResolved -= OnDamageResolved);
	}

	private void OnDamageResolved(int outcome, float amount, int emphasis)
	{
		if (Invisibility?.IsInvisible == true || !IsInsideTree()) return;
		if (!DamageNumber.TryDescribe((DamageOutcome)outcome, amount, (DamageEmphasis)emphasis, out string text, out var kind)) return;
		Vector3 right = GetViewport().GetCamera3D()?.GlobalBasis.X ?? Vector3.Right;
		Vector3 drift = right * (Spread * DriftPattern[_next++ % DriftPattern.Length]);
		var number = ScenePool.Spawn<DamageNumber>(NumberScene, GetTree().CurrentScene ?? GetTree().Root);
		number.Play(text, kind, (Anchor ?? this).GlobalPosition + Vector3.Up * Lift, drift);
	}
}
