using Godot;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.Json;
using System.IO;

/// <summary>
/// The combat arena's harness panel: hero action buttons, the status and monster action readouts, the feedback log, the
/// trace recorder and the scenario runner. Items, spells, monsters and display toggles come from the debug toolkit in
/// the arena's HUD, which scenario commands drive by resource path, so new content needs no harness edit.
/// </summary>
public partial class CombatHarness : CanvasLayer
{
	[Export] public Player Player { get; set; }
	/// <summary>The world the arena's debug tools act in.</summary>
	[Export] public DebugArena Arena { get; set; }
	[Export] public ItemSpawnTool Items { get; set; }
	[Export] public MonsterTool Monsters { get; set; }
	[Export] public HeroTool Hero { get; set; }
	/// <summary>Equipped on startup, as an exact authored copy like every weapon the <c>equip</c> command gives.</summary>
	[Export] public Weapon StartingWeapon { get; set; }
	[Export] public ulong Seed { get; set; } = 42;
	[Export] public CombatTraceRecorder Recorder { get; set; }
	[Export] public string TracePath { get; set; } = "";
	private int _stopAfterFrames;
	private ulong _startFrame;
	private HarnessCommand[] _commands = [];
	private int _commandIndex;
	/// <summary>The item the <c>equip</c> command gave last; the next one replaces it so the backpack stays empty.</summary>
	private InventoryItemSlot _equipped;
	/// <summary>The spell the <c>spell</c> command picked; wands and staves equipped later carry it too.</summary>
	private SpellDefinition _spell;
	private Label _status;
	private Label _monsterActions;
	private readonly System.Text.StringBuilder _actionText = new();
	private ulong _actionPanelFrame;
	private Label _feedback;
	private Button _heroGuardButton;
	private readonly Queue<string> _history = new();

	/// <summary>The target the arena's monster tool spawned first: the practice dummy or a monster; null when none lives.</summary>
	public Node3D PracticeTarget => Monsters?.Spawned.FirstOrDefault();
	/// <summary>The spawned target when it is a monster.</summary>
	public EnemyBase Monster => PracticeTarget as EnemyBase;

	public override void _Ready()
	{
		try { InitializeHarness(); }
		catch (Exception error)
		{
			GD.PushError($"Combat harness initialization failed: {error.Message}");
			SetProcess(false);
			SetPhysicsProcess(false);
			GetTree().Quit(1);
		}
	}

	private void InitializeHarness()
	{
		if (Player == null || Arena == null || Items == null || Monsters == null || Hero == null || Recorder == null)
			throw new InvalidDataException("Assign the player, arena, debug tools and trace recorder.");
		ProcessPhysicsPriority = CombatPhysics.Input - 1;
		DisableHarnessKeyboardFocus(GetNode<Control>("%Controls"));
		_startFrame = Engine.GetPhysicsFrames();
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--combat-seed=")) Seed = ulong.Parse(arg[14..]);
			if (arg.StartsWith("--combat-log=")) TracePath = arg[13..];
			if (arg == "--combat-console") Recorder.EchoToConsole = true;
			if (arg.StartsWith("--combat-frames=")) _stopAfterFrames = int.Parse(arg[16..]);
			if (arg.StartsWith("--combat-script="))
			{
				_commands = JsonSerializer.Deserialize<HarnessCommand[]>(File.ReadAllText(arg[16..])) ?? [];
				if (_commands.Any(c => c.Frame < 0)) throw new InvalidDataException("Combat command frames must be nonnegative.");
				_commands = _commands.OrderBy(c => c.Frame).ToArray();
				// The input controller keeps running so scripted presses use the real buffer and recovery cancels.
				Player.InputComponent.DeviceEnabled = false;
			}
		}
		// Spawned monsters seed their combat streams from the world, so a run replays with the harness seed.
		Arena.RunSeed = Seed;
		Player.AttackController.ConfigureRandom(GenerationRandom.DeriveSeed(Seed, "combat.attack", "player"));
		_status = GetNode<Label>("%Controls/Status");
		_monsterActions = GetNode<Label>("%Controls/MonsterActions");
		_feedback = GetNode<Label>("%Controls/Feedback");
		GetNode<Button>("%Controls/Attack").Pressed += () => ExecuteCommand("attack");
		_heroGuardButton = GetNode<Button>("%Controls/HeroGuard");
		_heroGuardButton.Pressed += () => ExecuteCommand(_heroGuardHeld ? "release_guard" : "guard");
		GetNode<Button>("%Controls/HeroDodge").Pressed += () => ExecuteCommand("dodge");
		GetNode<Button>("%Controls/Reset").Pressed += () => ExecuteCommand("reset");
		Recorder.Start(Arena, Seed, TracePath);

