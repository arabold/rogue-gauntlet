using Godot;
using System.Linq;

/// <summary>Inspector-authored playground using the production equip and action paths.</summary>
public partial class AttackAnimationHarness : CanvasLayer
{
	[Export] public Player Player { get; set; }
	[Export] public Node3D PracticeTarget { get; set; }
	[Export] public PackedScene[] Monsters { get; set; } = System.Array.Empty<PackedScene>();
	[Export] public Weapon[] Weapons { get; set; } = System.Array.Empty<Weapon>();
	private InventoryItemSlot _previewItem;
	private OptionButton _selection;
	private Label _status;
	private Label _feedback;
	private EnemyBase _monster;
	private float _motionTime;
	private double _previousTimeScale;
	private readonly System.Collections.Generic.Queue<string> _history = new();

	public override void _Ready()
	{
		_selection = GetNode<OptionButton>("Controls/Weapons");
		_status = GetNode<Label>("Controls/Status");
		foreach (var weapon in Weapons) _selection.AddItem(weapon.Name);
		_selection.ItemSelected += index => EquipPreview((int)index);
		GetNode<Button>("Controls/Attack").Pressed += Attack;
		_feedback = GetNode<Label>("Controls/Feedback");
		_previousTimeScale = Engine.TimeScale;
		GetNode<CheckButton>("Controls/SlowMotion").Toggled += value => Engine.TimeScale = value ? .25 : _previousTimeScale;
		var defense = GetNode<OptionButton>("Controls/Defense");
		foreach (var mode in new[] { "No defense", "High evasion", "Heavy armor", "Invulnerable" }) defense.AddItem(mode);
		defense.ItemSelected += _ => ApplyDefense();
		var monsters = GetNode<OptionButton>("Controls/Monsters");
		foreach (var scene in Monsters) monsters.AddItem(System.IO.Path.GetFileNameWithoutExtension(scene.ResourcePath));
		GetNode<Button>("Controls/SpawnMonster").Pressed += SpawnMonster;
		GetNode<Button>("Controls/MonsterAttack").Pressed += MonsterAttack;
		GetNode<Button>("Controls/Reset").Pressed += ResetHealth;
		Player.AttackController.AttackFeedback += (result, amount) => Feedback("Hero", result, amount);
		PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent").DamageResolved += (result, amount) => Feedback("Target", ((DamageOutcome)result).ToString(), amount);
		if (Weapons.Length > 0) EquipPreview(0);
		ApplyDefense();
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
		ApplyDefense();
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
		if (GodotObject.IsInstanceValid(_monster)) _monster.QueueFree();
		_monster = Monsters[GetNode<OptionButton>("Controls/Monsters").Selected].Instantiate<EnemyBase>();
		_monster.Position = Player.Position + Vector3.Back * 2;
		_monster.GetNode<EnemyBehaviorComponent>("EnemyBehaviorComponent").AIEnabled = false;
		Player.GetParent().AddChild(_monster);
		_monster.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		_monster.EnemyBehaviorComponent.SetTarget(Player);
		_monster.MovementComponent.SetLookAtDirection((Player.GlobalPosition - _monster.GlobalPosition).Normalized());
		_monster.LookAt(Player.GlobalPosition);
		var controller = _monster.GetNode<AttackController>("AttackController");
		controller.AttackFeedback += (result, amount) => Feedback("Monster", result, amount);
		Configure(controller);
		ApplyDefense();
	}

	public void MonsterAttack()
	{
		if (!GodotObject.IsInstanceValid(_monster) || _monster.IsDead) SpawnMonster();
		if (!GodotObject.IsInstanceValid(_monster)) return;
		Configure(_monster.GetNode<AttackController>("AttackController"));
		if (!_monster.EnemyBehaviorComponent.TryMeleeAttack()) Feedback("Monster", "Busy / interrupted", 0);
	}

	public void ResetHealth()
	{
		Player.HealthComponent.SetHealth(Player.Stats.MaxHealth, Player.Stats.MaxHealth);
		Player.MovementComponent.Push(Vector3.Zero, 0);
		PracticeTarget.GetNode<HealthComponent>("HealthComponent").SetHealth(1000, 1000);
	}

	private void Configure(AttackController controller)
	{
		controller.DebugDrawEnabled = GetNode<CheckButton>("Controls/Volumes").ButtonPressed;
		controller.EffectsEnabled = GetNode<CheckButton>("Controls/Effects").ButtonPressed;
	}

	private void ApplyDefense()
	{
		int mode = GetNode<OptionButton>("Controls/Defense").Selected;
		void Apply(HurtBoxComponent hurtBox)
		{
			hurtBox.Evasion = mode == 1 ? 1000 : 0;
			hurtBox.Armor = mode == 2 ? 1000 : 0;
			hurtBox.Invulnerable = mode == 3;
		}
		Apply(Player.HurtBoxComponent);
		Apply(PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent"));
		if (GodotObject.IsInstanceValid(_monster)) Apply(_monster.HurtBoxComponent);
	}

	private void Feedback(string actor, string result, float damage)
	{
		_history.Enqueue($"{actor}: {result}{(damage > 0 ? $" ({damage:0.0})" : "")}");
		while (_history.Count > 6) _history.Dequeue();
		_feedback.Text = string.Join("\n", _history);
	}

	public override void _PhysicsProcess(double delta)
	{
		_motionTime += (float)delta;
		if (GetNode<CheckButton>("Controls/Moving").ButtonPressed)
			PracticeTarget.Position = new Vector3(Mathf.Sin(_motionTime * 1.5f) * 1.3f, 0, -2);
	}

	public override void _ExitTree() => Engine.TimeScale = _previousTimeScale;

	public override void _Process(double delta)
	{
		if (Player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var slot) && slot?.Item is Weapon weapon)
			_status.Text = $"{weapon.Name}: {weapon.AttackStyle} — {(Player.AttackController.IsAttacking ? "attacking" : Player.ActionManager.IsBusy ? "preparing" : "ready")}\nHero HP: {Player.HealthComponent.CurrentHealth:0} | Target HP: {PracticeTarget.GetNode<HealthComponent>("HealthComponent").CurrentHealth:0}";
	}
}