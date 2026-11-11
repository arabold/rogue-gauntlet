using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns monsters, or the practice dummy, in a ring around the player, and tunes what it spawned: AI, counterattacks,
/// forced defense, invulnerability and kill rewards. By default a spawn plays like a regular monster: it targets the
/// player at once, attacks, defends and drops its rewards; the combat arena authors the passive settings it needs.
/// Spawns are the production actor scenes on the world's navigation. They carry no persistent id, so a floor never
/// saves them and killing one clears nothing.
/// </summary>
[GlobalClass]
public partial class MonsterTool : DebugTool
{
	private const int KillAllId = 0;
	private const int ClearId = 1;
	private const int AIId = 2;
	private const int InvulnerableId = 3;
	private const int RewardsId = 4;
	private const int RiposteId = 5;
	private static readonly int[] Counts = [1, 3, 5];
	private static readonly (string Label, EnemyDefenseMode Mode)[] DefenseModes =
	[
		("Monster default", EnemyDefenseMode.ProfileDefault),
		("No defense", EnemyDefenseMode.None),
		("Always dodge", EnemyDefenseMode.Dodge),
		("Always guard / parry", EnemyDefenseMode.GuardParry),
	];

	/// <summary>A target this tool spawned is in the world, configured and facing the player.</summary>
	[Signal] public delegate void TargetSpawnedEventHandler(Node3D target);

	/// <summary>Folder whose monster scenes the spawn submenu lists, so a new monster shows up without a menu edit.</summary>
	[Export] public string MonsterDirectory { get; set; } = "res://scenes/enemies";

	/// <summary>Stationary target listed first in the spawn submenu; unassigned, the menu offers monsters only.</summary>
	[Export] public PackedScene PracticeTargetScene { get; set; }

	/// <summary>
	/// Whether spawned monsters make their own decisions: chasing, attacking and choosing defenses. Off, they stand still
	/// and act only when told to, as the combat harness expects; their attack and defense execution stays live either way.
	/// </summary>
	[Export]
	public bool AIEnabled
	{
		get;
		set
		{
			field = value;
			foreach (EnemyBase monster in SpawnedMonsters) { ApplyAI(monster); }
		}
	} = true;

	/// <summary>Spawned monsters may counterattack after a guard, as their defense profile allows; off disables it.</summary>
	[Export]
	public bool RiposteEnabled
	{
		get;
		set
		{
			field = value;
			foreach (EnemyBase monster in SpawnedMonsters) { ApplyRiposte(monster); }
		}
	} = true;

	/// <summary>Defense forced onto spawned monsters; the default leaves each monster's authored chances.</summary>
	[Export]
	public EnemyDefenseMode DefenseMode
	{
		get;
		set
		{
			field = value;
			foreach (EnemyBase monster in SpawnedMonsters)
			{
				if (monster.DefenseController != null) { monster.DefenseController.Mode = value; }
			}
		}
	}

	/// <summary>Spawned targets take hits without losing health.</summary>
	[Export]
	public bool Invulnerable
	{
		get;
		set
		{
			field = value;
			foreach (Node3D target in Spawned)
			{
				if (HurtBoxOf(target) is { } hurtBox) { hurtBox.Invulnerable = value; }
			}
		}
	}

	/// <summary>Spawned monsters drop loot and award experience when killed, on a floor that can hold the drops.</summary>
	[Export]
	public bool RewardsEnabled
	{
		get;
		set
		{
			field = value;
			foreach (EnemyBase monster in SpawnedMonsters)
			{
				if (monster.DeathComponent != null) { monster.DeathComponent.RewardsEnabled = value; }
			}
		}
	} = true;

	/// <summary>How many copies one spawn places, evenly around the player starting straight ahead.</summary>
	[Export(PropertyHint.Range, "1,8")] public int Count { get; set => field = Mathf.Max(1, value); } = 1;

	/// <summary>The live targets this tool spawned, in spawn order.</summary>
	public IReadOnlyList<Node3D> Spawned
	{
		get
		{
			_spawned.RemoveAll(target => !GodotObject.IsInstanceValid(target) || !target.IsInsideTree());
			return _spawned;
		}
	}

	private IEnumerable<EnemyBase> SpawnedMonsters => Spawned.OfType<EnemyBase>().Where(monster => !monster.IsDead);

	private readonly List<Node3D> _spawned = [];
	private readonly List<PackedScene> _spawnScenes = [];
	private PopupMenu _spawnMenu;
	private PopupMenu _countMenu;
	private PopupMenu _defenseMenu;

