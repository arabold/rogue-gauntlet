using Godot;

/// <summary>
/// The clickable button inside an <see cref="ItemSlotPanel"/>. Beyond a normal button it
/// renders a rich hover tooltip — the shared <see cref="ItemDetailsView"/> — so hover shows
/// the same quality-colored name and stat lines as the right-click context menu. Falls back
/// to the default text tooltip when no item is set.
/// </summary>
[GlobalClass]
public partial class ItemSlotButton : Button
{
	public Item Item { get; set; }

	public override Control _MakeCustomTooltip(string forText)
	{
		if (Item == null)
		{
			return null;
		}

		var view = new ItemDetailsView();
		view.SetItem(Item);
		return view;
	}
}
