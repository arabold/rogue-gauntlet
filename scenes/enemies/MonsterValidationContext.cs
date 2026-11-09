using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What a monster's Resources need to validate themselves against the actor they are assigned to: labelled error
/// collection, the actor's animation bindings and clips, and its wiring. Nodes are read through stored properties,
/// because the editor represents non-tool C# components as placeholders; the monster Resources are <c>[Tool]</c>, so
/// they validate as real instances in the editor as well as at spawn.
/// </summary>
public sealed class MonsterValidationContext
{
	private readonly List<string> _errors = [];
	private readonly Stack<string> _scopes = new();
	private readonly Resource[] _bindings;
	private readonly AnimationPlayer _player;

	public Node Actor { get; }
	public MonsterDefinition Definition { get; }
	public Node AnimationComponent { get; }
	public AnimationTree AnimationTree { get; }
	public Node AttackController { get; }
	public Node ActionGate { get; }
	/// <summary>Number of health phase transitions the actor's <see cref="HealthPhaseComponent"/> authors.</summary>
	public int PhaseCount { get; }
	public IReadOnlyList<string> Errors => _errors;

	public MonsterValidationContext(Node actor, MonsterDefinition definition)
	{
		Actor = actor;
		Definition = definition;
		AnimationComponent = actor.GetNodeOrNull<Node>("CombatAnimationComponent");
		AttackController = Object<Node>(actor, "AttackController");
		ActionGate = Object<Node>(actor, "ActionGate");
		var playerPath = AnimationComponent?.Get("AnimationPlayerPath").AsNodePath();
		_player = playerPath != null && !playerPath.IsEmpty ? AnimationComponent.GetNodeOrNull<AnimationPlayer>(playerPath) : null;
		var treePath = AnimationComponent?.Get("AnimationTreePath").AsNodePath();
		AnimationTree = treePath != null && !treePath.IsEmpty ? AnimationComponent.GetNodeOrNull<AnimationTree>(treePath) : null;
		var profile = Object<Resource>(AnimationComponent, "Profile");
		_bindings = profile?.Get("Bindings").AsGodotArray().Select(value => value.AsGodotObject() as Resource).Where(value => value != null).ToArray() ?? [];
		var phases = Object<Node>(actor, "HealthPhases");
		PhaseCount = phases?.Get("EnterHealthFractions").AsFloat32Array().Length ?? 0;
	}

	/// <summary>Record <paramref name="message"/> under the current scope when <paramref name="valid"/> is false.</summary>
	public bool Check(bool valid, string message)
	{
		if (!valid) _errors.Add(_scopes.Count == 0 ? message : $"{string.Join(".", _scopes.Reverse())}: {message}");
		return valid;
	}

	/// <summary>Record a scene-wiring problem; it names the node to fix, so it carries no Resource scope.</summary>
	public bool CheckWiring(bool valid, string message)
	{
		if (!valid && !_errors.Contains(message)) _errors.Add(message);
		return valid;
	}

	/// <summary>Label nested messages, e.g. <c>Behavior.Combat.Actions[1] 'bite'</c>.</summary>
	public IDisposable Scope(string label)
	{
		_scopes.Push(label);
		return new ScopeExit(_scopes);
	}

	private sealed class ScopeExit(Stack<string> scopes) : IDisposable
	{
		public void Dispose() => scopes.Pop();
	}

	public bool Local(Node node) => node != null && Actor.IsAncestorOf(node);

	public bool HasClip(StringName clip) => _player != null && !clip.IsEmpty && _player.HasAnimation(clip)
		&& double.IsFinite(_player.GetAnimation(clip).Length) && _player.GetAnimation(clip).Length > 0;

	private Resource[] BindingsFor(StringName style) => _bindings.Where(binding => binding.Get("Style").AsStringName() == style).ToArray();

	/// <summary>Exactly one binding plays <paramref name="style"/>; a wind-up clip needs a positive contact start.</summary>
	public bool CheckAttackStyle(StringName style)
	{
		var matches = BindingsFor(style);
		if (!Check(AnimationTree != null && matches.Length == 1 && HasClip(matches[0].Get("Clip").AsStringName()),
			$"needs one valid animation binding for style '{style}'.")) return false;
		if (!string.IsNullOrWhiteSpace(matches[0].Get("WindUpClip").AsString()))
			Check(HasClip(matches[0].Get("WindUpClip").AsStringName()) && Number(matches[0], "ContactStart") > 0,
				$"style '{style}' needs a valid WindUpClip and a positive ContactStart.");
		return true;
	}

	/// <summary>Contact start of the binding playing <paramref name="style"/>, as a fraction of the attack.</summary>
	public float? ContactStart(StringName style)
	{
		var matches = BindingsFor(style);
		return matches.Length == 1 ? Number(matches[0], "ContactStart") : null;
	}

