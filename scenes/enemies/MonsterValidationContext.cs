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
	private readonly bool _bindingsResolve;
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
		var bindings = profile?.Get("Bindings").AsGodotArray();
		_bindings = bindings?.Select(value => value.AsGodotObject() as Resource).Where(value => value != null).ToArray() ?? [];
		// Resolution rejects the whole profile when any binding is empty, invalid or repeats a style.
		_bindingsResolve = bindings != null && _bindings.Length == bindings.Count && _bindings.All(IsValidBinding)
			&& _bindings.Select(binding => binding.Get("Style").AsString()).Distinct().Count() == _bindings.Length;
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

	/// <summary><see cref="AttackAnimationBinding.IsValid"/>, read through stored properties.</summary>
	private static bool IsValidBinding(Resource binding)
	{
		float start = Number(binding, "ContactStart"), end = Number(binding, "ContactEnd");
		return !string.IsNullOrWhiteSpace(binding.Get("Style").AsString()) && !string.IsNullOrWhiteSpace(binding.Get("Clip").AsString())
			&& float.IsFinite(start) && float.IsFinite(end) && start >= 0 && end >= start && end <= 1
			&& (string.IsNullOrWhiteSpace(binding.Get("WindUpClip").AsString()) || start > 0);
	}

	/// <summary>
	/// Exactly one animation binding plays <paramref name="style"/> with a valid clip, in a profile the combat animation
	/// component can resolve at all; an optional wind-up clip must be valid too and, when
	/// <paramref name="windUpNeedsContactStart"/>, end at a positive contact start.
	/// </summary>
	public bool CheckBinding(StringName style, bool windUpNeedsContactStart = true)
	{
		CheckWiring(_bindingsResolve, "CombatAnimationComponent.Profile: every binding needs a style, a clip and an ordered contact window "
			+ "in [0, 1] (a positive start with a wind-up clip), and no style may repeat; otherwise no attack resolves.");
		var matches = BindingsFor(style);
		if (!Check(AnimationTree != null && matches.Length == 1 && HasClip(matches[0].Get("Clip").AsStringName()),
			$"needs one valid animation binding for style '{style}'.")) return false;
		if (!string.IsNullOrWhiteSpace(matches[0].Get("WindUpClip").AsString()))
			Check(HasClip(matches[0].Get("WindUpClip").AsStringName()) && (!windUpNeedsContactStart || Number(matches[0], "ContactStart") > 0),
				$"style '{style}' needs a valid WindUpClip{(windUpNeedsContactStart ? " and a positive ContactStart" : "")}.");
		return true;
	}

	/// <summary>Contact start of the binding playing <paramref name="style"/>, as a fraction of the attack.</summary>
	public float? ContactStart(StringName style)
	{
		var matches = BindingsFor(style);
		return matches.Length == 1 ? Number(matches[0], "ContactStart") : null;
	}

	/// <summary>The actor exports <paramref name="exportName"/> as one of its own nodes.</summary>
	public bool CheckLocalNode(string exportName, string message) => CheckWiring(Local(Object<Node>(Actor, exportName)), message);

	public static bool IsNonnegative(float value) => float.IsFinite(value) && value >= 0;
	public static bool IsPositive(float value) => float.IsFinite(value) && value > 0;
	public static bool IsProbability(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

	public static T Object<T>(GodotObject owner, StringName property) where T : GodotObject => owner?.Get(property).AsGodotObject() as T;
	public static float Number(GodotObject owner, StringName property) => owner.Get(property).AsSingle();
}
