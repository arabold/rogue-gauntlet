using Godot;
using Godot.Collections;

/// <summary>
/// Debug harness for screen effects: runs the real gameplay scene (generated level, player, monsters), whose
/// <c>Main</c> binds the player to its screen-effects layer, and adds a panel to drive each effect.
/// Number keys 1–9 fade the matching effect in or out; sliders set intensity directly. The health slider sets
/// the player's real health, and the potion buttons apply the real potions' buffs, so the health and status
/// drivers and the status components (confusion, slowness) are exercised end to end.
/// </summary>
public partial class ScreenEffectHarness : Node
{
	private const float FadeSeconds = 0.8f;

	private readonly System.Collections.Generic.List<HSlider> _intensitySliders = [];
	private Player _player;
	private HSlider _healthSlider;

	/// <summary>Effects listed in the panel, in key order.</summary>
	[Export] public Array<ScreenEffect> Effects { get; set; } = [];

	/// <summary>Potions whose buffs the panel can apply to the player, e.g. poison, blindness and slowness.</summary>
	[Export] public Array<BuffedItem> Potions { get; set; } = [];

	/// <summary>Container the harness fills with controls.</summary>
	[Export] public Container Controls { get; set; }

	public override void _Ready()
	{
		BuildEffectRows();

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += BindPlayer,
			signalBus => signalBus.PlayerSpawned -= BindPlayer);

		// Main (a child) normally spawns the player before this runs; the signal covers later spawns.
		if (GetTree().GetFirstNodeInGroup("player") is Player player)
		{
			BindPlayer(player);
		}
	}

	/// <summary>Tracks the spawned player for the health and buff controls; Main binds the effects themselves.</summary>
	private void BindPlayer(Player player)
	{
		_player = player;
		if (_healthSlider == null)
		{
			BuildHealthRow();
			BuildBuffRows();
		}
	}

	public override void _Process(double delta)
	{
		for (int i = 0; i < Effects.Count; i++)
		{
			_intensitySliders[i].SetValueNoSignal(Effects[i].Intensity);
		}

		HealthComponent health = _player?.HealthComponent;
		if (_healthSlider != null && health != null && health.MaxHealth > 0f)
		{
			_healthSlider.SetValueNoSignal(health.CurrentHealth / health.MaxHealth);
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
		{
			return;
		}

		int index = (int)key.Keycode - (int)Key.Key1;
		if (index >= 0 && index < Effects.Count && index < 9)
		{
			ToggleEffect(Effects[index]);
			GetViewport().SetInputAsHandled();
		}
	}

	private static void ToggleEffect(ScreenEffect effect)
	{
		effect.FadeTo(effect.Intensity >= 0.5f ? 0f : 1f, FadeSeconds);
	}

	private void BuildEffectRows()
	{
		for (int i = 0; i < Effects.Count; i++)
		{
			ScreenEffect effect = Effects[i];
			var toggle = new Button { Text = $"{i + 1}  {effect.Name}", CustomMinimumSize = new Vector2(150, 0), FocusMode = Control.FocusModeEnum.None };
			toggle.Pressed += () => ToggleEffect(effect);

			HSlider slider = CreateSlider();
			slider.ValueChanged += value => effect.Intensity = (float)value;
			_intensitySliders.Add(slider);

			AddRow(toggle, slider);
		}
	}

	private void BuildHealthRow()
	{
		var label = new Label { Text = "Player health", CustomMinimumSize = new Vector2(150, 0) };
		_healthSlider = CreateSlider();
		_healthSlider.ValueChanged += value =>
		{
			HealthComponent health = _player.HealthComponent;
			health.SetHealth(Mathf.Max((float)value * health.MaxHealth, 0.01f), health.MaxHealth);
		};
		AddRow(label, _healthSlider);
	}

	private void BuildBuffRows()
	{
		foreach (BuffedItem potion in Potions)
		{
			var apply = new Button { Text = $"Apply {potion.Buff.Name}", FocusMode = Control.FocusModeEnum.None };
			apply.Pressed += () => _player.ApplyBuff(potion.Buff);
			Controls.AddChild(apply);
		}

		var clear = new Button { Text = "Clear buffs", FocusMode = Control.FocusModeEnum.None };
		clear.Pressed += () => _player.BuffController.ClearBuffs();
		Controls.AddChild(clear);
	}

	private static HSlider CreateSlider() => new()
	{
		MinValue = 0,
		MaxValue = 1,
		Step = 0.01,
		CustomMinimumSize = new Vector2(180, 0),
		SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		FocusMode = Control.FocusModeEnum.None,
	};

	private void AddRow(Control label, Control slider)
	{
		var row = new HBoxContainer();
		row.AddChild(label);
		row.AddChild(slider);
		Controls.AddChild(row);
	}
}