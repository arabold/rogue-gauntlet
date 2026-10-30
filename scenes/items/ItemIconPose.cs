using Godot;

/// <summary>
/// Optional marker in an item model scene that fixes how the inventory <see cref="Preview"/> shows the model, for the
/// few the automatic pose (<see cref="ItemIconFraming.Auto"/>) gets wrong. Place it like a camera: its -Z looks at the
/// item and its +Y is up on screen. Only its rotation counts; the preview centers and sizes the model itself. To author
/// it, orbit the editor viewport to the wanted view, select the marker and use View > Align Transform with View.
/// </summary>
[GlobalClass]
public partial class ItemIconPose : Marker3D
{
}