	public override void BuildMenu(PopupMenu menu)
	{
		// Validating every monster scene instantiates it, so the list is built when the submenu first opens.
		_spawnMenu = new PopupMenu { Name = "SpawnMonsterMenu" };
		_spawnMenu.AboutToPopup += FillSpawnMenu;
		_spawnMenu.IdPressed += id => Spawn(_spawnScenes[(int)id]);
		menu.AddChild(_spawnMenu);
		menu.AddSubmenuNodeItem("Spawn", _spawnMenu);

		_countMenu = new PopupMenu { Name = "SpawnCountMenu" };
		foreach (int count in Counts) { _countMenu.AddRadioCheckItem(count.ToString(), count); }
		_countMenu.IdPressed += id => { Count = (int)id; RefreshMenu(menu); };
		menu.AddChild(_countMenu);
		menu.AddSubmenuNodeItem("Count", _countMenu);

		_defenseMenu = new PopupMenu { Name = "DefenseModeMenu" };
		foreach (var (label, mode) in DefenseModes) { _defenseMenu.AddRadioCheckItem(label, (int)mode); }
		_defenseMenu.IdPressed += id => { DefenseMode = (EnemyDefenseMode)(int)id; RefreshMenu(menu); };
		menu.AddChild(_defenseMenu);
		menu.AddSubmenuNodeItem("Defense", _defenseMenu);

		menu.AddCheckItem("Monster AI", AIId);
		menu.AddCheckItem("Riposte", RiposteId);
		menu.AddCheckItem("Invulnerable", InvulnerableId);
		menu.AddCheckItem("Rewards", RewardsId);
		menu.AddSeparator();
		menu.AddItem("Clear spawned", ClearId);
		menu.AddItem("Kill all enemies", KillAllId);
		menu.IdPressed += OnPressed;
	}

	public override void RefreshMenu(PopupMenu menu)
	{
		SetChecked(menu, AIId, AIEnabled);
		SetChecked(menu, RiposteId, RiposteEnabled);
		SetChecked(menu, InvulnerableId, Invulnerable);
		SetChecked(menu, RewardsId, RewardsEnabled);
		SetDisabled(menu, ClearId, Spawned.Count == 0);
		CheckRadio(_countMenu, Count);
		CheckRadio(_defenseMenu, (int)DefenseMode);
	}

	/// <summary>
	/// Places <see cref="Count"/> copies of <paramref name="scene"/> around the player at <paramref name="distance"/>, or
	/// at the distance a monster's movement and opening action are designed for. Each faces and targets the player with
	/// this tool's settings applied. Returns the copy straight ahead; null when nothing could spawn.
	/// </summary>
	public Node3D Spawn(PackedScene scene, float? distance = null)
	{
		Player player = Menu?.Player;
		IDebugWorld world = Menu?.DebugWorld;
		if (scene == null || player == null || world == null)
		{
			GD.PrintErr("Cannot spawn a monster without a scene, an active player and a world.");
			return null;
		}

		Node3D first = null;
		Vector3 ahead = -player.GlobalBasis.Z with { Y = 0 };
		ahead = ahead.IsZeroApprox() ? Vector3.Forward : ahead.Normalized();
		for (int index = 0; index < Count; index++)
		{
			Node3D target = Instantiate(scene);
			if (target == null)
			{
				break;
			}

			Vector3 direction = ahead.Rotated(Vector3.Up, Mathf.Tau * index / Count);
			Vector3 position = world.FindFreePositionNear(player.GlobalPosition + direction * (distance ?? PreferredDistance(target)));
			world.AddWorldNode(target, position);
			Prepare(target, player, world, index);
			_spawned.Add(target);
			first ??= target;
			EmitSignalTargetSpawned(target);
		}
		return first;
	}

	/// <summary>Removes every target this tool spawned, without killing them, so nothing drops or dies on screen.</summary>
	public void Clear()
	{
		foreach (Node3D target in Spawned)
		{
			target.GetParent()?.RemoveChild(target);
			target.QueueFree();
		}
		_spawned.Clear();
	}

	/// <summary>Kills every living enemy through its health, as lethal damage would.</summary>
	public void KillAll()
	{
		foreach (Node enemy in GetTree().GetNodesInGroup("enemy"))
		{
			HealthComponent health = enemy.GetNodeOrNull<HealthComponent>("HealthComponent");
			if (health != null && !health.IsDead)
			{
				health.TakeDamage(health.CurrentHealth);
			}
		}
	}

