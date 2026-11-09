using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Read-only checks shared by spawning and the editor. Read stored properties rather than casting
/// gameplay scripts: Godot represents non-tool C# components as native placeholders in the editor.
/// [Tool] definition Resources (attack, poise, vulnerability) are real instances, so their own rules are reused.
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
		Check(Nonnegative(Number(definition, "MaxMana")) && Nonnegative(Number(definition, "ManaRegenPerSecond")),
			"Definition.MaxMana and ManaRegenPerSecond must be nonnegative and finite.");
		Check(definition.Get("XpReward").AsInt32() >= 0, "Definition.XpReward: cannot be negative.");
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
		if (stats != null) Check(Local(stats) && Object<Node>(stats, "Actor") == actor
			&& Object<Node>(stats, "Movement") == movement && Object<Node>(stats, "Health") == health
			&& Object<Node>(stats, "HurtBox") == hurtbox, "ActorStatsComponent: bind this actor's movement, health and hurtbox.");
		var mana = Object<Node>(actor, "ManaComponent");
		Check(Local(mana) && mana != null, "ManaComponent: assign this actor's local mana pool.");
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
		var profile = Object<Resource>(definition, "Behavior");
		Check(profile != null, "Definition.Behavior: assign a behavior Resource.");
		if (profile == null) return errors.ToArray();
		if (profile is EnemyBehaviorProfile behaviorProfile)
			foreach (string error in behaviorProfile.ValidateCombat()) errors.Add($"Behavior: {error}.");
		Check(profile.Get("StayInHomeRoom").AsBool() || EnemyBase.HorizontalRadius(body) <= RoomAssembler.ActorRadius + .001f,
			$"BodyShape: wider than generation keeps passable ({RoomAssembler.ActorRadius}); shrink it or hold the monster in its room (Behavior.StayInHomeRoom).");
		var animation = actor.GetNodeOrNull<Node>("CombatAnimationComponent");
		var controller = Object<Node>(actor, "AttackController");
		Check(Local(controller) && animation != null && Object<Node>(controller, "AnimationComponent") == animation,
			"AttackController.AnimationComponent: bind this actor's CombatAnimationComponent.");
		Check(controller == null || Object<Node>(controller, "ActionGate") == gate, "AttackController.ActionGate: assign this actor's action gate.");
		var playerPath = animation?.Get("AnimationPlayerPath").AsNodePath();
		var player = playerPath != null && !playerPath.IsEmpty ? animation.GetNodeOrNull<AnimationPlayer>(playerPath) : null;
		var treePath = animation?.Get("AnimationTreePath").AsNodePath();
		Check(gate == null || Object<Node>(gate, "AnimationComponent") == animation, "ActionGate.AnimationComponent: assign this actor's combat animation component.");
		var tree = treePath != null && !treePath.IsEmpty ? animation.GetNodeOrNull<AnimationTree>(treePath) : null;
		bool HasClip(StringName clip) => player != null && !clip.IsEmpty && player.HasAnimation(clip)
			&& double.IsFinite(player.GetAnimation(clip).Length) && player.GetAnimation(clip).Length > 0;
		var combatProfiles = new List<(Resource Profile, string Label)>();
		var baseCombat = Object<Resource>(profile, "Combat");
		if (baseCombat != null) combatProfiles.Add((baseCombat, "Behavior.Combat"));
		var phases = profile.Get("Phases").AsGodotArray();
		for (int phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
		{
			var phase = phases[phaseIndex].AsGodotObject() as Resource;
			var phaseCombat = Object<Resource>(phase, "Combat");
			if (phaseCombat != null) combatProfiles.Add((phaseCombat, $"Behavior.Phases[{phaseIndex}].Combat"));
		}

		var animationProfile = Object<Resource>(animation, "Profile");
		var bindings = animationProfile?.Get("Bindings").AsGodotArray();
		var bindingList = bindings?.Select(value => value.AsGodotObject() as Resource).ToArray() ?? Array.Empty<Resource>();
		foreach (var (combat, label) in combatProfiles)
		{
			string tactics = label + ".TacticalStrategy";
			var tacticalPolicy = Object<Resource>(combat, "TacticalStrategy");
			Check(tacticalPolicy != null, $"{tactics}: assign an engagement priority strategy.");
			if (tacticalPolicy != null)
				Check(Positive(Number(tacticalPolicy, "MaximumRetreatDuration")) && Nonnegative(Number(tacticalPolicy, "RetreatRetryDelay"))
					&& Nonnegative(Number(tacticalPolicy, "StandAndFightDistance")), $"{tactics}: require positive retreat duration and nonnegative finite retry/stand distances.");
			var movementPolicy = Object<Resource>(combat, "MovementStrategy");
			Check(movementPolicy != null, $"{label}.MovementStrategy: assign a positioning strategy.");
			var approach = Object<Resource>(combat, "ApproachAction");
			Check(approach is EnemyAttackProfile or EnemySpellProfile { IsOffensive: true }, $"{label}.ApproachAction: select an offensive action used to approach the target.");
			var choices = combat.Get("Actions").AsGodotArray();
			var actions = new List<EnemyActionProfile>();
			foreach (var value in choices)
			{
				var choice = value.AsGodotObject() as Resource;
				var action = Object<EnemyActionProfile>(choice, "Action");
				if (action != null) actions.Add(action);
			}
			var movementScript = Object<Script>(movementPolicy, "script")?.ResourcePath ?? "";
			if (movementScript.EndsWith("/MaintainDistanceStrategy.cs"))
			{
				float minimum = Number(movementPolicy, "MinimumDistance"), preferred = Number(movementPolicy, "PreferredDistance"), maximum = Number(movementPolicy, "MaximumDistance");
				Check(Nonnegative(minimum) && float.IsFinite(preferred) && Positive(maximum) && minimum < preferred && preferred < maximum
					&& approach != null && maximum <= Number(approach, "MaximumDistance"), $"{label}.MovementStrategy: require minimum < preferred < maximum <= approach action range.");
			}
			if (movementScript.EndsWith("/EngagementRingStrategy.cs"))
				Check(Positive(Number(movementPolicy, "RingDistance")) && approach != null && Number(movementPolicy, "RingDistance") > Number(approach, "MaximumDistance"),
					$"{label}.MovementStrategy: the engagement ring must lie outside the approach action's range.");
			if (movementScript.EndsWith("/AttackThenRetreatStrategy.cs"))
				Check(Positive(Number(movementPolicy, "RetreatDistance")) && Positive(Number(movementPolicy, "MaximumRetreatDuration"))
					&& Nonnegative(Number(movementPolicy, "ReengagementDelay")), $"{label}.MovementStrategy: invalid retreat distance, duration or delay.");
			var retreat = Object<Resource>(combat, "RetreatCondition");
			if (retreat != null)
				Check(Probability(Number(retreat, "EnterHealthFraction")) && Probability(Number(retreat, "ExitHealthFraction"))
					&& Number(retreat, "EnterHealthFraction") < Number(retreat, "ExitHealthFraction") && Positive(Number(retreat, "DesiredDistance"))
					&& Positive(Number(retreat, "MaximumDuration")), $"{label}.RetreatCondition: require ordered health thresholds and positive distance/duration.");

			foreach (var action in actions)
			{
				Check(action.HasValidConfiguration, $"{label}.Actions: '{action.ResourceName}' has invalid configuration.");
				if (action is EnemyRushProfile rushProfile)
				{
					var rush = rushProfile.Rush;
					var rushBindings = bindingList.Where(value => value != null && value.Get("Style").AsString() == rush?.Style).ToArray();
					Check(tree != null && rushBindings.Length == 1 && HasClip(rushBindings[0].Get("Clip").AsStringName()),
						$"{label}.Actions: '{action.ResourceName}' needs one valid rush animation style/clip binding.");
					if (rushBindings.Length == 1 && !string.IsNullOrWhiteSpace(rushBindings[0].Get("WindUpClip").AsString()))
						Check(HasClip(rushBindings[0].Get("WindUpClip").AsStringName()), $"{label}.Actions: '{action.ResourceName}' needs a valid rush wind-up clip.");
					if (rush?.RecoveryDuration > 0)
					{
						var defenseAnimation = Object<Resource>(gate, "AnimationProfile");
						Check(defenseAnimation != null && HasClip(defenseAnimation.Get("RecoveryClip").AsStringName()),
							$"{label}.Actions: '{action.ResourceName}' needs the action gate's recovery animation clip.");
					}
					continue;
				}
				if (action is EnemySpellProfile spellProfile)
				{
					var spell = spellProfile.Spell;
					var cast = spell?.Cast;
					StringName style = cast != null && !string.IsNullOrWhiteSpace(cast.AttackStyle) ? cast.AttackStyle : spellProfile.Style;
					var spellBindings = bindingList.Where(value => value != null && value.Get("Style").AsString() == style).ToArray();
					Check(cast != null && spell?.IsValidLevel(spellProfile.Level) == true,
						$"{label}.Actions: '{action.ResourceName}' needs a valid shared SpellDefinition level and cast.");
						Check(!spellProfile.PaysMana || spell?.IsValidLevel(spellProfile.Level) == true
							&& Number(definition, "MaxMana") >= spell.ManaCost(spellProfile.Level),
							$"{label}.Actions: '{action.ResourceName}' requires Definition.MaxMana to cover its authored spell cost.");
					Check(tree != null && spellBindings.Length == 1 && HasClip(spellBindings[0].Get("Clip").AsStringName()),
						$"{label}.Actions: '{action.ResourceName}' needs one valid spell animation style/clip binding.");
					if (spellBindings.Length == 1 && !string.IsNullOrWhiteSpace(spellBindings[0].Get("WindUpClip").AsString()))
						Check(HasClip(spellBindings[0].Get("WindUpClip").AsStringName()) && Number(spellBindings[0], "ContactStart") > 0,
							$"{label}.Actions: '{action.ResourceName}' needs a valid spell wind-up clip and positive contact start.");
					continue;
				}
				if (action is not EnemyAttackProfile attackProfile) continue;
				var attack = attackProfile.Attack;
				Check(attack != null, $"{label}.Actions: '{action.ResourceName}' needs an AttackDefinition.");
				Check(Positive(attackProfile.Duration), $"{label}.Actions: '{action.ResourceName}' duration must be positive and finite.");
				StringName attackStyle = attack != null && !string.IsNullOrWhiteSpace(attack.AttackStyle) ? attack.AttackStyle : attackProfile.Style;
				var matches = bindingList.Where(value => value != null && value.Get("Style").AsString() == attackStyle).ToArray();
				Check(tree != null && matches.Length == 1 && HasClip(matches[0].Get("Clip").AsStringName()),
					$"{label}.Actions: '{action.ResourceName}' needs one valid animation style/clip binding.");
				if (matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].Get("WindUpClip").AsString()))
					Check(HasClip(matches[0].Get("WindUpClip").AsStringName()) && Number(matches[0], "ContactStart") > 0,
						$"{label}.Actions: '{action.ResourceName}' needs a valid WindUpClip and positive contact start.");
				if (attack == null) continue;
				foreach (var status in attack.OnHitStatuses ?? [])
					Check(status?.IsValid == true, $"{label}.Actions: '{action.ResourceName}' contains an invalid on-hit status payload.");
				Check(attack.IsRanged ? Object<PackedScene>(attack, "ProjectileScene") != null : attack.MeleeContact != null,
					$"{label}.Actions: '{action.ResourceName}' must bind a projectile scene or melee contact profile.");
				if (attack.GroundImpact is { } groundImpact)
				{
					Check(groundImpact.IsValid, $"{label}.Actions: '{action.ResourceName}' has an invalid ground impact profile.");
					if (groundImpact.Effect != null)
						Check(groundImpact.Effect.Scene != null && groundImpact.Effect.Lifetime == WorldEffectLifetime.Burst
							&& Local(Object<Node>(controller, "WorldEffects")), "AttackController.WorldEffects: bind a local host for ground impact effects.");
				}
				if (!attack.IsRanged) Check(Object<Node>(controller, "MeleeDetector") != null, "AttackController.MeleeDetector: assign a hit detector.");
				Check(Local(Object<Node>(controller, "Indicator")), "AttackController.Indicator: assign this actor's shared attack warning.");
				float? windUp = matches.Length == 1 ? attackProfile.Duration * Number(matches[0], "ContactStart") : null;
				Check(attack.HasValidImpactValues, "Attack: poise damage, knockback, hit-stop and wind-up turn rate must be nonnegative and finite.");
				Check(attack.HasValidArmorWindow, "Attack.HyperArmorStart/End: require 0 <= start <= end <= 1.");
				Check(attack.HasValidFacingLock(windUp), "Attack.FacingLockLead: must be nonnegative, melee only and no longer than the prepared wind-up.");
				Check(attack.HasValidFlight(windUp, windUp != null ? attackProfile.Duration : null),
					"Attack flight: stagger, homing and launch ramp must be finite and nonnegative with a launch scale in (0, 1]; stagger and homing belong to aimed single/spread shots whose last projectile leaves within the attack.");
				var tier = attack.ThreatTier;
				if (windUp < tier.MinimumWindUp() - .001f) errors.Add($"Attack: {tier} attacks need at least {tier.MinimumWindUp()} s of wind-up before contact.");
				float aimLock = attack.ProjectileAimLockDuration;
				Check(Nonnegative(aimLock), "Attack.ProjectileAimLockDuration: must be nonnegative and finite.");
				if (aimLock > 0)
				{
					Check(attack.IsRanged && attack.ProjectilePattern is ProjectilePattern.Single or ProjectilePattern.Spread,
						"Attack.ProjectileAimLockDuration: only aimed single/spread shots support locking.");
					if (matches.Length == 1) Check(aimLock <= attackProfile.Duration * Number(matches[0], "ContactStart"),
						"Attack.ProjectileAimLockDuration: cannot exceed the prepared wind-up.");
				}
			}
			var defense = Object<Resource>(combat, "DefenseStrategy");
			if (defense != null) ValidateDefense(defense, label);
		}
		void ValidateDefense(Resource defense, string label)
		{
			float guardChance = Number(defense, "GuardChance"), dodgeChance = Number(defense, "DodgeChance");
			Check(Probability(guardChance) && Probability(dodgeChance) && Probability(Number(defense, "RiposteChance"))
				&& guardChance + dodgeChance <= 1, $"{label}.DefenseStrategy: probabilities must be in [0,1], with guard + dodge <= 1.");
			Check(Nonnegative(Number(defense, "ReactionDelay")) && Positive(Number(defense, "GuardPressureWindow"))
				&& Positive(Number(defense, "MaxGuardDuration")), $"{label}.DefenseStrategy: invalid delay or guard durations.");
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
