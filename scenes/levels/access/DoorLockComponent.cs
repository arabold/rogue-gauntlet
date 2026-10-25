using Godot;
using System.Linq;

/// <summary>Instance-owned mechanical lock. Inventory and lock commit before observers are notified; owners observe
/// <see cref="Changed"/> instead of being refreshed by each mutation site.</summary>
[Tool]
public partial class DoorLockComponent : Node
{
	[Signal] public delegate void ChangedEventHandler();
	[Export] public DoorLockDefinition Definition { get; set; }
	public bool IsLocked { get; private set; }
	private bool _initialized;
	/// <summary>Prompt for an actor who has no usable method.</summary>
	public string MissingLabel => (Definition?.RequiredKey?.Name ?? "Lockpick") + " needed";
	public void Initialize(DoorLockDefinition definition)
	{
		// Editor instances must survive half-authored definitions; gameplay rejects them.
		if (!Engine.IsEditorHint()) definition?.Validate();
		Definition = definition;
		IsLocked = definition != null;
		_initialized = true;
		EmitSignal(SignalName.Changed);
	}
	/// <summary>Authored locks apply their definition here; generated or restored state set earlier is kept.</summary>
	public override void _Ready()
	{
		if (!_initialized) Initialize(Definition);
	}
	/// <summary>The first method this actor may use and can pay for, or null while the lock stays shut for them.</summary>
	public UnlockMethod SelectMethod(in ActorInteractionContext actor)
	{
		if (!IsLocked || !actor.HasPermission(InteractionPermission.OpenDoors)) return null;
		foreach (var method in Definition.Methods)
			if (actor.HasPermission(method.Permission) && method.FindCost(Definition, actor.Inventory) != null) return method;
		return null;
	}
	/// <summary>Called only at the reserved completion boundary. Invalid attempts have no effects.</summary>
	public bool TryUnlock(in ActorInteractionContext actor, string operationId)
	{
		if (!IsLocked || !actor.HasPermission(InteractionPermission.OpenDoors)) return false;
		var method = Definition.Methods.FirstOrDefault(m => m.OperationId == operationId);
		if (method == null || !actor.HasPermission(method.Permission)) return false;
		var cost = method.FindCost(Definition, actor.Inventory);
		if (cost == null || !actor.Inventory.CanRemove(cost, 1).Success) return false;
		using var update = actor.Inventory.DeferChanges();
		actor.Inventory.RemoveItem(cost, 1);
		IsLocked = false;
		EmitSignal(SignalName.Changed);
		return true;
	}
	public void Restore(bool locked)
	{
		if (locked && Definition == null) throw new System.InvalidOperationException("Lock state has no definition.");
		IsLocked = locked;
		EmitSignal(SignalName.Changed);
	}
}