	/// <summary>A monster or practice target from <paramref name="scene"/>, configured before it enters the tree.</summary>
	private Node3D Instantiate(PackedScene scene)
	{
		var target = scene.Instantiate<Node3D>();
		switch (target)
		{
			case EnemyBase monster:
				monster.EnemyBehaviorComponent.AIEnabled = false;
				ApplyRiposte(monster);
				if (monster.DeathComponent != null) { monster.DeathComponent.RewardsEnabled = RewardsEnabled; }
				return monster;
			case CombatPracticeTarget:
				return target;
			default:
				target.Free();
				GD.PrintErr($"{scene.ResourcePath} is neither a monster nor a practice target.");
				return null;
		}
	}

	private void Prepare(Node3D target, Player player, IDebugWorld world, int index)
	{
		if (HurtBoxOf(target) is { } hurtBox) { hurtBox.Invulnerable = Invulnerable; }
		if (target is not EnemyBase monster)
		{
			return;
		}

		monster.ConfigureCombatRandom(world.RunSeed, $"debug.monster.{index}");
		if (world.Navigation != null) { monster.EnemyBehaviorComponent.Navigation?.SetNavigationOverride(world.Navigation); }
		if (monster.DefenseController != null)
		{
			monster.DefenseController.Target = player;
			monster.DefenseController.Mode = DefenseMode;
		}
		monster.EnemyBehaviorComponent.SetTarget(player);
		monster.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		Vector3 facing = player.GlobalPosition - monster.GlobalPosition;
		if (facing.TryNormalizeHorizontal(out Vector3 look))
		{
			monster.MovementComponent.SetLookAtDirection(look);
			monster.LookAt(monster.GlobalPosition + look, Vector3.Up);
		}
		ApplyAI(monster);
	}

	/// <summary>
	/// Turns AI off at once, or on two physics frames later: a monster spawned this frame first settles at its spot, as
	/// the combat harness always allowed.
	/// </summary>
	private async void ApplyAI(EnemyBase monster)
	{
		if (!AIEnabled)
		{
			monster.EnemyBehaviorComponent.AIEnabled = false;
			return;
		}

		SceneTree tree = GetTree();
		await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		if (AIEnabled && GodotObject.IsInstanceValid(monster) && !monster.IsDead)
		{
			monster.EnemyBehaviorComponent.AIEnabled = true;
		}
	}

	/// <summary>Counterattacks follow the toggle; whether a monster can riposte at all is its defense profile's call.</summary>
	private void ApplyRiposte(EnemyBase monster)
	{
		if (monster.DefenseController != null) { monster.DefenseController.RiposteEnabled = RiposteEnabled; }
	}

	/// <summary>The distance a monster's movement and opening action are designed for; the dummy stands at arm's length.</summary>
	private static float PreferredDistance(Node3D target)
	{
		if (target is not EnemyBase monster)
		{
			return 2f;
		}
		var combat = monster.Definition?.Behavior?.Combat;
		if (combat?.MovementStrategy is MaintainDistanceStrategy strategy && combat.PrimaryOffense is { } primary)
		{
			return Mathf.Clamp(strategy.PreferredDistance, primary.MinimumRange, primary.EffectiveMaximumRange);
		}
		return monster.AttackController.IsRanged ? 6f : 2f;
	}

	private static HurtBoxComponent HurtBoxOf(Node3D target) => target switch
	{
		EnemyBase monster => monster.HurtBoxComponent,
		CombatPracticeTarget dummy => dummy.HurtBox,
		_ => null,
	};

	private void FillSpawnMenu()
	{
		if (_spawnScenes.Count > 0)
		{
			return;
		}

		if (PracticeTargetScene != null)
		{
			_spawnMenu.AddItem("Practice dummy", _spawnScenes.Count);
			_spawnScenes.Add(PracticeTargetScene);
			_spawnMenu.AddSeparator();
		}
		foreach (var (scene, name) in DebugCatalog.Monsters(MonsterDirectory))
		{
			_spawnMenu.AddItem(name, _spawnScenes.Count);
			_spawnMenu.SetItemTooltip(_spawnMenu.ItemCount - 1, scene.ResourcePath);
			_spawnScenes.Add(scene);
		}
	}

	private void OnPressed(long id)
	{
		switch (id)
		{
			case KillAllId: KillAll(); break;
			case ClearId: Clear(); break;
			case AIId: AIEnabled = !AIEnabled; break;
			case RiposteId: RiposteEnabled = !RiposteEnabled; break;
			case InvulnerableId: Invulnerable = !Invulnerable; break;
			case RewardsId: RewardsEnabled = !RewardsEnabled; break;
		}
		RefreshMenu(Popup);
	}
}
