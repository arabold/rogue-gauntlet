namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class TimedInteractionTest
{
	private partial class DummyTarget : Node3D, IInteractionTarget
	{
		public string OperationId { get; set; } = "dummy_op";
		public string VerbLabel { get; set; } = "Operate";
		public float Duration { get; set; } = 1.0f;
		public float Range { get; set; } = 3.0f;
		public InteractionPermission RequiredPermission { get; set; } = InteractionPermission.None;
		public ulong StateRevision { get; set; } = 1;
		public int CommitCount { get; private set; } = 0;
		public InteractionReservation ActiveReservation => _currentReservation;
		public Node3D TargetNode => this;
		public bool IsInteractive => true;

		private InteractionReservation _currentReservation = InteractionReservation.None;
		private ulong _nextToken = 0;

		public InteractionOffer GetOffer(in ActorInteractionContext actor)
		{
			return new InteractionOffer(
				this,
				OperationId,
				VerbLabel,
				Duration,
				Range,
				RequiredPermission,
				StateRevision);
		}

		public bool TryReserve(in InteractionOffer offer, Node3D actor, out InteractionReservation reservation)
		{
			if (_currentReservation.IsValid || offer.TargetRevision != StateRevision)
			{
				reservation = InteractionReservation.None;
				return false;
			}
			_currentReservation = new InteractionReservation(this, actor, offer.OperationId, ++_nextToken, StateRevision);
			reservation = _currentReservation;
			return true;
		}

		public InteractionCommitResult TryCommit(in InteractionReservation reservation, in ActorInteractionContext actor)
		{
			if (reservation.Token != _currentReservation.Token || reservation.TargetRevision != StateRevision)
			{
				return InteractionCommitResult.InvalidReservation;
			}
			_currentReservation = InteractionReservation.None;
			StateRevision++;
			CommitCount++;
			return InteractionCommitResult.Success;
		}

		public void Release(in InteractionReservation reservation)
		{
			if (reservation.Token == _currentReservation.Token)
			{
				_currentReservation = InteractionReservation.None;
			}
		}
	}

	private (CharacterBody3D actor, ActorActionGateComponent gate, ActorInteractionComponent interaction) CreateTestActor(ActorCapabilities capabilities = null)
	{
		var actor = new CharacterBody3D { Position = Vector3.Zero };
		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		actor.AddChild(gate);
		var interaction = new ActorInteractionComponent { Name = "ActorInteractionComponent", Actor = actor, ActionGate = gate };
		actor.AddChild(interaction);
		return (actor, gate, interaction);
	}

	[TestCase]
	public void ExactAuthoredDurationsAdvanceAndCommitAtAuthoredTimes()
	{
		float[] testDurations = new[] { 1.0f, 5.0f, 10.0f };

		foreach (float duration in testDurations)
		{
			var (actor, gate, interaction) = CreateTestActor();
			var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = duration };
			var context = new ActorInteractionContext(actor, new ActorCapabilities());
			var offer = target.GetOffer(context);

			var result = interaction.TryBegin(offer, context);
			AssertInt((int)result).IsEqual((int)InteractionRequestResult.Success);
			AssertBool(interaction.IsActive).IsTrue();
			AssertBool(gate.IsBusy).IsTrue();
			AssertInt(target.CommitCount).IsEqual(0);

			// Halfway tick
			float halfTime = duration * 0.5f;
			interaction._PhysicsProcess(halfTime);
			AssertBool(interaction.IsActive).IsTrue();
			AssertInt(target.CommitCount).IsEqual(0);
			AssertFloat(interaction.Progress).IsEqualApprox(0.5f, 0.05f);

			// Complete the remaining half
			interaction._PhysicsProcess(halfTime + 0.01f);
			AssertBool(interaction.IsActive).IsFalse();
			AssertBool(gate.IsBusy).IsFalse();
			AssertInt(target.CommitCount).IsEqual(1);

			actor.Free();
			target.Free();
		}
	}

	[TestCase]
	public void OneCompletionPerPressAndNoAutoRestart()
	{
		var (actor, gate, interaction) = CreateTestActor();
		var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = 1.0f };
		var context = new ActorInteractionContext(actor, new ActorCapabilities());
		var offer = target.GetOffer(context);

		interaction.TryBegin(offer, context);
		interaction._PhysicsProcess(1.05f);

		AssertInt(target.CommitCount).IsEqual(1);
		AssertBool(interaction.IsActive).IsFalse();

		// Extra physics frames do NOT re-trigger commit
		interaction._PhysicsProcess(1.0f);
		interaction._PhysicsProcess(1.0f);
		AssertInt(target.CommitCount).IsEqual(1);

		actor.Free();
		target.Free();
	}

	[TestCase]
	public void TwoActorsContestingOneTargetResolvesFirstAndRejectsSecond()
	{
		var (actorA, gateA, interactionA) = CreateTestActor();
		var (actorB, gateB, interactionB) = CreateTestActor();
		var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = 1.0f };

		var contextA = new ActorInteractionContext(actorA, new ActorCapabilities());
		var contextB = new ActorInteractionContext(actorB, new ActorCapabilities());

		// Actor A begins interaction
		var resA = interactionA.TryBegin(target.GetOffer(contextA), contextA);
		AssertInt((int)resA).IsEqual((int)InteractionRequestResult.Success);
		AssertBool(target.ActiveReservation.IsValid).IsTrue();

		// Actor B tries to interact with the same target -> rejected due to reservation
		var resB = interactionB.TryBegin(target.GetOffer(contextB), contextB);
		AssertInt((int)resB).IsEqual((int)InteractionRequestResult.ReservationFailed);
		AssertBool(interactionB.IsActive).IsFalse();

		// After A finishes, target becomes available
		interactionA._PhysicsProcess(1.05f);
		AssertInt(target.CommitCount).IsEqual(1);
		AssertBool(target.ActiveReservation.IsValid).IsFalse();

		// Now Actor B can interact
		var resB2 = interactionB.TryBegin(target.GetOffer(contextB), contextB);
		AssertInt((int)resB2).IsEqual((int)InteractionRequestResult.Success);
		AssertBool(interactionB.IsActive).IsTrue();

		actorA.Free();
		actorB.Free();
		target.Free();
	}

	[TestCase]
	public void MovementCancelsInteractionWithoutCommit()
	{
		var (actor, gate, interaction) = CreateTestActor();
		var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = 1.0f };
		var context = new ActorInteractionContext(actor, new ActorCapabilities());

		interaction.TryBegin(target.GetOffer(context), context);
		interaction._PhysicsProcess(0.3f);
		AssertBool(interaction.IsActive).IsTrue();

		// Displace the actor
		actor.GlobalPosition = new Vector3(2.0f, 0, 0);
		actor.Position = new Vector3(2.0f, 0, 0);
		interaction._PhysicsProcess(0.1f);

		// Must be cancelled by movement
		AssertBool(interaction.IsActive).IsFalse();
		AssertBool(gate.IsBusy).IsFalse();
		AssertBool(target.ActiveReservation.IsValid).IsFalse();
		AssertInt(target.CommitCount).IsEqual(0);

		actor.Free();
		target.Free();
	}

	[TestCase]
	public void InputReleaseCancelsInteractionWithoutCommit()
	{
		var (actor, gate, interaction) = CreateTestActor();
		var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = 1.0f };
		var context = new ActorInteractionContext(actor, new ActorCapabilities());

		interaction.TryBegin(target.GetOffer(context), context);
		interaction._PhysicsProcess(0.5f);
		AssertBool(interaction.IsActive).IsTrue();

		// Player released interaction button
		interaction.Cancel(InteractionCancelReason.InputReleased);

		AssertBool(interaction.IsActive).IsFalse();
		AssertBool(gate.IsBusy).IsFalse();
		AssertBool(target.ActiveReservation.IsValid).IsFalse();
		AssertInt(target.CommitCount).IsEqual(0);

		actor.Free();
		target.Free();
	}

	[TestCase]
	public void DamageCancelsInteractionThroughActionGate()
	{
		var (actor, gate, interaction) = CreateTestActor();
		var target = new DummyTarget { Position = new Vector3(1, 0, 0), Duration = 1.0f };
		var context = new ActorInteractionContext(actor, new ActorCapabilities());

		interaction.TryBegin(target.GetOffer(context), context);
		interaction._PhysicsProcess(0.4f);
		AssertBool(interaction.IsActive).IsTrue();

		// Authoritative damage interrupts through the gate
		gate.CancelCurrent(ActionInterruptReason.Damage);

		AssertBool(interaction.IsActive).IsFalse();
		AssertBool(gate.IsBusy).IsFalse();
		AssertBool(target.ActiveReservation.IsValid).IsFalse();
		AssertInt(target.CommitCount).IsEqual(0);

		actor.Free();
		target.Free();
	}

	[TestCase]
	public void ActorPermissionsEnforcedBetweenHumanoidAndIncapableActor()
	{
		var humanoidCaps = new ActorCapabilities { CanOpenDoors = true };
		var ratCaps = new ActorCapabilities { CanOpenDoors = false };

		var (humanoid, gateH, interactionH) = CreateTestActor(humanoidCaps);
		var (rat, gateR, interactionR) = CreateTestActor(ratCaps);

		var doorTarget = new DummyTarget
		{
			Position = new Vector3(1, 0, 0),
			RequiredPermission = InteractionPermission.OpenDoors
		};

		var ratContext = new ActorInteractionContext(rat, ratCaps);
		var ratOffer = doorTarget.GetOffer(ratContext);
		var ratResult = interactionR.TryBegin(ratOffer, ratContext);

		// Rat is rejected with PermissionDenied
		AssertInt((int)ratResult).IsEqual((int)InteractionRequestResult.PermissionDenied);
		AssertBool(interactionR.IsActive).IsFalse();
		AssertBool(doorTarget.ActiveReservation.IsValid).IsFalse();

		// Humanoid succeeds
		var humanContext = new ActorInteractionContext(humanoid, humanoidCaps);
		var humanOffer = doorTarget.GetOffer(humanContext);
		var humanResult = interactionH.TryBegin(humanOffer, humanContext);

		AssertInt((int)humanResult).IsEqual((int)InteractionRequestResult.Success);
		AssertBool(interactionH.IsActive).IsTrue();

		humanoid.Free();
		rat.Free();
		doorTarget.Free();
	}

	[TestCase]
	public void DoorAndChestComponentsCommitAuthorityThroughInteraction()
	{
		var doorScene = GD.Load<PackedScene>("res://scenes/props/door.tscn");
		var door = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());

		var (actor, gate, interaction) = CreateTestActor(new ActorCapabilities { CanOpenDoors = true });
		var comp = door.GetNode<InteractiveComponent>("InteractiveComponent");

		var context = new ActorInteractionContext(actor, new ActorCapabilities { CanOpenDoors = true });
		var offer = comp.GetOffer(context);
		AssertString(offer.VerbLabel).IsEqual("Open Door");
		AssertFloat(offer.Duration).IsEqual(0.5f);

		// An unlocked door completes at half a second.
		var beginResult = interaction.TryBegin(offer, context);
		AssertInt((int)beginResult).IsEqual((int)InteractionRequestResult.Success);

		interaction._PhysicsProcess(0.45f);
		AssertBool(door.IsOpen).IsFalse();
		AssertBool(interaction.IsActive).IsTrue();
		interaction._PhysicsProcess(0.1f);

		AssertBool(door.IsOpen).IsTrue();
		AssertBool(interaction.IsActive).IsFalse();

		actor.Free();
	}

	[TestCase]
	public void DoorCanBeInteractedFromBothSides()
	{
		var doorScene = GD.Load<PackedScene>("res://scenes/props/door.tscn");
		var door = doorScene.Instantiate<Door>();
		door.Position = Vector3.Zero;
		TestFactories.AddToSceneTree(door);

		var (actor, gate, interaction) = CreateTestActor(new ActorCapabilities { CanOpenDoors = true });
		var comp = door.GetNode<InteractiveComponent>("InteractiveComponent");
		var context = new ActorInteractionContext(actor, new ActorCapabilities { CanOpenDoors = true });

		// Approach from side +Z (e.g. corridor at z = 1.5m)
		actor.Position = new Vector3(0, 0, 1.5f);
		actor.GlobalPosition = new Vector3(0, 0, 1.5f);
		var offerA = comp.GetOffer(context);
		var resA = interaction.TryBegin(offerA, context);
		AssertInt((int)resA).IsEqual((int)InteractionRequestResult.Success);
		interaction.Cancel(InteractionCancelReason.ActionInterrupted);

		// Approach from side -Z (e.g. room interior at z = -1.5m)
		actor.Position = new Vector3(0, 0, -1.5f);
		actor.GlobalPosition = new Vector3(0, 0, -1.5f);
		var offerB = comp.GetOffer(context);
		var resB = interaction.TryBegin(offerB, context);
		AssertInt((int)resB).IsEqual((int)InteractionRequestResult.Success);
		interaction.Cancel(InteractionCancelReason.ActionInterrupted);

		actor.Free();
	}

	[TestCase]
	public void DoorIndicatorBindsCorrectKeyAndPromptsOnlyWhenFocused()
	{
		var doorScene = GD.Load<PackedScene>("res://scenes/props/door.tscn");
		var door = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());

		var comp = door.GetNode<InteractiveComponent>("InteractiveComponent");
		var floatingLabel = comp.GetNode<FloatingLabel>("FloatingLabel");
		var keyBinding = floatingLabel.GetNode<KeyBinding>("%KeyBinding");

		// KeyBinding must have resolved "interact" (key E)
		AssertString(floatingLabel.ActionBinding).IsEqual("interact");
		AssertString(keyBinding.ActionBinding).IsEqual("interact");

		// Label must render on top of scene geometry and x-ray overlays
		AssertBool(floatingLabel.NoDepthTest).IsTrue();
		AssertInt(floatingLabel.RenderPriority).IsGreater(8);

		// Indicator stays hidden until explicitly focused by interaction ranking
		AssertBool(floatingLabel.Visible).IsFalse();

		comp.SetFocused(true);
		AssertBool(floatingLabel.Visible).IsTrue();

		comp.SetFocused(false);
		AssertBool(floatingLabel.Visible).IsFalse();
	}
}