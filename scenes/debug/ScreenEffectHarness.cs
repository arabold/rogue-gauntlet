using Godot;
using Godot.Collections;

/// <summary>
/// Debug harness for screen effects: runs the real gameplay scene (generated level, player, monsters)
/// under a production <c>screen_effects.tscn</c> layer and adds a panel to drive each effect.
/// Number keys 1–9 fade the matching effect in or out; sliders set intensity directly; the health slider
/// sets the player's real health so the near-death driver is exercised end to end.
/// </summary>
public partial class ScreenEffectHarness : Node
{
	private const float FadeSeconds = 0.8f;

	private readonly System.Collections.Generic.List<HSlider> _intensitySliders = [];
	private Player _player;
	private HSlider _healthSlider;

	/// <summary>Effects listed in the panel, in key order.</summary>
	[Export] public Array<ScreenEffect> Effects { get; set; } = [];

	/// <summary>Blindness effect to focus on the spawned player.</summary>
	[Export] public BlindnessScreenEffect Blindness { get; set; }

	/// <summary>Health driver to bind to the spawned player.</summary>
	[Export] public HealthScreenEffectDriver HealthDriver { get; set; }

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

	/// <summary>Injects the spawned player as runtime context for the focus and health-driven effects.</summary>
	private void BindPlayer(Player player)
	{
		if (_player == player)
		{
			return;
		}

		_player = player;
		if (Blindness != null)
		{
			Blindness.Focus = _player;
		}

		if (HealthDriver != null)
		{
			HealthDriver.Health = _player.HealthComponent;
		}

		if (_healthSlider == null)
		{
			BuildHealthRow();
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