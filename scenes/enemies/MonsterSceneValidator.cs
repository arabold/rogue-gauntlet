using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Read-only checks shared by spawning and the editor. Read stored properties rather than casting
/// gameplay scripts: Godot represents non-tool C# components as native placeholders in the editor.
/// </summary>
public static class MonsterSceneValidator
{
	public static string[] Validate(Node actor)
	{
		var errors = new List<string>();
		void Check(bool valid, string message) { if (!valid) errors.Add(message); }
		bool Local(Node node) => node != null && actor.IsAncestorOf(node);
		var definition = Object<Resource>(actor, "Definition");
		if (definition == null) return new[] { "Definition: assign a MonsterDefinition Resource." };
		Check(!string.IsNullOrWhiteSpace(definition.Get("Id").AsString()), "Definition.Id: enter a stable monster ID.");
		Check(!string.IsNullOrWhiteSpace(definition.Get("DisplayName").AsString()), "Definition.DisplayName: enter a display name.");
		Check(Positive(Number(definition, "MaxHealth")), "Definition.MaxHealth: must be positive and finite.");
		Check(Nonnegative(Number(definition, "MoveSpeed")), "Definition.MoveSpeed: must be nonnegative and finite.");
		Check(definition.Get("XpReward").AsInt32() >= 0, "Definition.XpReward: cannot be negative.");
		foreach (var name in new[] { "HealthComponent", "MovementComponent", "EnemyBehaviorComponent", "DeathComponent", "LootTableComponent", "HurtBoxComponent", "ActionGate" })
			Check(Local(Object<Node>(actor, name)), $"{name}: assign a component within this actor.");
		var health = Object<Node>(actor, "HealthComponent");
		var movement = Object<Node>(actor, "MovementComponent");
		var behavior = Object<Node>(actor, "EnemyBehaviorComponent");
		var gate = Object<Node>(actor, "ActionGate");
		var death = Object<Node>(actor, "DeathComponent");
		var loot = Object<Node>(actor, "LootTableComponent");
		var hurtbox = Object<Node>(actor, "HurtBoxComponent");
		Check(actor.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape != null, "CollisionShape3D: assign a body shape.");
		Check(hurtbox?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape != null, "HurtBoxComponent/CollisionShape3D: assign a hurt shape.");
		if (movement != null)
		{
			Check(Object<Node>(movement, "Actor") == actor, "MovementComponent.Actor: must reference this actor.");
			Check(Object<Node>(movement, "ActionGate") == gate, "MovementComponent.ActionGate: assign this actor's action gate.");
		}
		if (death != null) Check(Object<Node>(death, "HealthComponent") == health && Object<Node>(death, "LootTableComponent") == loot, "DeathComponent: bind this actor's health and loot.");
		if (hurtbox != null) Check(Object<Node>(hurtbox, "HealthComponent") == health && (Object<Node>(hurtbox, "MovementComponent") == null || Object<Node>(hurtbox, "MovementComponent") == movement), "HurtBoxComponent: bind this actor's health and movement.");
		if (behavior != null)
		{
			Check(Object<Node>(behavior, "Actor") == actor && Object<Node>(behavior, "HealthComponent") == health
				&& Object<Node>(behavior, "MovementComponent") == movement, "EnemyBehaviorComponent: bind this actor's health and movement.");
			Check(behavior.GetNodeOrNull<Node>("Navigation") != null && behavior.GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D") != null
				&& behavior.GetNodeOrNull<Node>("Perception") != null && behavior.GetNodeOrNull<RayCast3D>("SightRay") != null,
				"EnemyBehaviorComponent: retain navigation, perception, agent and sight ray children.");
		}
		var profile = Object<Resource>(definition, "Behavior");
		Check(profile != null, "Definition.Behavior: assign a behavior Resource.");
		if (profile == null) return errors.ToArray();
		Check(Object<Resource>(profile, "MovementStrategy") != null, "Behavior.MovementStrategy: assign a positioning strategy.");
		var policy = Object<Resource>(profile, "AttackStrategy");
		Check(policy != null, "Behavior.AttackStrategy: assign the monster's single attack policy.");
		var animation = actor.GetNodeOrNull<Node>("CombatAnimationComponent");
		var controller = Object<Node>(actor, "AttackController");
		Check(Local(controller) && animation != null && Object<Node>(controller, "AnimationComponent") == animation,
			"AttackController.AnimationComponent: bind this actor's CombatAnimationComponent.");
		var playerPath = animation?.Get("AnimationPlayerPath").AsNodePath();
		var player = playerPath != null && !playerPath.IsEmpty ? animation.GetNodeOrNull<AnimationPlayer>(playerPath) : null;
		var treePath = animation?.Get("AnimationTreePath").AsNodePath();
		Check(gate == null || Object<Node>(gate, "AnimationComponent") == animation, "ActionGate.AnimationComponent: assign this actor's combat animation component.");
		var tree = treePath != null && !treePath.IsEmpty ? animation.GetNodeOrNull<AnimationTree>(treePath) : null;
		bool HasClip(StringName clip) => player != null && !clip.IsEmpty && player.HasAnimation(clip)
			&& double.IsFinite(player.GetAnimation(clip).Length) && player.GetAnimation(clip).Length > 0;
		if (policy != null)
		{
			var attack = Object<Resource>(policy, "Attack");
			Check(attack != null, "AttackStrategy.Attack: assign an AttackDefinition.");
			Check(Positive(Number(policy, "Duration")) && Positive(Number(policy, "EngagementDistance")), "AttackStrategy: duration and engagement distance must be positive and finite.");
			Check(Nonnegative(Number(policy, "MinDamage")) && float.IsFinite(Number(policy, "MaxDamage"))
				&& Number(policy, "MaxDamage") >= Number(policy, "MinDamage") && Probability(Number(policy, "Accuracy"))
				&& Probability(Number(policy, "CritChance")), "AttackStrategy: invalid damage range, accuracy or critical chance.");
			var style = attack?.Get("AttackStyle").AsString();
			if (string.IsNullOrEmpty(style)) style = attack?.Get("IsRanged").AsBool() == true ? "ranged" : "melee";
			var animationProfile = Object<Resource>(animation, "Profile");
			var bindings = animationProfile?.Get("Bindings").AsGodotArray();
			var bindingList = bindings?.Select(value => value.AsGodotObject() as Resource).ToArray() ?? Array.Empty<Resource>();
			var matches = bindingList.Where(value => value != null && value.Get("Style").AsString() == style).ToArray();
			Check(tree != null && matches.Length == 1 && HasClip(matches[0].Get("Clip").AsStringName()),
				"CombatAnimationComponent: assign a valid attack style/clip and AnimationPlayer/Tree paths.");
			if (Object<Script>(policy, "script")?.ResourcePath.EndsWith("/MeleeAttackStrategy.cs") == true)
			{
				Check(attack?.Get("IsRanged").AsBool() == false && Object<Resource>(attack, "MeleeContact") != null,
					"MeleeAttackStrategy: assign a melee definition with an explicit contact profile.");
				Check(Object<Node>(controller, "MeleeDetector") != null, "AttackController.MeleeDetector: assign a hit detector.");
			}
			if (Object<Script>(policy, "script")?.ResourcePath.EndsWith("/ProjectileAttackStrategy.cs") == true)
				Check(attack?.Get("IsRanged").AsBool() == true && Object<PackedScene>(attack, "ProjectileScene") != null,
					"ProjectileAttackStrategy: assign a projectile definition and scene.");

		}
		var movementPolicy = Object<Resource>(profile, "MovementStrategy");
		var movementScript = Object<Script>(movementPolicy, "script")?.ResourcePath ?? "";
		if (movementScript.EndsWith("/MaintainDistanceStrategy.cs"))
		{
			float minimum = Number(movementPolicy, "MinimumDistance"), preferred = Number(movementPolicy, "PreferredDistance"), maximum = Number(movementPolicy, "MaximumDistance");
			Check(Nonnegative(minimum) && float.IsFinite(preferred) && Positive(maximum) && minimum < preferred && preferred < maximum
				&& policy != null && maximum <= Number(policy, "EngagementDistance"), "MovementStrategy: require minimum < preferred < maximum <= attack engagement distance.");
		}
		if (movementScript.EndsWith("/AttackThenRetreatStrategy.cs"))
			Check(Positive(Number(movementPolicy, "RetreatDistance")) && Positive(Number(movementPolicy, "MaximumRetreatDuration"))
				&& Nonnegative(Number(movementPolicy, "ReengagementDelay")), "MovementStrategy: invalid retreat distance, duration or delay.");
		var retreat = Object<Resource>(profile, "RetreatCondition");
		if (retreat != null)
			Check(Probability(Number(retreat, "EnterHealthFraction")) && Probability(Number(retreat, "ExitHealthFraction"))
				&& Number(retreat, "EnterHealthFraction") < Number(retreat, "ExitHealthFraction") && Positive(Number(retreat, "DesiredDistance"))
				&& Positive(Number(retreat, "MaximumDuration")), "RetreatCondition: require ordered health thresholds and positive distance/duration.");

		var defense = Object<Resource>(profile, "DefenseStrategy");
		if (defense != null)
		{
			float guardChance = Number(defense, "GuardChance"), dodgeChance = Number(defense, "DodgeChance");
			Check(Probability(guardChance) && Probability(dodgeChance) && Probability(Number(defense, "RiposteChance"))
				&& guardChance + dodgeChance <= 1, "DefenseStrategy: probabilities must be in [0,1], with guard + dodge <= 1.");
			Check(Nonnegative(Number(defense, "ReactionDelay")) && Positive(Number(defense, "GuardPressureWindow"))
				&& Positive(Number(defense, "MaxGuardDuration")), "DefenseStrategy: invalid delay or guard durations.");
			var defenseController = Object<Node>(actor, "DefenseController");
			if (defenseController != null) Check(Local(defenseController) && Object<Node>(defenseController, "Enemy") == actor,
				"DefenseController.Enemy: assign this actor to its local defense controller.");
			Check((guardChance == 0 && dodgeChance == 0) || defenseController != null, "EnemyDefenseController: add a controller for nonzero response probabilities.");
			foreach (var (chance, name, definitionProperty, clipProperties) in new[]
			{
				(guardChance, "GuardComponent", "GuardDef", new[] { "GuardHoldClip", "RecoveryClip", "BlockHitClip", "ParryClip" }),
				(dodgeChance, "DodgeComponent", "DodgeDef", new[] { "DodgeBackwardClip", "DodgeForwardClip", "DodgeLeftClip", "DodgeRightClip", "RecoveryClip" })
			})
			{
				if (chance <= 0) continue;
				var capability = Object<Node>(actor, name);
				Check(Local(capability) && Object<Node>(defenseController, name) == capability,
					$"DefenseController.{name}: assign this actor's {name}; empty means unavailable.");
				var capabilityDefinition = Object<Resource>(capability, definitionProperty);
				Check(capability != null && capabilityDefinition != null && Object<Node>(capability, "Actor") == actor
					&& Object<Node>(capability, "ActionGate") == gate && Object<Node>(capability, "AnimationComponent") == animation,
					$"{name}: bind actor, action gate, animation and definition.");
				var clips = Object<Resource>(capabilityDefinition, "AnimationProfile");
				Check(clips != null && clipProperties.All(property => (property == "ParryClip"
					&& capabilityDefinition.Get("CanParry").AsBool() == false) || HasClip(clips.Get(property).AsStringName())),
					$"{name}: assign valid defense and recovery clips.");
			}
		}
		return errors.ToArray();
	}

	/// <summary>Validate a saved actor without entering the tree or consuming generation RNG.</summary>
	public static string[] ValidateScene(PackedScene scene, out string definitionId)
	{
		definitionId = "";
		if (scene == null) return new[] { "Scene: assign a monster PackedScene." };
		var actor = scene.Instantiate<Node>();
		try
		{
			if (actor is not EnemyBase) return new[] { "Scene: the root must be an EnemyBase actor." };
			definitionId = Object<Resource>(actor, "Definition")?.Get("Id").AsString() ?? "";
			return Validate(actor);
		}
		finally { actor.Free(); }
	}

	private static T Object<T>(GodotObject owner, StringName property) where T : GodotObject => owner?.Get(property).AsGodotObject() as T;
	private static float Number(GodotObject owner, StringName property) => owner.Get(property).AsSingle();
	private static bool Positive(float value) => float.IsFinite(value) && value > 0;
	private static bool Nonnegative(float value) => float.IsFinite(value) && value >= 0;
	private static bool Probability(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
}