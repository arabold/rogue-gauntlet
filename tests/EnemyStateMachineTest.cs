namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="EnemyStateMachine"/> transition semantics — the control flow that
/// drives enemy behavior (idle/patrol/chase/flee/...). Pure: the machine is a plain class
/// and the states are an interface, so fake states with a null context exercise the
/// Enter/Exit/Update contract without a Godot runtime. (The navigation the real states call into
/// is covered by ClearanceNavigationTest and MonsterChaseTest.)
/// </summary>
[TestSuite]
public class EnemyStateMachineTest
{
	/// <summary>A fake state that records its callbacks and can script the next transition.</summary>
	private sealed class RecordingState : IEnemyState
	{
		private readonly EnemyBehaviorState? _nextOnUpdate;
		private readonly List<string> _log;

		public RecordingState(EnemyBehaviorState id, List<string> log, EnemyBehaviorState? nextOnUpdate = null)
		{
			Id = id;
			_log = log;
			_nextOnUpdate = nextOnUpdate;
		}

		public EnemyBehaviorState Id { get; }

		public void Enter(EnemyContext ctx) => _log.Add($"{Id}:Enter");

		public EnemyBehaviorState? Update(EnemyContext ctx, double delta) => _nextOnUpdate;

		public void Exit(EnemyContext ctx) => _log.Add($"{Id}:Exit");
	}

	[TestCase]
	public void InitialStateIsSetWithoutCallingEnter()
	{
		var log = new List<string>();
		var machine = new EnemyStateMachine(
			null,
			new IEnemyState[] { new RecordingState(EnemyBehaviorState.Idle, log) },
			EnemyBehaviorState.Idle);

		AssertObject(machine.CurrentId).IsEqual(EnemyBehaviorState.Idle);
		AssertArray(log).IsEmpty();
	}

	/// <summary>Staying put, a tick that requests the current state and a forced change to it run neither Enter nor Exit.</summary>
	[TestCase]
	public void NoTransitionRunsNeitherEnterNorExit()
	{
		var log = new List<string>();
		var staying = new EnemyStateMachine(null, new IEnemyState[] { new RecordingState(EnemyBehaviorState.Idle, log) }, EnemyBehaviorState.Idle);
		var requestingItself = new EnemyStateMachine(null,
			new IEnemyState[] { new RecordingState(EnemyBehaviorState.Idle, log, nextOnUpdate: EnemyBehaviorState.Idle) }, EnemyBehaviorState.Idle);
		var forced = new EnemyStateMachine(null,
			new IEnemyState[] { new RecordingState(EnemyBehaviorState.Patrolling, log) }, EnemyBehaviorState.Patrolling);

		staying.Tick(0.016);
		requestingItself.Tick(0.016);
		forced.ChangeState(EnemyBehaviorState.Patrolling);

		AssertObject(staying.CurrentId).IsEqual(EnemyBehaviorState.Idle);
		AssertObject(requestingItself.CurrentId).IsEqual(EnemyBehaviorState.Idle);
		AssertObject(forced.CurrentId).IsEqual(EnemyBehaviorState.Patrolling);
		AssertArray(log).IsEmpty();
	}

	[TestCase]
	public void TickTransitionsAndRunsExitThenEnterInOrder()
	{
		var log = new List<string>();
		var machine = new EnemyStateMachine(
			null,
			new IEnemyState[]
			{
				new RecordingState(EnemyBehaviorState.Idle, log, nextOnUpdate: EnemyBehaviorState.Chasing),
				new RecordingState(EnemyBehaviorState.Chasing, log),
			},
			EnemyBehaviorState.Idle);

		machine.Tick(0.016);

		AssertObject(machine.CurrentId).IsEqual(EnemyBehaviorState.Chasing);
		AssertArray(log).ContainsExactly("Idle:Exit", "Chasing:Enter");
	}

	[TestCase]
	public void ForcedChangeStateRunsExitThenEnter()
	{
		var log = new List<string>();
		var machine = new EnemyStateMachine(
			null,
			new IEnemyState[]
			{
				new RecordingState(EnemyBehaviorState.Chasing, log),
				new RecordingState(EnemyBehaviorState.Dead, log),
			},
			EnemyBehaviorState.Chasing);

		// Forced transition, e.g. the host killing the enemy regardless of current behavior.
		machine.ChangeState(EnemyBehaviorState.Dead);

		AssertObject(machine.CurrentId).IsEqual(EnemyBehaviorState.Dead);
		AssertArray(log).ContainsExactly("Chasing:Exit", "Dead:Enter");
	}
}