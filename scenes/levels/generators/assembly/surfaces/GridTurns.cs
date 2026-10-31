/// <summary>GridMap cell orientations for upright quarter turns about +Y.</summary>
public static class GridTurns
{
	public const int Turn0 = 0, Turn90 = 16, Turn180 = 10, Turn270 = 22;

	/// <summary>The four upright orientations, indexed by quarter turns.</summary>
	public static readonly int[] Upright = [Turn0, Turn90, Turn180, Turn270];
}
