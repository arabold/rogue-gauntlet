using Godot;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.Json;
using System.IO;

/// <summary>Scene-authored combat arena using production equipment, actions and structured diagnostics.</summary>
public partial class CombatHarness : CanvasLayer
{
	[Export] public Player Player { get; set; }
	[Export] public Node3D PracticeTarget { get; set; }
	[Export] public PackedScene[] Monsters { get; set; } = System.Array.Empty<PackedScene>();
	[Export] public Weapon[] Weapons { get; set; } = System.Array.Empty<Weapon>();
	[Export] public ulong Seed { get; set; } = 42;
	[Export] public CombatTraceRecorder Recorder { get; set; }
	[Export] public string TracePath { get; set; } = "";
	private int _stopAfterFrames;
	private ulong _startFrame;
	private HarnessCommand[] _commands = [];
	private int _commandIndex;
	private InventoryItemSlot _previewItem;
	private OptionButton _selection;
	private Label _status;
	private Label _feedback;
	private EnemyBase _monster;
	public EnemyBase Monster => _monster;
	private float _motionTime;
	private double _previousTimeScale;
	private readonly Queue<string> _history = new();
	private readonly Dictionary<HurtBoxComponent, MeshInstance3D> _hurtboxMeshes = new();
	private enum MonsterAutomation { Manual, Attack, Dodge, Guard }
	private MonsterAutomation _automation;
	private float _automaticPause;
	private float _automaticGuardRemaining;
	private bool _dodgeRight;

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
		ProcessPhysicsPriority = CombatPhysics.Input - 1;
		_startFrame = Engine.GetPhysicsFrames();
		_previousTimeScale = Engine.TimeScale;
		Recorder ??= GetParent().GetNode<CombatTraceRecorder>("CombatTraceRecorder");
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--combat-seed=")) Seed = ulong.Parse(arg[14..]);
			if (arg.StartsWith("--combat-log=")) TracePath = arg[13..];
			if (arg == "--combat-console") GetNode<CheckButton>("%Controls/TraceConsole").SetPressedNoSignal(true);
			if (arg.StartsWith("--combat-frames=")) _stopAfterFrames = int.Parse(arg[16..]);
			if (arg.StartsWith("--combat-script="))
			{
				_commands = JsonSerializer.Deserialize<HarnessCommand[]>(File.ReadAllText(arg[16..])) ?? [];
				if (_commands.Any(c => c.Frame < 0)) throw new InvalidDataException("Combat command frames must be nonnegative.");
				_commands = _commands.OrderBy(c => c.Frame).ToArray();
				Player.InputController.SetPhysicsProcess(false);
				Player.InputComponent.SetPhysicsProcess(false);
			}
		}
		Player.AttackController.ConfigureRandom(GenerationRandom.DeriveSeed(Seed, "combat.attack", "player"));
		_selection = GetNode<OptionButton>("%Controls/Weapons");
		_status = GetNode<Label>("%Controls/Status");
		foreach (var weapon in Weapons) _selection.AddItem(weapon.Name);
		_selection.ItemSelected += index => ExecuteCommand("equip", (int)index, Vector3.Zero);
		GetNode<Button>("%Controls/Attack").Pressed += () => ExecuteCommand("attack", 0, Vector3.Zero);
		var heroGuardBtn = GetNodeOrNull<Button>("%Controls/HeroGuard");
		if (heroGuardBtn != null) heroGuardBtn.Pressed += () => ExecuteCommand(_heroGuardHeld ? "release_guard" : "guard", 0, Vector3.Zero);
		var heroDodgeBtn = GetNodeOrNull<Button>("%Controls/HeroDodge");
		if (heroDodgeBtn != null) heroDodgeBtn.Pressed += () => ExecuteCommand("dodge", 0, Vector3.Zero);
		_feedback = GetNode<Label>("%Controls/Feedback");
		var defense = GetNode<OptionButton>("%Controls/Defense");
		foreach (var mode in new[] { "No defense", "Invulnerable", "Monster default", "Always dodge", "Always guard / parry" }) defense.AddItem(mode);
		defense.Selected = 2;
		defense.ItemSelected += value => ExecuteCommand("defense", (int)value, Vector3.Zero);
		var monsters = GetNode<OptionButton>("%Controls/Monsters");
		foreach (var scene in Monsters) monsters.AddItem(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Path.GetFileNameWithoutExtension(scene.ResourcePath).Replace('_', ' ')));
		if (Monsters.Length > 0) monsters.Selected = 0;
		GetNode<Button>("%Controls/SpawnMonster").Pressed += () => ExecuteCommand("spawn", GetNode<OptionButton>("%Controls/Monsters").Selected, Vector3.Zero);
		GetNode<Button>("%Controls/MonsterAttack").Pressed += () => ExecuteCommand("monster_attack", 0, Vector3.Zero);
		var automation = GetNode<OptionButton>("%Controls/Automation");
		foreach (string mode in new[] { "Manual / react to hero", "Repeat attack", "Repeat dodge", "Repeat guard" }) automation.AddItem(mode);
		automation.ItemSelected += value => ExecuteCommand("automation", (int)value, Vector3.Zero);
		GetNode<Button>("%Controls/Reset").Pressed += () => ExecuteCommand("reset", 0, Vector3.Zero);

		BindToggle("TraceConsole", value => Recorder.EchoToConsole = value);
		BindToggle("TraceFrames", value => Recorder.CaptureFrames = value);
		BindToggle("SlowMotion", value => Engine.TimeScale = value ? .25 : _previousTimeScale);
		BindToggle("Hurtboxes", UpdateHurtboxDebugDraw);
		BindToggle("Volumes", _ => ConfigureAttackPresentation());
		BindToggle("Effects", _ => ConfigureAttackPresentation());
		BindToggle("HeroInvulnerable", value => Player.HurtBoxComponent.Invulnerable = value);
		Recorder.Start(GetParent(), Seed, TracePath);

		Player.AttackController.AttackFeedback += (result, amount) => Feedback("Hero", result, amount);
		Player.HurtBoxComponent.DamageResolved += (result, amount) => Feedback("Hero", ((DamageOutcome)result).ToString(), amount);
		if (Player.DodgeComponent != null)
		{
			Player.DodgeComponent.DodgeStarted += dir => Feedback("Hero", "Dodge", 0);
		}
		if (Player.GuardComponent != null)
		{
			Player.GuardComponent.ParrySuccessful += () => Feedback("Hero", "Parry!", 0);
			Player.GuardComponent.BlockSuccessful += chip => Feedback("Hero", $"Block (-{chip:0})", chip);
		}
		PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent").DamageResolved += (result, amount) => Feedback("Target", ((DamageOutcome)result).ToString(), amount);
		if (Weapons.Length > 0) EquipPreview(0);
		ApplyDefense();
	}

	/// <summary>Use the same application path for authored startup state and subsequent UI changes.</summary>
	private void BindToggle(string name, Action<bool> apply)
	{
		var toggle = GetNode<CheckButton>($"%Controls/{name}");
		toggle.Toggled += value => apply(value);
		apply(toggle.ButtonPressed);
	}

	private void ConfigureAttackPresentation()
	{
		Configure(Player.AttackController);
		if (GodotObject.IsInstanceValid(_monster) && !_monster.IsDead) Configure(_monster.AttackController);
	}

	/// <summary>Switching gear deliberately interrupts the old attack before replacing its visual.</summary>
	public void EquipPreview(int index)
	{
		if (index < 0 || index >= Weapons.Length) return;
		Player.AttackController.CancelAttack();
		if (_previewItem != null) Player.Inventory.RemoveItem(_previewItem, 1);
		var item = ItemFactory.Create(Weapons[index]);
		if (!Player.Inventory.TryAdd(item).Success) return;
		_previewItem = Player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, item));
		Player.Inventory.Equip(_previewItem);
		CombatTrace.Write(this, "harness.equipped", ("weapon", Weapons[index].ResourcePath), ("style", item is Weapon w ? w.AttackStyle.ToString() : ""));
		ApplyDefense();
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
	public void Attack()
	{
		Configure(Player.AttackController);
		Player.ActionManager.TryPerformPrimary();
	}

	/// <summary>Spawn a native actor with AI decisions disabled; its real attack executor remains active.</summary>
	public void SpawnMonster()
	{
		if (Monsters.Length == 0) return;
		if (GodotObject.IsInstanceValid(_monster))
		{
			if (_monster.HurtBoxComponent != null) _hurtboxMeshes.Remove(_monster.HurtBoxComponent);
			_monster.GetParent().RemoveChild(_monster);
			_monster.QueueFree();
		}
		int selected = GetNode<OptionButton>("%Controls/Monsters").Selected;
		if (selected < 0 || selected >= Monsters.Length) selected = 0;
		_monster = Monsters[selected].Instantiate<EnemyBase>();
		PracticeTarget.Position = new Vector3(-3, 0, -2);
		_monster.Position = Player.Position + Vector3.Forward * (float)GetNode<SpinBox>("%Controls/SpawnDistance").Value;
		_monster.GetNode<EnemyBehaviorComponent>("EnemyBehaviorComponent").AIEnabled = false;
		Player.GetParent().AddChild(_monster);
		_monster.ConfigureCombatRandom(Seed, "harness.monster");
		// Arena simulations exercise death without awarding dungeon loot or experience.
		if (_monster.DeathComponent != null)
		{
			_monster.DeathComponent.LootTableComponent = null;
			_monster.DeathComponent.Xp = 0;
		}
		if (_monster.DefenseController != null) _monster.DefenseController.Target = Player;
		_monster.EnemyBehaviorComponent.SetTarget(Player);
		_monster.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		_monster.EnemyBehaviorComponent.SetTarget(Player);
		_monster.MovementComponent.SetLookAtDirection((Player.GlobalPosition - _monster.GlobalPosition).Normalized());
		_monster.LookAt(Player.GlobalPosition);
		CombatTrace.Write(this, "harness.spawned", ("scene", Monsters[selected].ResourcePath), ("seed", Seed));
		var controller = _monster.GetNode<AttackController>("AttackController");
		controller.AttackFeedback += (result, amount) => Feedback("Monster", result, amount);
		_monster.HurtBoxComponent.DamageResolved += (result, amount) => Feedback("Monster Defend", ((DamageOutcome)result).ToString(), amount);
		if (_monster.DodgeComponent != null)
		{
			_monster.DodgeComponent.DodgeStarted += dir => Feedback("Monster", "Dodge", 0);
		}
		if (_monster.GuardComponent != null)
		{
			_monster.GuardComponent.ParrySuccessful += () => Feedback("Monster", "Parry!", 0);
			_monster.GuardComponent.BlockSuccessful += chip => Feedback("Monster", $"Block (-{chip:0})", chip);
		}
		if (_monster.DefenseController != null)
		{
			_monster.DefenseController.RiposteTriggered += () => Feedback("Monster", "Riposte!", 0);
		}
		Configure(controller);
		ApplyDefense();
		if (!SetAutomation((int)_automation)) SetAutomation(0);

		var hurtboxesToggle = GetNodeOrNull<CheckButton>("%Controls/Hurtboxes");
		if (hurtboxesToggle != null && hurtboxesToggle.ButtonPressed)
		{
			UpdateHurtboxDebugDraw(true);
		}
	}

	public void MonsterAttack()
	{
		if (!GodotObject.IsInstanceValid(_monster) || _monster.IsDead) SpawnMonster();
		if (!GodotObject.IsInstanceValid(_monster)) return;

		Configure(_monster.GetNode<AttackController>("AttackController"));
		if (!_monster.EnemyBehaviorComponent.TryAttack()) Feedback("Monster", "Busy / interrupted", 0);
	}

	/// <summary>Change future requests only; automated actors retain their authored capabilities and normal action gates.</summary>
	public bool SetAutomation(int mode)
	{
		if (mode < 0 || mode > 3) return false;
		bool spawned = GodotObject.IsInstanceValid(_monster);
		if (spawned && (mode == 2 && _monster.DodgeComponent == null || mode == 3 && _monster.GuardComponent == null))
		{
			Feedback("Monster", "Selected behavior unavailable for this monster", 0);
			GetNode<OptionButton>("%Controls/Automation").Selected = (int)_automation;
			return false;
		}
		if (_automation == MonsterAutomation.Guard && spawned) _monster.GuardComponent?.EndGuard();
		_automation = (MonsterAutomation)mode;
		_automaticPause = _automaticGuardRemaining = 0;
		GetNode<OptionButton>("%Controls/Automation").Selected = mode;
		if (spawned && _monster.DefenseController != null)
			_monster.DefenseController.SetPhysicsProcess(mode < 2);
		return true;
	}

	/// <summary>Repeat production requests in gameplay time; never teleport actors, refill cooldowns or synthesize missing components.</summary>
	private void UpdateAutomation(float delta)
	{
		if (_automation == MonsterAutomation.Manual || !GodotObject.IsInstanceValid(_monster) || _monster.IsDead || Player.IsDead) return;
		_automaticPause = Mathf.Max(0, _automaticPause - delta);
		if (_automation == MonsterAutomation.Guard && _monster.GuardComponent?.IsGuarding == true)
		{
			_automaticGuardRemaining -= delta;
			if (_automaticGuardRemaining <= 0) _monster.GuardComponent.EndGuard();
			return;
		}
		if (_automaticPause > 0 || _monster.ActionGate.IsBusy || _monster.MovementComponent.IsPushed) return;
		Vector3 toward = Player.GlobalPosition - _monster.GlobalPosition;
		toward.Y = 0;
		if (toward.IsZeroApprox()) toward = Vector3.Back;
		toward = toward.Normalized();
		_monster.MovementComponent.FaceDirection(toward);
		bool accepted = false;
		switch (_automation)
		{
			case MonsterAutomation.Attack:
				accepted = _monster.EnemyBehaviorComponent.TryAttack();
				break;
			case MonsterAutomation.Dodge:
				if (_monster.DodgeComponent.CooldownRemaining > 0) return;
				accepted = _monster.DodgeComponent.TryDodge(toward.Cross(Vector3.Up) * (_dodgeRight ? 1 : -1)) == DefenseRequestResult.Success;
				if (accepted) _dodgeRight = !_dodgeRight;
				break;
			case MonsterAutomation.Guard:
				accepted = _monster.GuardComponent.TryBeginGuard(toward) == DefenseRequestResult.Success;
				if (accepted) _automaticGuardRemaining = .6f;
				break;
		}
		if (accepted)
		{
			_automaticPause = (float)GetNode<SpinBox>("%Controls/RepeatPause").Value;
			CombatTrace.Write(this, "harness.automatic_action", ("behavior", _automation.ToString()));
		}
	}

	public void ResetHealth()
	{
		Player.ActionGate.ResetForSimulation();
		Player.AttackController.CancelAttack();
		Player.MovementComponent.Stop();
		Player.HealthComponent.SetHealth(Player.Stats.MaxHealth, Player.Stats.MaxHealth);
		Player.MovementComponent.Push(Vector3.Zero, 0);
		Player.HurtBoxComponent.Poise?.Restore();
		_heroGuardHeld = false;
		Player.InputController.RequireFreshDefenseInput();
		if (Player.GetNode<AnimationTree>("AnimationTree").Get("parameters/playback").AsGodotObject() is AnimationNodeStateMachinePlayback playback)
			playback.Start("Idle");
		PracticeTarget.GetNode<HealthComponent>("HealthComponent").SetHealth(1000, 1000);
		if (GodotObject.IsInstanceValid(_monster) && !_monster.IsDead)
		{
			_monster.HealthComponent.SetHealth(_monster.HealthComponent.MaxHealth, _monster.HealthComponent.MaxHealth);
			_monster.MovementComponent.Push(Vector3.Zero, 0);
			_monster.HurtBoxComponent.Poise?.Restore();
		}
	}

	private void Configure(AttackController controller)
	{
		controller.DebugDrawEnabled = GetNode<CheckButton>("%Controls/Volumes").ButtonPressed;
		controller.EffectsEnabled = GetNode<CheckButton>("%Controls/Effects").ButtonPressed;
	}

	public void ApplyDefense()
	{
		int mode = GetNode<OptionButton>("%Controls/Defense").Selected;
		if (mode < 0) mode = 2;
		bool isInvulnerable = mode == 1;

		PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent").Invulnerable = isInvulnerable;

		if (GodotObject.IsInstanceValid(_monster) && !_monster.IsDead)
		{
			_monster.HurtBoxComponent.Invulnerable = isInvulnerable;

			var controller = _monster.DefenseController
				?? _monster.GetNodeOrNull<EnemyDefenseController>("EnemyDefenseController");
			if (controller != null)
			{
				controller.Mode = mode switch
				{
					2 => EnemyDefenseMode.ProfileDefault,
					3 => EnemyDefenseMode.Dodge,
					4 => EnemyDefenseMode.GuardParry,
					_ => EnemyDefenseMode.None
				};
			}
		}
	}

	private void UpdateHurtboxDebugDraw(bool enabled)
	{
		if (!enabled)
		{
			foreach (var mesh in _hurtboxMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(mesh)) mesh.QueueFree();
			}
			_hurtboxMeshes.Clear();
			return;
		}

		RefreshHurtboxMesh(Player?.HurtBoxComponent, new Color(0f, 0.75f, 1f, 0.25f)); // Cyan for hero
		RefreshHurtboxMesh(PracticeTarget?.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent"), new Color(0.2f, 1f, 0.4f, 0.25f)); // Green for target
		if (GodotObject.IsInstanceValid(_monster) && !_monster.IsDead)
		{
			RefreshHurtboxMesh(_monster.HurtBoxComponent, new Color(1f, 0.35f, 0f, 0.25f)); // Orange for monster
		}
	}

	private void RefreshHurtboxMesh(HurtBoxComponent hurtbox, Color color)
	{
		if (hurtbox == null || !GodotObject.IsInstanceValid(hurtbox)) return;
		if (_hurtboxMeshes.TryGetValue(hurtbox, out var existing) && GodotObject.IsInstanceValid(existing))
			return;

		var colShape = hurtbox.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (colShape?.Shape == null) return;

		var mesh = new MeshInstance3D
		{
			Name = "HurtboxDebugMesh",
			Mesh = colShape.Shape.GetDebugMesh(),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		mesh.MaterialOverride = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = true
		};
		colShape.AddChild(mesh);
		_hurtboxMeshes[hurtbox] = mesh;
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
		int frame = (int)(Engine.GetPhysicsFrames() - _startFrame);
		while (_commandIndex < _commands.Length && _commands[_commandIndex].Frame <= frame)
		{
			var command = _commands[_commandIndex++];
			ExecuteCommand(command.Command, command.Value, new Vector3(command.X, 0, command.Z));
		}
		if (_stopAfterFrames > 0 && frame >= _stopAfterFrames)
		{
			Recorder.Stop();
			GetTree().Quit();
			return;
		}
		_motionTime += (float)delta;
		if (GetNode<CheckButton>("%Controls/Moving").ButtonPressed)
			PracticeTarget.Position = new Vector3(Mathf.Sin(_motionTime * 1.5f) * 1.3f, 0, -2);
		UpdateAutomation((float)delta);
	}

	public override void _ExitTree()
	{
		Recorder?.Stop();
		Player.InputController.SetGuardIntent(false);
		UpdateHurtboxDebugDraw(false);
		Engine.TimeScale = _previousTimeScale;
	}

	public override void _Process(double delta)
	{
		string monster = GodotObject.IsInstanceValid(_monster)
			? $"Monster: {DescribeAction(_monster.ActionGate, _monster.IsDead)} · HP {_monster.HealthComponent.CurrentHealth:0}"
			: "Monster: Not spawned";
		_status.Text = $"Hero: {DescribeAction(Player.ActionGate, Player.IsDead)} · HP {Player.HealthComponent.CurrentHealth:0}\n{monster}\nPractice target: HP {PracticeTarget.GetNode<HealthComponent>("HealthComponent").CurrentHealth:0}";
		GetNode<Button>("%Controls/HeroGuard").Text = _heroGuardHeld ? "Release guard (L)" : "Guard / parry (L)";
	}

	/// <summary>Present gameplay state rather than animation style identifiers in the arena HUD.</summary>
	private static string DescribeAction(ActorActionGateComponent gate, bool dead)
	{
		if (dead) return "Dead";
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

	/// <summary>Execute one scenario command through production components, returning acceptance.</summary>
	public bool ExecuteCommand(string command, int value, Vector3 direction)
	{
		bool accepted;
		switch (command)
		{
			case "equip": accepted = value >= 0 && value < Weapons.Length; if (accepted) EquipPreview(value); break;
			case "spawn":
				accepted = value >= 0 && value < Monsters.Length;
				if (accepted) { GetNode<OptionButton>("%Controls/Monsters").Selected = value; SpawnMonster(); }
				break;
			case "defense":
				accepted = value >= 0 && value <= 4;
				if (accepted) { GetNode<OptionButton>("%Controls/Defense").Selected = value; ApplyDefense(); }
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
			case "move": Player.InputComponent.InputDirection = direction.Normalized(); Player.MovementComponent.SetInputDirection(direction); accepted = true; break;
			case "monster_attack":
				accepted = GodotObject.IsInstanceValid(_monster) && !_monster.IsDead;
				if (accepted) { Configure(_monster.AttackController); accepted = _monster.EnemyBehaviorComponent.TryAttack(); }
				break;
			case "automation": accepted = SetAutomation(value); break;
			case "spawn_distance":
				accepted = value >= 1 && value <= 12;
				if (accepted) GetNode<SpinBox>("%Controls/SpawnDistance").Value = value;
				break;
			case "monster_guard": accepted = GodotObject.IsInstanceValid(_monster) && _monster.GuardComponent?.TryBeginGuard(direction) == DefenseRequestResult.Success; break;
			case "monster_dodge": accepted = GodotObject.IsInstanceValid(_monster) && _monster.DodgeComponent?.TryDodge(direction) == DefenseRequestResult.Success; break;
			case "position_monster": accepted = GodotObject.IsInstanceValid(_monster); if (accepted) _monster.Position = new Vector3(direction.X, _monster.Position.Y, direction.Z); break;
			case "position_player": Player.Position = new Vector3(direction.X, Player.Position.Y, direction.Z); accepted = true; break;
			case "reset": ResetHealth(); accepted = true; break;
			default: accepted = false; break;
		}
		CombatTrace.Write(this, "harness.command", ("command", command), ("value", value), ("direction", CombatTrace.Vector(direction)), ("accepted", accepted));
		return accepted;
	}

	private sealed class HarnessCommand
	{
		public int Frame { get; set; }
		public string Command { get; set; } = "";
		public int Value { get; set; }
		public float X { get; set; }
		public float Z { get; set; }
	}

}