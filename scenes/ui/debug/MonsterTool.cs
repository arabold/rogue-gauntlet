using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns monsters, or the practice dummy, in a ring around the player or scattered across an arena, and tunes what it
/// spawned: AI, counterattacks,
/// forced defense, invulnerability and kill rewards. By default a spawn plays like a regular monster: it targets the
/// player at once, attacks, defends and drops its rewards; the combat arena authors the passive settings it needs.
/// Spawns are the production actor scenes on the world's navigation. They carry no persistent id, so a floor never
/// saves them and killing one clears nothing.
/// </summary>
[GlobalClass]
public partial class MonsterTool : DebugTool
{
	private static readonly int[] Counts = [1, 3, 5];

	/// <summary>The defense modes in menu order; harness scenarios pick them by this index.</summary>
	internal static readonly (string Label, EnemyDefenseMode Mode)[] DefenseModes =
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
			ApplySettingsToSpawned();
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
			ApplySettingsToSpawned();
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
			ApplySettingsToSpawned();
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
			ApplySettingsToSpawned();
		}
	} = true;

	/// <summary>How many copies one spawn places: evenly around the player starting straight ahead, or scattered (<see cref="ScatterSeparation"/>).</summary>
	[Export(PropertyHint.Range, "1,8")] public int Count { get; set => field = Mathf.Max(1, value); } = 1;

	/// <summary>
	/// On an arena, the spawn menu places each copy at a random free spot of the arena floor at least this far from the
	/// player and every other target, so a fight starts with room to move. 0, or a generated level, keeps the ring around
	/// the player. Scripted spawns (<see cref="Spawn"/>) always use the ring.
	/// </summary>
	[Export(PropertyHint.Range, "0,20,0.5,or_greater,suffix:m")] public float ScatterSeparation { get; set; }

	/// <summary>How far from the player scattered copies may stand, so they spawn in view; 0 allows the whole arena.</summary>
	[Export(PropertyHint.Range, "0,40,0.5,or_greater,suffix:m")] public float ScatterRadius { get; set; }

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
	/// <summary>Random spots tried per scattered copy before taking the roomiest one found.</summary>
	private const int ScatterAttempts = 48;
	private RandomNumberGenerator _scatterRandom;
	private PopupMenu _spawnMenu;
	private int _clearId;

	protected override void BuildMenu(PopupMenu menu)
	{
		// Validating every monster scene instantiates it, so the list is filled when the submenu first opens.
		_spawnMenu = AddSubmenu(menu, "Spawn", "SpawnMonsterMenu");
		_spawnMenu.AboutToPopup += FillSpawnMenu;

		PopupMenu counts = AddSubmenu(menu, "Count", "SpawnCountMenu");
		foreach (int count in Counts)
		{
			AddRadio(counts, count.ToString(), () => Count == count, () => Count = count);
		}
		PopupMenu defenses = AddSubmenu(menu, "Defense", "DefenseModeMenu");
		foreach (var (label, mode) in DefenseModes)
		{
			AddRadio(defenses, label, () => DefenseMode == mode, () => DefenseMode = mode);
		}

		AddToggle(menu, "Monster AI", () => AIEnabled, value => AIEnabled = value);
		AddToggle(menu, "Riposte", () => RiposteEnabled, value => RiposteEnabled = value);
		AddToggle(menu, "Invulnerable", () => Invulnerable, value => Invulnerable = value);
		AddToggle(menu, "Rewards", () => RewardsEnabled, value => RewardsEnabled = value);
		menu.AddSeparator();
		_clearId = AddAction(menu, "Clear spawned", Clear);
		AddAction(menu, "Kill all enemies", KillAll);
	}

	protected override void OnRefresh() => SetDisabled(Popup, _clearId, Spawned.Count == 0);

	/// <summary>
	/// Places <see cref="Count"/> copies of <paramref name="scene"/> around the player at <paramref name="distance"/>, or
	/// at the distance a monster's movement and opening action are designed for. Each faces and targets the player with
	/// this tool's settings applied. Returns the copy straight ahead; null when nothing could spawn.
	/// </summary>
	public Node3D Spawn(PackedScene scene, float? distance = null)
	{
		if (!CanSpawn(scene, out Player player, out IDebugWorld world)) return null;
		if (!(-player.GlobalBasis.Z).TryNormalizeHorizontal(out Vector3 ahead)) { ahead = Vector3.Forward; }
		return Place(scene, player, world, (target, index) => world.FindFreePositionNear(
			player.GlobalPosition + ahead.Rotated(Vector3.Up, Mathf.Tau * index / Count) * (distance ?? PreferredDistance(target))));
	}

	/// <summary>
	/// The spawn menu's placement: on an arena with a <see cref="ScatterSeparation"/>, <see cref="Count"/> copies at random
	/// free spots of its floor, each at least that far from the player and every other target; otherwise <see cref="Spawn"/>.
	/// Spots come from a stream of the world's run seed, so a harness session replays them. Returns the first copy.
	/// </summary>
	public Node3D Scatter(PackedScene scene)
	{
		if (ScatterSeparation <= 0 || Menu?.DebugWorld is not DebugArena arena) return Spawn(scene);
		if (!CanSpawn(scene, out Player player, out IDebugWorld world)) return null;
		_scatterRandom ??= GenerationRandom.Create(arena.RunSeed, "debug.monster.scatter");
		return Place(scene, player, world, (_, _) => FindScatteredPosition(arena, player));
	}

	private bool CanSpawn(PackedScene scene, out Player player, out IDebugWorld world)
	{
		player = Menu?.Player;
		world = Menu?.DebugWorld;
		if (scene != null && player != null && world != null) return true;
		GD.PrintErr("Cannot spawn a monster without a scene, an active player and a world.");
		return false;
	}

	/// <summary>Adds <see cref="Count"/> configured copies of <paramref name="scene"/>, each where <paramref name="positionFor"/> puts it.</summary>
	private Node3D Place(PackedScene scene, Player player, IDebugWorld world, System.Func<Node3D, int, Vector3> positionFor)
	{
		Node3D first = null;
		for (int index = 0; index < Count; index++)
		{
			Node3D target = Instantiate(scene);
			if (target == null)
			{
				break;
			}

			world.AddWorldNode(target, positionFor(target, index));
			Prepare(target, player, world, index);
			_spawned.Add(target);
			first ??= target;
			EmitSignalTargetSpawned(target);
		}
		return first;
	}

	/// <summary>
	/// A random free spot of the arena floor, within <see cref="ScatterRadius"/> of the player, at least
	/// <see cref="ScatterSeparation"/> from the player, every enemy and every target this tool placed, copies of the same
	/// spawn included; the roomiest spot tried when none is that far.
	/// </summary>
	private Vector3 FindScatteredPosition(DebugArena arena, Player player)
	{
		Vector3[] occupied = GetTree().GetNodesInGroup("enemy").OfType<Node3D>().Concat(Spawned)
			.Select(node => node.GlobalPosition).Append(player.GlobalPosition).ToArray();
		Rect2 field = arena.Bounds;
		Vector3 best = player.GlobalPosition;
		float bestRoom = -1;
		for (int attempt = 0; attempt < ScatterAttempts; attempt++)
		{
			Vector2 point = ScatterRadius > 0
				? new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z)
					+ Vector2.FromAngle(_scatterRandom.RandfRange(0, Mathf.Tau)) * ScatterRadius * Mathf.Sqrt(_scatterRandom.Randf())
				: new Vector2(_scatterRandom.RandfRange(field.Position.X, field.End.X), _scatterRandom.RandfRange(field.Position.Y, field.End.Y));
			// The arena's navigation covers only its floor; a spot past its edge has nowhere to stand.
			if (!field.HasPoint(point)) continue;
			Vector3 spot = arena.FindFreePositionNear(new Vector3(point.X, player.GlobalPosition.Y, point.Y));
			float room = occupied.Min(other => other.HorizontalDistanceTo(spot));
			if (room >= ScatterSeparation) return spot;
			if (room > bestRoom) (best, bestRoom) = (spot, room);
		}
		return best;
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
			Kill(enemy.GetNodeOrNull<HealthComponent>("HealthComponent"));
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
				ApplySettings(monster);
				return monster;
			case CombatPracticeTarget:
				ApplySettings(target);
				return target;
			default:
				target.Free();
				GD.PrintErr($"{scene.ResourcePath} is neither a monster nor a practice target.");
				return null;
		}
	}

	/// <summary>Seeds a spawned monster's combat, aims it at the player and starts its AI; navigation comes from its world.</summary>
	private void Prepare(Node3D target, Player player, IDebugWorld world, int index)
	{
		if (target is not EnemyBase monster)
		{
			return;
		}

		monster.ConfigureCombatRandom(world.RunSeed, $"debug.monster.{index}");
		if (monster.DefenseController != null) { monster.DefenseController.Target = player; }
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

	/// <summary>
	/// Applies this tool's invulnerability, counterattack, defense and reward settings; all are safe before the target
	/// enters the tree. Whether a monster can riposte at all is its defense profile's call.
	/// </summary>
	private void ApplySettings(Node3D target)
	{
		if (HurtBoxOf(target) is { } hurtBox) { hurtBox.Invulnerable = Invulnerable; }
		if (target is not EnemyBase monster)
		{
			return;
		}
		if (monster.DefenseController != null)
		{
			monster.DefenseController.RiposteEnabled = RiposteEnabled;
			monster.DefenseController.Mode = DefenseMode;
		}
		if (monster.DeathComponent != null) { monster.DeathComponent.RewardsEnabled = RewardsEnabled; }
	}

	private void ApplySettingsToSpawned()
	{
		foreach (Node3D target in Spawned) { ApplySettings(target); }
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

	/// <summary>The hurtbox of a spawned monster or practice target.</summary>
	internal static HurtBoxComponent HurtBoxOf(Node3D target) => target switch
	{
		EnemyBase monster => monster.HurtBoxComponent,
		CombatPracticeTarget dummy => dummy.HurtBox,
		_ => null,
	};

	private void FillSpawnMenu()
	{
		if (_spawnMenu.ItemCount > 0)
		{
			return;
		}

		if (PracticeTargetScene != null)
		{
			AddAction(_spawnMenu, "Practice dummy", () => Scatter(PracticeTargetScene));
			_spawnMenu.AddSeparator();
		}
		foreach (var (scene, name) in DebugCatalog.Monsters(MonsterDirectory))
		{
			AddAction(_spawnMenu, name, () => Scatter(scene));
			_spawnMenu.SetItemTooltip(_spawnMenu.ItemCount - 1, scene.ResourcePath);
		}
	}
}
