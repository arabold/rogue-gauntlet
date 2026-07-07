using Godot;
using System;
using System.Linq;

public partial class InventoryPanel : ScrollContainer
{
	private const float ItemSlotWidth = 128;

	[Export] public PackedScene InventoryItemScene;

	/// <summary>
	/// Banner shown while choosing a target for a scroll effect. Lives outside this
	/// scene (authored above the inventory area in <c>character_dialog.tscn</c>) and is
	/// injected by the parent, since a <see cref="ScrollContainer"/> can only lay out
	/// one scrollable child.
	/// </summary>
	[Export] public Label TargetingBanner { get; set; }

	public GridContainer InventoryGrid;
	private Inventory _inventory;
	private Player _player;
	private Action _unsubscribeInventory = () => { };
	private InventoryTargetRequest _activeTarget;

	public override void _Ready()
	{
		InventoryGrid = GetNode<GridContainer>("%InventoryGrid");
		Resized += RecalculateColumns;
		RecalculateColumns();

		var contextMenu = GetNode<InventoryItemContextMenu>("%InventoryItemContextMenu");
		contextMenu.TargetedUseRequested += OnTargetedUseRequested;

		Update();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_activeTarget == null)
		{
			return;
		}

		if (@event.IsActionReleased("ui_cancel"))
		{
			CancelTargeting();
			GetViewport().SetInputAsHandled();
		}
	}

	private void RecalculateColumns()
	{
		float separation = InventoryGrid.GetThemeConstant("h_separation");
		float availableWidth = GetRect().Size.X;
		InventoryGrid.Columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + separation) / (ItemSlotWidth + separation)));
	}

	public void Initialize(Inventory inventory, Player player = null)
	{
		CancelTargeting();

		_unsubscribeInventory();
		_unsubscribeInventory = () => { };

		_inventory = inventory;
		_player = player;
		if (_inventory != null)
		{
			_unsubscribeInventory = this.SubscribeUntilExit(
				_inventory,
				inventory =>
				{
					inventory.Changed += Update;
					inventory.ItemEquipped += OnItemEquipped;
					inventory.ItemUnequipped += OnItemUnequipped;
				},
				inventory =>
				{
					inventory.Changed -= Update;
					inventory.ItemEquipped -= OnItemEquipped;
					inventory.ItemUnequipped -= OnItemUnequipped;
				});
		}

		RecalculateColumns();
		Update();
	}

	public override void _ExitTree()
	{
		_unsubscribeInventory();
		_unsubscribeInventory = () => { };
		_inventory = null;

		base._ExitTree();
	}

	private void Update()
	{
		foreach (Node child in InventoryGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (_inventory == null)
		{
			return;
		}

		foreach (InventoryItemSlot slot in _inventory.Items)
		{
			var itemSlotPanel = InventoryItemScene.Instantiate<ItemSlotPanel>();
			itemSlotPanel.SetItem(slot, _inventory.IsEquipped(slot));
			itemSlotPanel.ItemSelected += OnItemSelected;

			InventoryGrid.AddChild(itemSlotPanel);
		}

		ApplyTargetingVisuals();
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		Update();
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		Update();
	}

	private void OnItemSelected(ItemSlotPanel itemSlotPanel)
	{
		if (_inventory == null)
		{
			return;
		}

		if (_activeTarget != null)
		{
			// The button is disabled for ineligible slots, so any click that reaches here
			// is on an eligible target.
			InventoryTargetRequest request = _activeTarget;
			EndTargeting();
			request.Confirmed?.Invoke(itemSlotPanel.Slot);
			return;
		}

		var contextMenu = GetNode<InventoryItemContextMenu>("%InventoryItemContextMenu");
		contextMenu.Initialize(_inventory, itemSlotPanel.Slot);

		var rect = itemSlotPanel.GetGlobalRect();
		contextMenu.Position =
			new Vector2I((int)rect.Position.X, (int)rect.Position.Y)
				+ new Vector2I((int)rect.Size.X, 0);

		// contextMenu.PopupExclusive(this);
		contextMenu.Popup();
	}

	private void OnTargetedUseRequested(InventoryItemSlot scrollSlot)
	{
		if (_inventory == null || scrollSlot.Item is not Scroll { Effect: not null } scroll)
		{
			return;
		}

		ScrollEffect effect = scroll.Effect;
		bool AnyTarget() => _inventory.Items.Any(slot => slot != scrollSlot && effect.IsValidTarget(_player, slot));

		if (!AnyTarget())
		{
			ShowBannerMessage("Nothing suitable — the scroll stays rolled up.");
			return;
		}

		// Reading far enough to pick a target reveals the scroll itself, even if the
		// player then cancels without picking anything.
		GameSession.Instance?.IdentifyItemType(scroll);

		BeginTargeting(new InventoryTargetRequest
		{
			Prompt = effect.TargetPrompt,
			IsEligible = slot => slot != scrollSlot && effect.IsValidTarget(_player, slot),
			Confirmed = slot =>
			{
				effect.ApplyToTarget(_player, slot);
				_inventory.Consume(scrollSlot);
			},
		});
	}

	private void BeginTargeting(InventoryTargetRequest request)
	{
		_activeTarget = request;
		if (TargetingBanner != null)
		{
			TargetingBanner.Text = $"{request.Prompt}  (Esc cancels)";
			TargetingBanner.Visible = true;
		}

		ApplyTargetingVisuals();
	}

	private void CancelTargeting()
	{
		if (_activeTarget == null)
		{
			return;
		}

		InventoryTargetRequest request = _activeTarget;
		EndTargeting();
		request.Cancelled?.Invoke();
	}

	/// <summary>Clears targeting state and its visuals, without invoking either callback.</summary>
	private void EndTargeting()
	{
		_activeTarget = null;
		if (TargetingBanner != null)
		{
			TargetingBanner.Visible = false;
		}

		ApplyTargetingVisuals();
	}

	private void ApplyTargetingVisuals()
	{
		foreach (Node child in InventoryGrid.GetChildren())
		{
			if (child is not ItemSlotPanel panel)
			{
				continue;
			}

			SlotTargetingState state = _activeTarget == null
				? SlotTargetingState.None
				: _activeTarget.IsEligible(panel.Slot) ? SlotTargetingState.Eligible : SlotTargetingState.Ineligible;
			panel.SetTargetingState(state);
		}
	}

	/// <summary>A transient banner message, e.g. when a scroll has nothing to target.</summary>
	private void ShowBannerMessage(string message)
	{
		if (TargetingBanner == null)
		{
			return;
		}

		TargetingBanner.Text = message;
		TargetingBanner.Visible = true;
		GetTree().CreateTimer(1.5).Timeout += () =>
		{
			if (_activeTarget == null && IsInstanceValid(TargetingBanner))
			{
				TargetingBanner.Visible = false;
			}
		};
	}
}
