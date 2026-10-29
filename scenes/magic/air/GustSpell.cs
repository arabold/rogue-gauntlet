using Godot;
using System.Linq;

/// <summary>Focus-owned area contact and visual for Gust; casting is supplied by the caller.</summary>
public partial class GustSpell : Node, ICombatSpell
{
	public string DisplayName => "Gust";
	public int LevelCount => 3;
	[Export] public GustDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	private readonly AreaCastLifecycle _cast = new();
	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Damage; SetPhysicsProcess(false); }
	public override void _PhysicsProcess(double delta) { if (!_cast.Advance(delta)) SetPhysicsProcess(false); }

	/// <summary>Damage and push each visible hostile actor once; an empty area still plays the wind.</summary>
	public bool Cast(Node3D caster, int level, RandomNumberGenerator random)
	{
		if (Definition?.IsValidLevel(level) != true || VisualScene == null) return false;
		float radius = Definition.RadiusByLevel[level - 1];
		float damage = Definition.DamageByLevel[level - 1], push = Definition.PushByLevel[level - 1];
		AttackDefenseTraits defenseTraits = Definition.DefenseTraits;
		if (!AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, radius, out var cast)) return false;
		var node = VisualScene.Instantiate();
		if (node is not VisualEffectInstance visual || !IsValidVisual(visual))
		{ node?.Free(); return false; }
		var visibility = AreaVisibility.Capture(cast.World, cast.Origin, radius);
		VisualRoot.AddChild(visual);
		visual.GlobalPosition = cast.Origin;
		visual.Scale = new Vector3(radius / 9f, 1, radius / 9f);
		visual.SetVisibility(visibility);
		_cast.Track(visual);
		_cast.Schedule(cast, Definition.ContactDelay, damage, defenseTraits, push, element: DamageElement.Air);
		return true;
	}

	private static bool IsValidVisual(VisualEffectInstance visual)
	{
		var timeline = visual.Timeline;
		return timeline != null && !timeline.Sustained && float.IsFinite(timeline.Duration) && timeline.Duration > 0
			&& float.IsFinite(timeline.FadeIn) && float.IsFinite(timeline.FadeOutStart)
			&& timeline.Surfaces is { Length: > 0 } && timeline.Surfaces.All(surface => surface?.MaterialOverride is ShaderMaterial)
			&& timeline.Lights != null && timeline.Lights.All(light => light != null)
			&& timeline.Emitters != null && timeline.Emitters.All(emitter => emitter?.MaterialOverride is ShaderMaterial);
	}

	/// <summary>Stop this focus's remaining visual and invalidate any later contacts.</summary>
	public void Cancel()
	{
		SetPhysicsProcess(false);
		AreaCastLifecycle.ClearVisuals(_cast.Cancel());
	}
	public override void _ExitTree() => Cancel();
}
