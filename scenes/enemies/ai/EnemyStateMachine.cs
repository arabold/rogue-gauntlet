using System.Collections.Generic;

/// <summary>
/// One enemy behavior state in the <see cref="EnemyStateMachine"/>. <see cref="Update"/> returns the
/// id of the state to switch to, or null to stay. The machine runs <see cref="Exit"/> on the old
/// state and <see cref="Enter"/> on the new one only around an actual switch; staying (null) or
/// returning the current id runs neither.
/// </summary>
/// <remarks>
/// States are plain C# objects, created once per enemy and reused for its lifetime, so any field a
/// state declares persists across re-entry exactly like the per-component fields it replaced. Shared
/// data that several states need lives on <see cref="EnemyContext"/> instead.
/// </remarks>
public interface IEnemyState
{
	/// <summary>The behavior id this state represents; also the value surfaced as CurrentBehavior.</summary>
	EnemyBehaviorState Id { get; }

	/// <summary>Runs once when the machine switches into this state.</summary>
	void Enter(EnemyContext ctx);

	/// <summary>
	/// Runs every physics frame while this state is active and the enemy is not mid-action. Returns
	/// the id of the state to switch to, or null to stay in this state.
	/// </summary>
	EnemyBehaviorState? Update(EnemyContext ctx, double delta);

	/// <summary>Runs once when the machine switches out of this state.</summary>
	void Exit(EnemyContext ctx);
}

/// <summary>
/// A state with no per-frame behavior, used for Sleeping, Guarding, and Dead where the enemy simply
/// holds still. Preserves the original behavior ladder, which had no branch for these ids and so did
/// nothing while in them.
/// </summary>
public sealed class PassiveState : IEnemyState
{
	public EnemyBehaviorState Id { get; }

	public PassiveState(EnemyBehaviorState id)
	{
		Id = id;
	}

	public void Enter(EnemyContext ctx)
	{
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		return null;
	}

	public void Exit(EnemyContext ctx)
	{
	}
}

/// <summary>
/// Drives the enemy behavior layer. Holds one reusable instance of each <see cref="IEnemyState"/> so
/// state-local fields persist across re-entry (mirroring the old per-component fields), and exposes
/// <see cref="CurrentId"/> as the source of truth for CurrentBehavior.
/// </summary>
/// <remarks>
/// The initial state is set WITHOUT calling <see cref="IEnemyState.Enter"/>, matching the original
/// <c>_Ready</c>, which assigned CurrentBehavior but ran no state-entry logic. Enter/Exit only fire
/// on real transitions thereafter.
/// </remarks>
public sealed class EnemyStateMachine
{
	private readonly Dictionary<EnemyBehaviorState, IEnemyState> _states = new();
	private readonly EnemyContext _context;
	private IEnemyState _current;

	public EnemyBehaviorState CurrentId => _current.Id;

	public EnemyStateMachine(EnemyContext context, IEnumerable<IEnemyState> states, EnemyBehaviorState initial)
	{
		_context = context;
		foreach (IEnemyState state in states)
		{
			_states[state.Id] = state;
		}
		_current = _states[initial];
	}

	/// <summary>Runs the active state for one physics frame and applies any requested transition.</summary>
	public void Tick(double delta)
	{
		EnemyBehaviorState? next = _current.Update(_context, delta);
		if (next.HasValue)
		{
			ChangeState(next.Value);
		}
	}

	/// <summary>
	/// Switches to <paramref name="id"/>, running Exit on the old state and Enter on the new one. A
	/// no-op when already in that state, so Enter/Exit never fire for a same-state change. Also used
	/// by the host for forced transitions such as death.
	/// </summary>
	public void ChangeState(EnemyBehaviorState id)
	{
		if (id == _current.Id)
		{
			return;
		}

		_current.Exit(_context);
		_current = _states[id];
		_current.Enter(_context);
	}
}
