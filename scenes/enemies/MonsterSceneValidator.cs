using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Read-only checks shared by spawning and the editor. Node wiring is read through stored properties, because Godot
/// represents non-tool C# components as native placeholders in the editor. The monster's Resources (definition,
/// behavior, combat profile, strategies, actions, conditions) are [Tool], so they validate themselves through
/// <see cref="MonsterValidationContext"/>; adding a strategy or action type never edits this class.
/// </summary>
public static class MonsterSceneValidator
{

	public static string[] Validate(Node actor)
	{
		var definition = Object<MonsterDefinition>(actor, "Definition");
		if (definition == null) return new[] { "Definition: assign a MonsterDefinition Resource." };
		var context = new MonsterValidationContext(actor, definition);
		bool Check(bool valid, string message) => context.CheckWiring(valid, message);
		bool Local(Node node) => context.Local(node);
		foreach (var name in new[] { "HealthComponent", "MovementComponent", "EnemyBehaviorComponent", "DeathComponent", "LootTableComponent", "HurtBoxComponent", "ActionGate", "SpellCaster", "ManaComponent", "Stats", "BuffController" })
			Check(Local(Object<Node>(actor, name)), $"{name}: assign a component within this actor.");
		var body = Object<CollisionShape3D>(actor, "BodyShape");
		Check(body != null && body.GetParent() == actor && body.Shape is CapsuleShape3D or CylinderShape3D or SphereShape3D,
			"BodyShape: assign the actor's own capsule, cylinder or sphere collision shape; its radius sizes navigation.");
		var health = Object<Node>(actor, "HealthComponent");
		var statuses = Object<Node>(actor, "BuffController");
		if (statuses != null)
			Check(Local(statuses) && Object<Node>(statuses, "Actor") == actor && Object<Node>(statuses, "Health") == health,
				"BuffController: bind this actor and its health.");
		var effects = Object<Node>(actor, "WorldEffects");
		if (effects != null)
		{
			var surfaces = Object<Node>(effects, "SurfaceController");
			Check(Local(effects) && Object<Node>(effects, "Health") == health, "WorldEffects: bind this actor's health.");
			Check(Local(surfaces) && Object<Node>(surfaces, "Target") == actor.GetNodeOrNull<Node>("Pivot"), "WorldEffects.SurfaceController: bind this actor's visual Pivot.");
			var cloak = Object<Node>(actor, "Invisibility");
			Check(cloak == null || (Object<Node>(cloak, "Host") == effects && Object<Node>(cloak, "Statuses") == statuses
				&& statuses != null && Object<Resource>(cloak, "Condition") != null), "Invisibility: bind this actor's effect host and status controller, and assign its condition.");
		}
		var movement = Object<Node>(actor, "MovementComponent");
		var behavior = Object<Node>(actor, "EnemyBehaviorComponent");
		var gate = Object<Node>(actor, "ActionGate");
		var frozen = actor.GetNodeOrNull<Node>("FrozenStatusComponent");
		if (frozen != null)
		{
			var frozenEffects = Object<Node>(frozen, "Effects");
			var frozenAppearance = Object<Resource>(frozen, "Appearance");
			Check(Object<Node>(actor, "FrozenStatus") == frozen && Object<Node>(frozen, "Statuses") == statuses && statuses != null && Object<Node>(frozen, "Health") == health
				&& Object<Node>(frozen, "ActionGate") == gate
				&& Object<Node>(frozen, "Animation") == actor.GetNodeOrNull<Node>("EnemyBehaviorComponent/AnimationTree")
				&& Object<Resource>(frozen, "Condition") != null
				&& ((frozenEffects == null && frozenAppearance == null) || (frozenEffects == effects && frozenEffects != null && frozenAppearance != null)),
				"FrozenStatusComponent: bind this actor's status, health, gate, animation and condition; assign both visual dependencies or neither.");
		}
		foreach (string name in new[] { "IronSkinStatus", "StoneSkinStatus" })
		{
			var status = Object<Node>(actor, name);
			if (status != null)
				Check(Local(status) && Object<Node>(status, "Statuses") == statuses && statuses != null
					&& Object<Node>(status, "Host") == effects && Object<Resource>(status, "Condition") != null
					&& Object<Resource>(status, "Effect") != null, $"{name}: bind this actor's status controller and effect host, and assign its condition and appearance.");
		}
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
		if (hurtbox != null) Check(Object<Node>(hurtbox, "HealthComponent") == health && Object<Node>(hurtbox, "Buffs") == statuses
			&& (Object<Node>(hurtbox, "MovementComponent") == null || Object<Node>(hurtbox, "MovementComponent") == movement),
			"HurtBoxComponent: bind this actor's health, buff controller and movement.");
		var stats = Object<Node>(actor, "Stats");
		if (stats != null) Check(Local(stats) && Object<Node>(stats, "Movement") == movement && Object<Node>(stats, "Health") == health
			&& Object<Node>(stats, "HurtBox") == hurtbox, "ActorStatsComponent: bind this actor's movement, health and hurtbox.");
		var phases = Object<Node>(actor, "HealthPhases");
		if (phases != null)
		{
			var fractions = phases.Get("EnterHealthFractions").AsFloat32Array();
			Check(Local(phases) && Object<Node>(phases, "Health") == health, "HealthPhaseComponent: bind this actor's health.");
			Check(fractions.Length > 0 && fractions.Select((value, index) => float.IsFinite(value) && value is > 0 and < 1
				&& (index == 0 || value < fractions[index - 1])).All(valid => valid),
				"HealthPhaseComponent.EnterHealthFractions: list strictly descending fractions in (0, 1).");
		}
		var poise = hurtbox != null ? Object<Node>(hurtbox, "Poise") : null;
		if (poise != null)
		{
			Check(Local(poise) && Object<Node>(poise, "ActionGate") == gate, "HurtBoxComponent.Poise: assign this actor's poise component and bind its action gate.");
			Check(Object<Resource>(poise, "Definition") is PoiseDefinition { IsValid: true },
				"PoiseComponent.Definition: assign a PoiseDefinition with positive max poise and stagger and nonnegative regeneration.");
		}
		var vulnerability = hurtbox != null ? Object<Resource>(hurtbox, "Vulnerability") : null;
		if (vulnerability != null)
			Check(vulnerability is HitVulnerabilityProfile { IsValid: true },
				"HurtBoxComponent.Vulnerability: require front + rear half-angles <= 180 and nonnegative multipliers.");
		var healthBar = actor.GetNodeOrNull<Node>("Nameplate/FloatingHealthBar");
		var barPoise = healthBar != null ? Object<Node>(healthBar, "Poise") : null;
		Check(barPoise == null || barPoise == poise, "FloatingHealthBar.Poise: show this actor's hurtbox poise or leave it empty.");
		if (behavior != null)
		{
			Check(Object<Node>(behavior, "Actor") == actor && Object<Node>(behavior, "HealthComponent") == health
				&& Object<Node>(behavior, "MovementComponent") == movement, "EnemyBehaviorComponent: bind this actor's health and movement.");
			Check(behavior.GetNodeOrNull<NavigationComponent>("Navigation") != null
				&& behavior.GetNodeOrNull<Node>("Perception") != null && behavior.GetNodeOrNull<RayCast3D>("SightRay") != null,
				"EnemyBehaviorComponent: retain navigation, perception and sight ray children.");
		}
		var profile = definition.Behavior;
		Check(profile == null || profile.StayInHomeRoom || EnemyBase.HorizontalRadius(body) <= RoomAssembler.ActorRadius + .001f,
			$"BodyShape: wider than generation keeps passable ({RoomAssembler.ActorRadius}); shrink it or hold the monster in its room (Behavior.StayInHomeRoom).");
		var animation = actor.GetNodeOrNull<Node>("CombatAnimationComponent");
		var controller = Object<Node>(actor, "AttackController");
		Check(Local(controller) && animation != null && Object<Node>(controller, "AnimationComponent") == animation,
			"AttackController.AnimationComponent: bind this actor's CombatAnimationComponent.");
		Check(controller == null || Object<Node>(controller, "ActionGate") == gate, "AttackController.ActionGate: assign this actor's action gate.");
		Check(gate == null || Object<Node>(gate, "AnimationComponent") == animation, "ActionGate.AnimationComponent: assign this actor's combat animation component.");
		definition.Validate(context);
		return [.. context.Errors];
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

	private static T Object<T>(GodotObject owner, StringName property) where T : GodotObject => MonsterValidationContext.Object<T>(owner, property);
}