	/// <summary>The attack's own rules, against the wind-up the action and its binding prepare.</summary>
	public void CheckAttackDefinition(AttackDefinition attack, float duration, StringName style)
	{
		float? windUp = ContactStart(style) is { } start ? duration * start : null;
		foreach (var status in attack.OnHitStatuses ?? [])
			Check(status?.IsValid == true, "contains an invalid on-hit status payload (refreshing statuses need a saved buff file).");
		Check(attack.IsRanged ? Object<PackedScene>(attack, "ProjectileScene") != null : attack.MeleeContact != null,
			"must bind a projectile scene or a melee contact profile.");
		if (!attack.IsRanged) CheckWiring(Object<Node>(AttackController, "MeleeDetector") != null, "AttackController.MeleeDetector: assign a hit detector.");
		CheckWiring(Local(Object<Node>(AttackController, "Indicator")), "AttackController.Indicator: assign this actor's shared attack warning.");
		Check(attack.HasValidImpactValues, "Attack: poise damage, knockback, hit-stop and wind-up turn rate must be nonnegative and finite.");
		Check(attack.HasValidArmorWindow, "Attack.HyperArmorStart/End: require 0 <= start <= end <= 1.");
		Check(attack.HasValidFacingLock(windUp), "Attack.FacingLockLead: must be nonnegative, melee only and no longer than the prepared wind-up.");
		Check(attack.HasValidFlight(windUp, windUp != null ? duration : null),
			"Attack flight: stagger, homing and launch ramp must be finite and nonnegative with a launch scale in (0, 1]; stagger and homing belong to aimed single/spread shots whose last projectile leaves within the attack.");
		var tier = attack.ThreatTier;
		Check(!(windUp < tier.MinimumWindUp() - .001f), $"Attack: {tier} attacks need at least {tier.MinimumWindUp()} s of wind-up before contact.");
		float aimLock = attack.ProjectileAimLockDuration;
		Check(float.IsFinite(aimLock) && aimLock >= 0, "Attack.ProjectileAimLockDuration: must be nonnegative and finite.");
		if (aimLock > 0)
		{
			Check(attack.IsRanged && attack.ProjectilePattern is ProjectilePattern.Single or ProjectilePattern.Spread,
				"Attack.ProjectileAimLockDuration: only aimed single/spread shots support locking.");
			Check(windUp == null || aimLock <= windUp, "Attack.ProjectileAimLockDuration: cannot exceed the prepared wind-up.");
		}
	}

	/// <summary>The actor exports <paramref name="exportName"/> as one of its own nodes.</summary>
	public bool CheckLocalNode(string exportName, string message) => CheckWiring(Local(Object<Node>(Actor, exportName)), message);

	/// <summary>A dash needs the actor's wired dash component, its travel binding and, with recovery, the gate's recovery clip.</summary>
	public void CheckDash(DashProfile dash)
	{
		var component = Object<Node>(Actor, "DashComponent");
		CheckWiring(Local(component) && Object<Node>(component, "Actor") == Actor && Object<Node>(component, "ActionGate") == ActionGate
			&& Object<Node>(component, "MovementComponent") == Object<Node>(Actor, "MovementComponent")
			&& Object<Node>(component, "AnimationComponent") == AnimationComponent,
			"DashComponent: wire this actor's dash component to its actor, movement, gate and animation.");
		var matches = BindingsFor(dash.Style);
		if (Check(AnimationTree != null && matches.Length == 1 && HasClip(matches[0].Get("Clip").AsStringName()),
			$"needs one valid animation binding for dash style '{dash.Style}'.")
			&& !string.IsNullOrWhiteSpace(matches[0].Get("WindUpClip").AsString()))
			Check(HasClip(matches[0].Get("WindUpClip").AsStringName()), $"dash style '{dash.Style}' needs a valid WindUpClip.");
		if (dash.RecoveryDuration > 0)
			Check(HasClip(Object<Resource>(ActionGate, "AnimationProfile")?.Get("RecoveryClip").AsStringName() ?? ""),
				"a dash with recovery needs the action gate's recovery animation clip.");
	}

	/// <summary>Nonzero guard or dodge chances need the matching wired capability and its clips.</summary>
	public void CheckDefenseCapabilities(float guardChance, float dodgeChance)
	{
		var controller = Object<Node>(Actor, "DefenseController");
		if (controller != null) CheckWiring(Local(controller) && Object<Node>(controller, "Enemy") == Actor,
			"DefenseController.Enemy: assign this actor to its local defense controller.");
		CheckWiring((guardChance == 0 && dodgeChance == 0) || controller != null, "EnemyDefenseController: add a controller for nonzero response probabilities.");
		foreach (var (chance, name, definitionProperty, clipProperties) in new[]
		{
			(guardChance, "GuardComponent", "GuardDef", new[] { "GuardHoldClip", "RecoveryClip", "BlockHitClip", "ParryClip" }),
			(dodgeChance, "DodgeComponent", "DodgeDef", new[] { "DodgeBackwardClip", "DodgeForwardClip", "DodgeLeftClip", "DodgeRightClip", "RecoveryClip" })
		})
		{
			if (chance <= 0) continue;
			var capability = Object<Node>(Actor, name);
			CheckWiring(Local(capability) && Object<Node>(controller, name) == capability,
				$"DefenseController.{name}: assign this actor's {name}; empty means unavailable.");
			var capabilityDefinition = Object<Resource>(capability, definitionProperty);
			CheckWiring(capability != null && capabilityDefinition != null && Object<Node>(capability, "Actor") == Actor
				&& Object<Node>(capability, "ActionGate") == ActionGate && Object<Node>(capability, "AnimationComponent") == AnimationComponent,
				$"{name}: bind actor, action gate, animation and definition.");
			var clips = Object<Resource>(capabilityDefinition, "AnimationProfile");
			CheckWiring(clips != null && clipProperties.All(property => (property == "ParryClip"
				&& capabilityDefinition.Get("CanParry").AsBool() == false) || HasClip(clips.Get(property).AsStringName())),
				$"{name}: assign valid defense and recovery clips.");
		}
	}

	public static T Object<T>(GodotObject owner, StringName property) where T : GodotObject => owner?.Get(property).AsGodotObject() as T;
	public static float Number(GodotObject owner, StringName property) => owner.Get(property).AsSingle();
}
