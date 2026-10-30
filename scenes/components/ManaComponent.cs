using Godot;

/// <summary>
/// An actor's mana pool: a current value clamped to a maximum, regenerating in gameplay time. Spells pay from it;
/// nothing else does. The owner pushes the maximum and regeneration in (the player's from <see cref="PlayerStats"/>),
/// so the component knows nothing about stats or classes.
/// </summary>
[GlobalClass]
public partial class ManaComponent : Node
{
	/// <summary>
	/// Fires on every spend, edit and maximum change, but during regeneration only when the whole-number value
	/// changes or the pool fills, so listeners refresh a few times a second rather than every tick.
	/// </summary>
	[Signal] public delegate void ManaChangedEventHandler(float currentMana, float maxMana);
	/// <summary>A cost the pool could not cover was asked for; presentation cues (the HUD) react, nothing else changes.</summary>
	[Signal] public delegate void ManaShortfallEventHandler(float cost);

	private float _maxMana = 100;
	private float _currentMana = 100;

	// Declared before CurrentMana so scene loading assigns the maximum first and the current value clamps against it.
	/// <summary>Lowering the maximum clamps current mana down; raising it never refills.</summary>
	[Export(PropertyHint.Range, "0,500,1,or_greater")]
	public float MaxMana
	{
		get => _maxMana;
		set => SetMana(_currentMana, value);
	}

	/// <summary>Clamped to [0, <see cref="MaxMana"/>]; editable in the Remote Inspector to test casting.</summary>
	[Export(PropertyHint.Range, "0,500,0.1,or_greater")]
	public float CurrentMana
	{
		get => _currentMana;
		set => SetMana(value, _maxMana);
	}

	/// <summary>Mana regained per second; physics processing runs only while the pool is below its maximum.</summary>
	[Export(PropertyHint.Range, "0,50,0.1,or_greater")]
	public float RegenPerSecond
	{
		get;
		set
		{
			field = Mathf.Max(value, 0);
			UpdateRegeneration();
		}
	}

	public override void _Ready()
	{
		// Overriding _PhysicsProcess enables it at ready; a full pool has nothing to do.
		UpdateRegeneration();
	}

	/// <summary>Sets both values at once, so restoring a saved current value never clamps against a stale maximum.</summary>
	public void SetMana(float currentMana, float maxMana)
	{
		maxMana = Mathf.Max(maxMana, 0);
		currentMana = Mathf.Clamp(currentMana, 0, maxMana);
		if (Mathf.IsEqualApprox(currentMana, _currentMana) && Mathf.IsEqualApprox(maxMana, _maxMana))
		{
			return;
		}

		_maxMana = maxMana;
		_currentMana = currentMana;
		UpdateRegeneration();
		EmitSignalManaChanged(_currentMana, _maxMana);
	}

	public bool CanSpend(float amount) => amount >= 0 && amount <= _currentMana;

	/// <summary>Whether the pool covers <paramref name="cost"/>, announcing a shortfall when it does not; spends nothing.</summary>
	public bool CheckCost(float cost)
	{
		if (CanSpend(cost)) return true;
		EmitSignalManaShortfall(cost);
		return false;
	}

	/// <summary>Spends <paramref name="amount"/> when the pool covers it; otherwise spends nothing.</summary>
	public bool TrySpend(float amount)
	{
		if (!CanSpend(amount))
		{
			return false;
		}

		SetMana(_currentMana - amount, _maxMana);
		return true;
	}

	public override void _PhysicsProcess(double delta)
	{
		float previous = _currentMana;
		_currentMana = Mathf.Min(_currentMana + RegenPerSecond * (float)delta, _maxMana);
		if (_currentMana >= _maxMana || Mathf.Floor(_currentMana) > Mathf.Floor(previous))
		{
			EmitSignalManaChanged(_currentMana, _maxMana);
		}

		if (_currentMana >= _maxMana) SetPhysicsProcess(false);
	}

	private void UpdateRegeneration()
	{
		if (IsNodeReady())
		{
			SetPhysicsProcess(RegenPerSecond > 0 && _currentMana < _maxMana);
		}
	}
}