		Player.AttackController.AttackFeedback += (result, amount) => Feedback("Hero", result, amount);
		Player.HurtBoxComponent.DamageResolved += (result, amount, _) => Feedback("Hero", ((DamageOutcome)result).ToString(), amount);
		if (Player.DodgeComponent != null)
		{
			Player.DodgeComponent.DodgeStarted += dir => Feedback("Hero", "Dodge", 0);
		}
		if (Player.GuardComponent != null)
		{
			Player.GuardComponent.ParrySuccessful += () => Feedback("Hero", "Parry!", 0);
			Player.GuardComponent.BlockSuccessful += chip => Feedback("Hero", $"Block (-{chip:0})", chip);
		}
		// Targets spawned from the debug menu get the same readouts as the harness's own.
		Monsters.TargetSpawned += OnTargetSpawned;
		// The arena builds its navigation when its own setup finishes, after this panel's and before the first physics
		// tick, so a scenario's frame-zero spawn replaces the dummy rather than the reverse.
		Arena.Ready += SpawnPracticeTarget;
		if (StartingWeapon != null) Equip(StartingWeapon);
	}

	/// <summary>Keep mouse-operated debug controls from capturing the keys used to control the hero.</summary>
	private static void DisableHarnessKeyboardFocus(Node node)
	{
		if (node is Control control)
		{
			control.FocusMode = Control.FocusModeEnum.None;
			control.ReleaseFocus();
		}
		foreach (Node child in node.GetChildren()) DisableHarnessKeyboardFocus(child);
	}

	/// <summary>
	/// Equips an exact authored copy of <paramref name="definition"/>, replacing the item the harness equipped before;
	/// switching gear deliberately interrupts the old attack. A wand or staff carries the spell the <c>spell</c> command
	/// picked, when it can.
	/// </summary>
	public bool Equip(Item definition)
	{
		if (definition is not EquipableItem) return false;
		Player.AttackController.CancelAttack();
		if (_equipped != null && Player.Inventory.Items.Contains(_equipped)) Player.Inventory.RemoveItem(_equipped, 1);
		_equipped = null;
		var item = ItemFactory.Create(definition);
		if (item is MagicStaff staff && _spell != null && staff.CanBind(_spell)) staff.BoundSpell = _spell;
		if (!Items.Give(item, equip: true)) return false;
		_equipped = Player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, item));
		CombatTrace.Write(this, "harness.equipped", ("weapon", definition.ResourcePath), ("style", item is Weapon weapon ? weapon.AttackStyle.ToString() : ""));
		return true;
	}

	/// <summary>
	/// Picks the spell wands and staves carry and binds it to the equipped one, so its secondary exercises mana and the
	/// weapon's hit as in gameplay. The weapon reads its spell live, so only the caster's spell node is rebound.
	/// </summary>
	public bool SelectSpell(SpellDefinition spell)
	{
		if (spell == null) return false;
		_spell = spell;
		var entry = Player.Inventory.EquippedItems[EquipmentSlot.WeaponHand];
		if (entry?.Item is not MagicStaff staff || !staff.CanBind(spell) || ReferenceEquals(staff.BoundSpell, spell)) return true;
		staff.BoundSpell = spell;
		Player.AttackController.CancelAttack();
		Player.SpellCaster.Bind(staff.BoundSpell, staff);
		return true;
	}

	private bool _heroGuardHeld;

	public bool RequestHeroGuard(bool held)
	{
		_heroGuardHeld = held;
		Player.InputController.SetGuardIntent(held);
		if (!held) { Player.GuardComponent?.EndGuard(); return true; }
		return Player.GuardComponent?.TryBeginGuard(Player.MovementComponent.LookAtDirection) == DefenseRequestResult.Success;
	}

	public bool RequestHeroDodge(Vector3 direction)
	{
		if (direction.IsZeroApprox()) direction = Player.InputController.GetDodgeDirection();
		return Player.DodgeComponent?.TryDodge(direction) == DefenseRequestResult.Success;
	}

	/// <summary>Exercise the actual action slot, including acceptance, recovery and cooldown.</summary>
	public void Attack() => Player.ActionManager.TryPerformPrimary();

	/// <summary>Places the practice dummy, as the arena starts.</summary>
	private void SpawnPracticeTarget() => SpawnTarget(Monsters.PracticeTargetScene, null);

	/// <summary>
	/// Replaces the arena's target with <paramref name="scene"/>, spawned by the monster tool with its settings, at
	/// <paramref name="distance"/> ahead of the hero or the monster's preferred distance.
	/// </summary>
	private bool SpawnTarget(PackedScene scene, float? distance)
	{
		if (scene == null) return false;
		Monsters.Clear();
		return Monsters.Spawn(scene, distance) != null;
	}

	private void OnTargetSpawned(Node3D target)
	{
		if (MonsterTool.HurtBoxOf(target) is { } hurtBox)
			hurtBox.DamageResolved += (result, amount, _) => Feedback("Target", ((DamageOutcome)result).ToString(), amount);
		if (target is EnemyBase monster) BindMonsterFeedback(monster);
		CombatTrace.Write(this, "harness.spawned", ("scene", target.SceneFilePath), ("seed", Seed));
	}

	private void BindMonsterFeedback(EnemyBase monster)
	{
		monster.AttackController.AttackFeedback += (result, amount) => Feedback("Monster", result, amount);
		monster.HurtBoxComponent.DamageResolved += (result, amount, _) => Feedback("Monster Defend", ((DamageOutcome)result).ToString(), amount);
		if (monster.DodgeComponent != null) monster.DodgeComponent.DodgeStarted += dir => Feedback("Monster", "Dodge", 0);
		if (monster.GuardComponent != null)
		{
			monster.GuardComponent.ParrySuccessful += () => Feedback("Monster", "Parry!", 0);
			monster.GuardComponent.BlockSuccessful += chip => Feedback("Monster", $"Block (-{chip:0})", chip);
		}
		if (monster.DefenseController != null) monster.DefenseController.RiposteTriggered += () => Feedback("Monster", "Riposte!", 0);
	}

	/// <summary>
	/// Restores the hero through the hero tool and the target's health, poise and motion. Automated actors keep their
	/// authored capabilities and normal action gates.
	/// </summary>
	public void ResetHealth()
	{
		Hero.Reset();
		_heroGuardHeld = false;
		Node3D target = PracticeTarget;
		if (MonsterTool.HurtBoxOf(target)?.HealthComponent is { } targetHealth) targetHealth.SetHealth(targetHealth.MaxHealth, targetHealth.MaxHealth);
		if (target is CombatPracticeTarget dummy)
		{
			dummy.Movement?.Stop();
			dummy.Movement?.Push(Vector3.Zero, 0);
			dummy.ActionGate?.ResetForSimulation(); // Ends a stagger, so the restored dummy no longer takes punish damage.
			dummy.HurtBox?.Poise?.Restore();
		}
		if (target is EnemyBase { IsDead: false } monster)
		{
			monster.HealthComponent.SetHealth(monster.HealthComponent.MaxHealth, monster.HealthComponent.MaxHealth);
			monster.MovementComponent.Push(Vector3.Zero, 0);
			monster.DashComponent?.Cancel();
			monster.HurtBoxComponent.Poise?.Restore();
		}
	}

	private void Feedback(string actor, string result, float damage)
	{
		_history.Enqueue($"{actor}: {result}{(damage > 0 ? $" ({damage:0.0})" : "")}");
		while (_history.Count > 6) _history.Dequeue();
		_feedback.Text = string.Join("\n", _history);
	}

	public override void _PhysicsProcess(double delta)
	{
		Recorder.AdvanceClock(delta);
		UpdateActionPanel();
		int frame = (int)(Engine.GetPhysicsFrames() - _startFrame);
		while (_commandIndex < _commands.Length && _commands[_commandIndex].Frame <= frame)
		{
			var command = _commands[_commandIndex++];
			ExecuteCommand(command.Command, command.Path, command.Value, new Vector3(command.X, 0, command.Z));
		}
		if (_stopAfterFrames > 0 && frame >= _stopAfterFrames)
		{
			Recorder.Stop();
			GetTree().Quit();
			return;
		}
	}

	public override void _ExitTree()
	{
		Recorder?.Stop();
		Player.InputController.SetGuardIntent(false);
	}

	public override void _Process(double delta)
	{
		Node3D target = PracticeTarget;
		string monster = target is EnemyBase spawned
			? $"Monster: {DescribeAction(spawned.ActionGate, spawned.IsDead)} · HP {spawned.HealthComponent.CurrentHealth:0}"
			: target == null ? "Monster: Not spawned" : "Dummy";
		float? targetHealth = MonsterTool.HurtBoxOf(target)?.HealthComponent?.CurrentHealth;
		_status.Text = $"Hero: {DescribeAction(Player.ActionGate, Player.IsDead)} · HP {Player.HealthComponent.CurrentHealth:0}\n{monster} · HP {targetHealth:0}";
		_heroGuardButton.Text = _heroGuardHeld ? "Release guard (L)" : "Guard / parry (L)";
	}

	/// <summary>Refresh the action tuning panel; it evaluates actions, so it runs in the physics step like the AI.</summary>
	private void UpdateActionPanel()
	{
		if (_monsterActions == null || ++_actionPanelFrame % 6 != 0) return;
		_monsterActions.Text = Monster is { } monster ? DescribeActions(monster) : "Monster actions appear here.";
	}

	/// <summary>
	/// Tuning view of the monster's choices: phase, tactical decision, running action, and for each authored action
	/// its priority, effective weight (after modifiers) and whether it is ready, cooling down or out of position.
	/// </summary>
	private string DescribeActions(EnemyBase monster)
	{
		var behavior = monster.EnemyBehaviorComponent;
		var actions = behavior.Actions;
		if (actions == null) return "Monster AI is not ready.";
		_actionText.Clear();
		_actionText.Append($"Phase {monster.HealthPhases?.CurrentPhase ?? 0} · tactics: {behavior.TacticalDecisionReason}");
		_actionText.Append($" · running: {behavior.RunningAction?.Action.Label ?? "none"}\n");
		foreach (var action in actions.Actions)
		{
			var target = actions.EvaluateTarget(action);
			float weight = target != null ? actions.ResolveWeight(action, target) : action.Weight;
			string state = actions.IsCoolingDown(action) ? $"cooldown {actions.CooldownRemaining(action):0.0} s"
				: target == null ? "not in position" : "ready";
			_actionText.Append($"• {action.Label} [p{action.Priority}] weight {weight:0.##}: {state}\n");
		}
		_actionText.Append($"Last: {actions.LastDecision}");
		return _actionText.ToString();
	}

	/// <summary>Present gameplay state rather than animation style identifiers in the arena HUD.</summary>
	private static string DescribeAction(ActorActionGateComponent gate, bool dead)
	{
		if (dead) return "Dead";
		if (gate?.IsStatusLocked == true) return "Immobilized";
		if (gate?.IsBusy != true) return "Ready";
		return (gate.CurrentKind, gate.CurrentPhase) switch
		{
			(_, ActorActionPhase.Stagger) => "Staggered",
			(ActorActionKind.Guard, ActorActionPhase.WindUp) => "Raising guard",
			(ActorActionKind.Guard, ActorActionPhase.Recovery) => "Guard recovery",
			(ActorActionKind.Guard, _) => "Guarding",
			(_, ActorActionPhase.Recovery) => "Recovering",
			(ActorActionKind.Dodge, _) => "Dodging",
			(_, ActorActionPhase.WindUp) => "Preparing",
			(ActorActionKind.PrimaryAttack or ActorActionKind.SecondaryTechnique, _) => "Attacking",
			_ => "Busy"
		};
	}

	/// <summary>
	/// Execute one scenario command through production components, returning acceptance. <paramref name="path"/> names
	/// the resource a command uses: the item to equip, the spell to bind, or the monster scene to spawn (empty for the
	/// practice dummy, with <paramref name="value"/> its distance in metres; 0 is the monster's preferred distance).
	/// </summary>
	public bool ExecuteCommand(string command, string path = "", int value = 0, Vector3 direction = default)
	{
		bool accepted;
		var monster = Monster;
		bool monsterAlive = monster is { IsDead: false };
		switch (command)
		{
			case "equip": accepted = Equip(LoadResource<Item>(path)); break;
			case "spell": accepted = SelectSpell(LoadResource<SpellDefinition>(path)); break;
			case "spawn":
				accepted = value >= 0 && SpawnTarget(string.IsNullOrEmpty(path) ? Monsters.PracticeTargetScene : LoadResource<PackedScene>(path),
					value > 0 ? value : null);
				break;
			case "defense":
				accepted = value >= 0 && value < MonsterTool.DefenseModes.Length;
				if (accepted) Monsters.DefenseMode = MonsterTool.DefenseModes[value].Mode;
				break;
			case "attack":
				ulong previous = Player.ActionGate.CurrentGeneration;
				Attack(); accepted = Player.ActionGate.CurrentGeneration != previous && Player.ActionGate.CurrentKind == ActorActionKind.PrimaryAttack; break;
			case "secondary":
				ulong beforeSecondary = Player.ActionGate.CurrentGeneration;
				Player.ActionManager.TryPerformSecondary();
				accepted = Player.ActionGate.CurrentGeneration != beforeSecondary && Player.ActionGate.CurrentKind == ActorActionKind.SecondaryTechnique;
				break;
			case "guard": accepted = RequestHeroGuard(true); break;
			case "release_guard": accepted = RequestHeroGuard(false); break;
			case "dodge": accepted = RequestHeroDodge(direction); break;
			// Buffered presses go through the input controller like a device press; acceptance means buffered.
			case "press_primary": Player.InputController.PushIntent(PlayerIntent.Primary); accepted = true; break;
			case "press_secondary": Player.InputController.PushIntent(PlayerIntent.Secondary); accepted = true; break;
			case "press_dodge": Player.InputController.PushIntent(PlayerIntent.Dodge); accepted = true; break;
			case "move": Player.InputComponent.InputDirection = direction.Normalized(); Player.MovementComponent.SetInputDirection(direction); accepted = true; break;
			case "monster_attack": accepted = monsterAlive && monster.EnemyBehaviorComponent.TryAttack(); break;
			case "automation":
				accepted = value is 0 or 1;
				if (accepted) Monsters.AIEnabled = value == 1;
				break;
			case "monster_guard": accepted = monster != null && monster.GuardComponent?.TryBeginGuard(direction) == DefenseRequestResult.Success; break;
			case "monster_dodge": accepted = monster != null && monster.DodgeComponent?.TryDodge(direction) == DefenseRequestResult.Success; break;
			case "position_monster": accepted = monster != null; if (accepted) monster.Position = new Vector3(direction.X, monster.Position.Y, direction.Z); break;
			case "position_player": Player.Position = new Vector3(direction.X, Player.Position.Y, direction.Z); accepted = true; break;
			case "reset": ResetHealth(); accepted = true; break;
			case "build": accepted = Hero.ApplyBuild(LoadResource<HeroBuildProfile>(path)); _equipped = null; break;
			default: accepted = false; break;
		}
		CombatTrace.Write(this, "harness.command", ("command", command), ("path", path), ("value", value), ("direction", CombatTrace.Vector(direction)), ("accepted", accepted));
		return accepted;
	}

	/// <summary>
	/// The resource at <paramref name="path"/> when it exists and has type <typeparamref name="T"/>; null otherwise. A path
	/// command is a load boundary, so pending finalizers settle first, as before a floor generates.
	/// </summary>
	private static T LoadResource<T>(string path) where T : Resource
	{
		if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return null;
		ResourceCacheGuard.Settle();
		return ResourceLoader.Load(path) as T;
	}

	private sealed class HarnessCommand
	{
		public int Frame { get; set; }
		public string Command { get; set; } = "";
		public string Path { get; set; } = "";
		public int Value { get; set; }
		public float X { get; set; }
		public float Z { get; set; }
	}

}