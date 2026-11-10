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
	[Export] public Node3D TargetParent { get; set; }
	[Export] public PackedScene PracticeTargetScene { get; set; }
	[Export] public string MonsterRootDirectory { get; set; } = "res://scenes/enemies";
	/// <summary>Spells offered for direct casts; each definition's scene is instanced once, as a wand would bind it.</summary>
	[Export] public SpellPool SpellPool { get; set; }
	private PackedScene[] _monsters = System.Array.Empty<PackedScene>();
	/// <summary>Discovered production monsters; assignable in tests without serializing a hand-maintained roster.</summary>
	public PackedScene[] Monsters { get => _monsters; set { _monsters = value ?? []; if (IsNodeReady()) RefreshMonsterChoices(); } }
	[Export] public Weapon[] Weapons { get; set; } = System.Array.Empty<Weapon>();
	[Export] public ulong Seed { get; set; } = 42;
	[Export] public CombatTraceRecorder Recorder { get; set; }
	[Export] public string TracePath { get; set; } = "";
	[Export] public NavigationProfile NavigationProfile { get; set; }
	private int _stopAfterFrames;
	private ulong _startFrame;
	private HarnessCommand[] _commands = [];
	private int _commandIndex;
	private InventoryItemSlot _previewItem;
	private OptionButton _selection;
	private Label _status;
	private Label _monsterActions;
	private readonly System.Text.StringBuilder _actionText = new();
	private ulong _actionPanelFrame;
	private Label _feedback;
	private EnemyBase _monster;
	private FloorNavigation _arenaNavigation;
	public EnemyBase Monster => _monster;
	private double _previousTimeScale;
	private readonly Queue<string> _history = new();
	private ICombatSpell[] _spells = [];
	private RandomNumberGenerator[] _spellRandoms = [];
	private readonly Queue<(int Spell, int Level)> _pendingCasts = new();
	/// <summary>The spell nodes instanced from <see cref="SpellPool"/>, in the pool's order.</summary>
	public IReadOnlyList<ICombatSpell> SpellComponents => _spells;
	/// <summary>Find a configured spell in the harness's explicitly assigned list.</summary>
	public T GetSpell<T>() where T : class, ICombatSpell => _spells.OfType<T>().FirstOrDefault();
	private SpellDefinition[] Spells => SpellPool?.Spells ?? [];
	private SpellDefinition SelectedSpell
	{
		get
		{
			int selected = GetNode<OptionButton>("%Controls/Spell").Selected;
			return selected >= 0 && selected < Spells.Length ? Spells[selected] : null;
		}
	}

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
		DisableHarnessKeyboardFocus(GetNode<Control>("%Controls"));
		_startFrame = Engine.GetPhysicsFrames();
		_previousTimeScale = Engine.TimeScale;
		Recorder ??= GetParent().GetNode<CombatTraceRecorder>("CombatTraceRecorder");
		GetParent().GetNodeOrNull<CombatCameraFeedbackController>("CombatCameraFeedbackController")?.Bind(Player);
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
		Player.AttackController.ConfigureRandom(GenerationRandom.DeriveSeed(Seed, "combat.attack", "player"));
		if (_monsters.Length == 0) _monsters = DiscoverMonsters();
		_arenaNavigation = CreateArenaNavigation();
		_spells = Spells.Select(InstantiateSpell).ToArray();
		if (_spells.Any(spell => string.IsNullOrWhiteSpace(spell.DisplayName) || spell.LevelCount < 1))
			throw new InvalidDataException("Harness spells require a name and at least one level.");
		_spellRandoms = Spells.Select(spell => new RandomNumberGenerator
			{ Seed = GenerationRandom.DeriveSeed(Seed, "combat.harness.spell", spell.ResourcePath) }).ToArray();
		var spellChoice = GetNode<OptionButton>("%Controls/Spell");
		foreach (var spell in _spells) spellChoice.AddItem(spell.DisplayName);
		spellChoice.ItemSelected += index => ExecuteCommand("spell", (int)index, Vector3.Zero);
		if (_spells.Length > 0) { spellChoice.Select(0); RefreshSpellLevels(0); }
		GetNode<Button>("%Controls/CastSpell").Disabled = _spells.Length == 0;
		GetNode<Button>("%Controls/CastSpell").Pressed += () =>
		{
			int selected = spellChoice.Selected, level = GetNode<OptionButton>("%Controls/SpellLevel").Selected + 1;
			if (selected >= 0 && selected < _spells.Length && level > 0) _pendingCasts.Enqueue((selected, level));
		};
		_selection = GetNode<OptionButton>("%Controls/Weapons");
		_status = GetNode<Label>("%Controls/Status");
		_monsterActions = GetNode<Label>("%Controls/MonsterActions");
		foreach (var weapon in Weapons) _selection.AddItem(weapon.Name);
		_selection.ItemSelected += index => ExecuteCommand("equip", (int)index, Vector3.Zero);
		GetNode<Button>("%Controls/Attack").Pressed += () => ExecuteCommand("attack", 0, Vector3.Zero);
		var heroGuardBtn = GetNodeOrNull<Button>("%Controls/HeroGuard");
		if (heroGuardBtn != null) heroGuardBtn.Pressed += () => ExecuteCommand(_heroGuardHeld ? "release_guard" : "guard", 0, Vector3.Zero);
		var heroDodgeBtn = GetNodeOrNull<Button>("%Controls/HeroDodge");
		if (heroDodgeBtn != null) heroDodgeBtn.Pressed += () => ExecuteCommand("dodge", 0, Vector3.Zero);
		_feedback = GetNode<Label>("%Controls/Feedback");
		var defense = GetNode<OptionButton>("%Controls/Defense");
		foreach (var mode in new[] { "Monster default", "No defense", "Always dodge", "Always guard / parry" }) defense.AddItem(mode);
		defense.Selected = 0;
		defense.ItemSelected += value => ExecuteCommand("defense", (int)value, Vector3.Zero);
		var monsters = GetNode<OptionButton>("%Controls/Monsters");
		RefreshMonsterChoices();
		if (monsters.ItemCount > 0) monsters.Selected = 0;
		GetNode<Button>("%Controls/SpawnMonster").Pressed += () => ExecuteCommand("spawn", GetNode<OptionButton>("%Controls/Monsters").Selected, Vector3.Zero);
		GetNode<Button>("%Controls/Reset").Pressed += () => ExecuteCommand("reset", 0, Vector3.Zero);

		BindToggle("SlowMotion", value => Engine.TimeScale = value ? .25 : _previousTimeScale);
		BindToggle("Hurtboxes", UpdateHurtboxDebugDraw);
		BindToggle("Volumes", _ => ConfigureAttackPresentation());
		BindToggle("Effects", value => { ConfigureAttackPresentation(); SetFeedbackEnabled(value); });
		BindToggle("TargetInvulnerable", ApplyTargetInvulnerability);
		BindToggle("MonsterAI", ApplyMonsterAI);
		BindToggle("CameraShake", value =>
		{
			var cameraFeedback = GetParent().GetNodeOrNull<CombatCameraFeedbackController>("CombatCameraFeedbackController");
			if (cameraFeedback != null) cameraFeedback.Enabled = value;
		});
		Recorder.Start(GetParent(), Seed, TracePath);

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
		CallDeferred(MethodName.SpawnMonster);
		if (Weapons.Length > 0) EquipPreview(0);
		ApplyDefense();
	}

	/// <summary>
	/// Discovered rosters by root directory. Validating instantiates every monster scene and dominates a harness load, and
	/// the authored scenes cannot change within one process, so later loads (test suites reload the arena per test) reuse
	/// the first result.
	/// </summary>
	private static readonly Dictionary<string, PackedScene[]> DiscoveredMonsters = [];

	/// <summary>Discover valid authored monster scenes locally so adding a monster never needs a harness roster edit.</summary>
	private PackedScene[] DiscoverMonsters()
	{
		if (!DiscoveredMonsters.TryGetValue(MonsterRootDirectory, out var roster))
			DiscoveredMonsters[MonsterRootDirectory] = roster = ScanMonsters();
		return [.. roster];
	}

	private PackedScene[] ScanMonsters()
	{
		var scenes = new List<PackedScene>();
		void Visit(string directory)
		{
			foreach (string name in ResourceLoader.ListDirectory(directory).OrderBy(value => value, StringComparer.Ordinal))
			{
				string path = directory.TrimEnd('/') + "/" + name.TrimStart('/');
				if (name.EndsWith("/", StringComparison.Ordinal))
				{
					if (path.Contains("/templates/", StringComparison.Ordinal)) continue;
					Visit(path.TrimEnd('/'));
					continue;
				}
				if (!path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)) continue;
				var scene = ResourceLoader.Load<PackedScene>(path);
				if (scene == null) continue;
				string[] errors = MonsterSceneValidator.ValidateScene(scene, out string definitionId);
				if (errors.Length == 0 && !string.IsNullOrWhiteSpace(definitionId)) scenes.Add(scene);
				else GD.PushWarning($"Combat harness skipped {path}: {string.Join(" ", errors)}");
			}
		}
		ResourceCacheGuard.Settle();
		Visit(MonsterRootDirectory.TrimEnd('/'));
		return scenes.OrderBy(MonsterDisplayName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(scene => scene.ResourcePath, StringComparer.Ordinal).ToArray();
	}

	private static string MonsterDisplayName(PackedScene scene)
	{
		var actor = scene.Instantiate<EnemyBase>();
		try { return actor.Definition?.DisplayName ?? Path.GetFileNameWithoutExtension(scene.ResourcePath); }
		finally { actor.Free(); }
	}

	private void RefreshMonsterChoices()
	{
		var selector = GetNodeOrNull<OptionButton>("%Controls/Monsters");
		if (selector == null) return;
		selector.Clear();
		selector.AddItem("Practice dummy");
		foreach (var scene in Monsters)
		{
			selector.AddItem(MonsterDisplayName(scene));
			selector.SetItemMetadata(selector.ItemCount - 1, scene.ResourcePath);
		}
	}

	/// <summary>
	/// Instances a definition's self-contained spell scene, the same scene a wand binds, beside the hero in the arena. The
	/// arena root is still adding its children, so the spell joins it once this frame's setup ends.
	/// </summary>
	private ICombatSpell InstantiateSpell(SpellDefinition definition)
	{
		var spell = definition?.Instantiate()
			?? throw new InvalidDataException($"Harness spell {definition?.ResourcePath} has no combat spell scene.");
		GetParent().CallDeferred(Node.MethodName.AddChild, (Node)spell);
		return spell;
	}

	/// <summary>
	/// Selects a spell for direct casts and binds it to the equipped wand or staff, so K exercises mana and the weapon's
	/// hit as in gameplay. The weapon reads its spell live, so only the caster's spell node is rebound.
	/// </summary>
	private void SelectSpell(int index)
	{
		GetNode<OptionButton>("%Controls/Spell").Select(index);
		RefreshSpellLevels(index);
		if (_previewItem?.Item is not MagicStaff staff || !staff.CanBind(Spells[index]) || ReferenceEquals(staff.BoundSpell, Spells[index])) return;
		staff.BoundSpell = Spells[index];
		if (!Player.Inventory.IsEquipped(_previewItem)) return;
		Player.AttackController.CancelAttack();
		Player.SpellCaster.Bind(staff.BoundSpell, staff);
	}

	private void RefreshSpellLevels(int index)
	{
		var levels = GetNode<OptionButton>("%Controls/SpellLevel");
		int selected = Mathf.Max(0, levels.Selected); levels.Clear();
		if (index < 0 || index >= _spells.Length) return;
		for (int level = 1; level <= _spells[index].LevelCount; level++) levels.AddItem($"Level {level}");
		levels.Select(Mathf.Min(selected, levels.ItemCount - 1));
	}

	/// <summary>Use the same application path for authored startup state and subsequent UI changes.</summary>
	private void BindToggle(string name, Action<bool> apply)
	{
		var toggle = GetNode<CheckButton>($"%Controls/{name}");
		toggle.Toggled += value => apply(value);
		apply(toggle.ButtonPressed);
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
		if (item is MagicStaff staff && staff.CanBind(SelectedSpell)) staff.BoundSpell = SelectedSpell;
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
		SpawnTarget(GetNode<OptionButton>("%Controls/Monsters").Selected);
	}

	private bool SpawnTarget(int selected)
	{
		bool isDummy = selected == 0;
		PackedScene scene = isDummy ? PracticeTargetScene : selected > 0 && selected <= Monsters.Length ? Monsters[selected - 1] : null;
		if (scene == null) return false;
		var candidate = scene.Instantiate<Node3D>();
		if (isDummy && candidate is not CombatPracticeTarget || !isDummy && candidate is not EnemyBase)
		{
			candidate.Free();
			GD.PushWarning($"Combat target scene has the wrong root: {scene.ResourcePath}");
			return false;
		}
		var candidateMonster = candidate as EnemyBase;
		if (candidateMonster != null)
		{
			candidateMonster.EnemyBehaviorComponent.AIEnabled = false;
			if (candidateMonster.DefenseController != null) candidateMonster.DefenseController.RiposteEnabled = false;
			if (candidateMonster.DeathComponent != null) candidateMonster.DeathComponent.RewardsEnabled = false;
		}
		float spawnDistance = GetSpawnDistance(candidateMonster);
		candidate.Position = Player.Position + Vector3.Forward * spawnDistance;
		if (GodotObject.IsInstanceValid(PracticeTarget))
		{
			var oldHurtBox = PracticeTarget is EnemyBase oldEnemy ? oldEnemy.HurtBoxComponent : PracticeTarget.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent");
			if (oldHurtBox != null) oldHurtBox.DebugDrawEnabled = false;
			PracticeTarget.GetParent()?.RemoveChild(PracticeTarget);
			PracticeTarget.QueueFree();
		}
		TargetParent.AddChild(candidate);
		candidate.ResetPhysicsInterpolation();
		candidateMonster?.EnemyBehaviorComponent.Navigation?.SetNavigationOverride(_arenaNavigation);
		PracticeTarget = candidate;
		_monster = candidateMonster;
		if (GetTargetHurtBox() is { } targetHurtBox)
			targetHurtBox.DamageResolved += (result, amount, _) => Feedback("Target", ((DamageOutcome)result).ToString(), amount);
		if (_monster != null)
		{
			_monster.ConfigureCombatRandom(Seed, "harness.monster");
			if (_monster.DefenseController != null) _monster.DefenseController.Target = Player;
			_monster.EnemyBehaviorComponent.SetTarget(Player);
			_monster.EnemyBehaviorComponent.SetAction(EnemyAction.None);
			_monster.MovementComponent.SetLookAtDirection((Player.GlobalPosition - _monster.GlobalPosition).Normalized());
			_monster.LookAt(Player.GlobalPosition);
			BindMonsterFeedback(_monster);
		}
		SetFeedbackEnabled(GetNode<CheckButton>("%Controls/Effects").ButtonPressed);
		GetNode<OptionButton>("%Controls/Defense").Disabled = _monster == null;
		CombatTrace.Write(this, "harness.spawned", ("scene", scene.ResourcePath), ("seed", Seed));
		if (GetNodeOrNull<CheckButton>("%Controls/Hurtboxes")?.ButtonPressed == true) UpdateHurtboxDebugDraw(true);
		ApplyDefense();
		ApplyTargetInvulnerability(GetNode<CheckButton>("%Controls/TargetInvulnerable").ButtonPressed);
		if (_monster != null) ApplyMonsterAI(GetNode<CheckButton>("%Controls/MonsterAI").ButtonPressed);
		return true;
	}

	/// <summary>Start monsters at the distance their authored movement and opening action are designed for.</summary>
	private static float GetSpawnDistance(EnemyBase monster)
	{
		var combat = monster?.Definition?.Behavior?.Combat;
		if (combat?.MovementStrategy is MaintainDistanceStrategy strategy && combat.PrimaryOffense is { } primary)
			return Mathf.Clamp(strategy.PreferredDistance, primary.MinimumRange, primary.EffectiveMaximumRange);
		return monster?.AttackController.IsRanged == true ? 6f : 2f;
	}

	/// <summary>Builds navigation over the static combat arena so production monster movement works without a Level.</summary>
	private FloorNavigation CreateArenaNavigation()
	{
		if (NavigationProfile == null) throw new InvalidDataException("Combat harness requires a NavigationProfile.");
		const float halfExtent = 20f;
		const float wallThickness = 1f;
		var bounds = new Rect2(-halfExtent, -halfExtent, halfExtent * 2, halfExtent * 2);
		var grid = new ClearanceGrid(bounds);
		grid.AddFloor(bounds);
		grid.AddObstacle(new Rect2(-halfExtent, -halfExtent, halfExtent * 2, wallThickness));
		grid.AddObstacle(new Rect2(-halfExtent, halfExtent - wallThickness, halfExtent * 2, wallThickness));
		grid.AddObstacle(new Rect2(-halfExtent, -halfExtent, wallThickness, halfExtent * 2));
		grid.AddObstacle(new Rect2(halfExtent - wallThickness, -halfExtent, wallThickness, halfExtent * 2));
		grid.Freeze();
		return new FloorNavigation(grid, NavigationProfile);
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
		Configure(monster.AttackController);
	}

	/// <summary>Toggle a small, stationary crowd using the selected real monster scene.</summary>
	public void MonsterAttack()
	{
		if (!GodotObject.IsInstanceValid(_monster) || _monster.IsDead) SpawnMonster();
		if (!GodotObject.IsInstanceValid(_monster)) return;

		Configure(_monster.GetNode<AttackController>("AttackController"));
		if (!_monster.EnemyBehaviorComponent.TryAttack()) Feedback("Monster", "Busy / interrupted", 0);
	}

	/// <summary>Change future requests only; automated actors retain their authored capabilities and normal action gates.</summary>
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
		var targetHealth = GetTargetHealth();
		if (targetHealth != null) targetHealth.SetHealth(targetHealth.MaxHealth, targetHealth.MaxHealth);
		if (PracticeTarget is CombatPracticeTarget dummy)
		{
			dummy.Movement?.Stop();
			dummy.Movement?.Push(Vector3.Zero, 0);
			dummy.ActionGate?.ResetForSimulation(); // Ends a stagger, so the restored dummy no longer takes punish damage.
			dummy.HurtBox?.Poise?.Restore();
		}
		if (GodotObject.IsInstanceValid(_monster) && !_monster.IsDead)
		{
			_monster.HealthComponent.SetHealth(_monster.HealthComponent.MaxHealth, _monster.HealthComponent.MaxHealth);
			_monster.MovementComponent.Push(Vector3.Zero, 0);
			_monster.DashComponent?.Cancel();
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
		if (mode < 0) mode = 0;
		if (_monster?.DefenseController is { } controller)
			controller.Mode = mode switch { 0 => EnemyDefenseMode.ProfileDefault, 1 => EnemyDefenseMode.None,
				2 => EnemyDefenseMode.Dodge, 3 => EnemyDefenseMode.GuardParry, _ => EnemyDefenseMode.ProfileDefault };
	}

	private HurtBoxComponent GetTargetHurtBox() => PracticeTarget switch
	{
		EnemyBase enemy => enemy.HurtBoxComponent,
		CombatPracticeTarget dummy => dummy.HurtBox,
		_ => null
	};
	private HealthComponent GetTargetHealth() => PracticeTarget switch
	{
		EnemyBase enemy => enemy.HealthComponent,
		CombatPracticeTarget dummy => dummy.Health,
		_ => null
	};
	private void ApplyTargetInvulnerability(bool enabled) { if (GetTargetHurtBox() is { } hurtBox) hurtBox.Invulnerable = enabled; }
	private async void ApplyMonsterAI(bool enabled)
	{
		var monster = _monster;
		if (!GodotObject.IsInstanceValid(monster) || monster.EnemyBehaviorComponent is not { } behavior) return;
		if (!enabled) { behavior.AIEnabled = false; return; }
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		if (GodotObject.IsInstanceValid(monster) && ReferenceEquals(_monster, monster)
			&& GetNode<CheckButton>("%Controls/MonsterAI").ButtonPressed) behavior.AIEnabled = true;
	}
	private void SetFeedbackEnabled(bool enabled)
	{
		var playerFeedback = Player?.GetNodeOrNull<CombatFeedbackController>("CombatFeedbackController");
		if (playerFeedback != null) playerFeedback.Enabled = enabled;
		var targetFeedback = PracticeTarget?.GetNodeOrNull<CombatFeedbackController>("CombatFeedbackController");
		if (targetFeedback != null) targetFeedback.Enabled = enabled;
	}

	private void UpdateHurtboxDebugDraw(bool enabled)
	{
		if (Player?.HurtBoxComponent is { } playerHurtbox) playerHurtbox.DebugDrawEnabled = enabled;
		if (GetTargetHurtBox() is { } targetHurtbox) targetHurtbox.DebugDrawEnabled = enabled;
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
		while (_pendingCasts.TryDequeue(out var cast))
		{
			// Direct casts use the hero's current weapon hit, as a cast from that weapon would.
			var context = new SpellCastContext(Player, null, SpellTargetIntent.Hostile,
				new SpellPower(cast.Level, Player.Stats.ResolveAttack(Player.Inventory.EquippedWeapon)));
			if (!_spells[cast.Spell].Cast(in context, _spellRandoms[cast.Spell]))
				GD.PushError($"{_spells[cast.Spell].DisplayName} preview could not cast.");
		}
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
	}

	public override void _ExitTree()
	{
		foreach (var spell in _spells) spell.Cancel();
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
		var targetHealth = GetTargetHealth();
		_status.Text = $"Hero: {DescribeAction(Player.ActionGate, Player.IsDead)} · HP {Player.HealthComponent.CurrentHealth:0}\n{(_monster == null ? "Dummy" : monster)} · HP {targetHealth?.CurrentHealth:0}";
		GetNode<Button>("%Controls/HeroGuard").Text = _heroGuardHeld ? "Release guard (L)" : "Guard / parry (L)";
	}

	/// <summary>Refresh the action tuning panel; it evaluates actions, so it runs in the physics step like the AI.</summary>
	private void UpdateActionPanel()
	{
		if (_monsterActions == null || ++_actionPanelFrame % 6 != 0) return;
		_monsterActions.Text = GodotObject.IsInstanceValid(_monster) ? DescribeActions(_monster) : "Monster actions appear here.";
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

	/// <summary>Execute one scenario command through production components, returning acceptance.</summary>
	public bool ExecuteCommand(string command, int value, Vector3 direction)
	{
		bool accepted;
		switch (command)
		{
			case "equip": accepted = value >= 0 && value < Weapons.Length; if (accepted) EquipPreview(value); break;
			case "spell": accepted = value >= 0 && value < Spells.Length; if (accepted) SelectSpell(value); break;
			case "spawn":
				accepted = value >= 0 && value <= Monsters.Length;
				if (accepted) { GetNode<OptionButton>("%Controls/Monsters").Selected = value; SpawnMonster(); }
				break;
			case "defense":
				accepted = value >= 0 && value <= 3;
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
			// Buffered presses go through the input controller like a device press; acceptance means buffered.
			case "press_primary": Player.InputController.PushIntent(PlayerIntent.Primary); accepted = true; break;
			case "press_secondary": Player.InputController.PushIntent(PlayerIntent.Secondary); accepted = true; break;
			case "press_dodge": Player.InputController.PushIntent(PlayerIntent.Dodge); accepted = true; break;
			case "move": Player.InputComponent.InputDirection = direction.Normalized(); Player.MovementComponent.SetInputDirection(direction); accepted = true; break;
			case "monster_attack":
				accepted = GodotObject.IsInstanceValid(_monster) && !_monster.IsDead;
				if (accepted) { Configure(_monster.AttackController); accepted = _monster.EnemyBehaviorComponent.TryAttack(); }
				break;
			case "automation":
				accepted = value is 0 or 1;
				if (accepted)
				{
					var monsterAi = GetNode<CheckButton>("%Controls/MonsterAI");
					monsterAi.SetPressedNoSignal(value == 1);
					ApplyMonsterAI(value == 1);
				}
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
