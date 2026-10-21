using Godot;
using System.Linq;

/// <summary>
/// Optional authored boss anchor. Place inside a room; its negative Z axis is the boss's facing.
/// The room designer owns clearance here; generated fallback placement still uses the fit profile.
/// </summary>
[Tool, GlobalClass]
public partial class GatekeeperSpawnMarker : Marker3D
{
	public static GatekeeperSpawnMarker Find(Level level) => level?.FindChildren("*", "", true, false)
		.OfType<GatekeeperSpawnMarker>().OrderBy(marker => marker.GetPath().ToString(), System.StringComparer.Ordinal).FirstOrDefault();
}