using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Pure-C# result of joining a child cluster map onto an accumulated parent cluster map across a sealed 1-tile bridge.</summary>
public readonly record struct ClusterJoinResult(
	MapData CombinedMap,
	Vector2I ParentShift,
	Vector2I ChildShift,
	Vector2I BridgeTile,
	Vector2I BridgePerpendicular,
	int ChildTurns);

public static partial class RoomAssembler
{
	/// <summary>
	/// Assembles a leaf <see cref="RoomType"/> or composite <see cref="RoomGroup"/> tree into a single atomic
	/// <see cref="RoomCluster"/> connected across sealed 1-tile bridges.
	/// </summary>
	public static RoomCluster AssembleCluster(RoomDefinition definition, ulong seed, RoomAssemblyOptions options = null, RoomPool pool = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		options ??= new RoomAssemblyOptions();
		if (definition is RoomType roomType)
		{
			var blueprint = Build(roomType, seed, options);
			return new RoomCluster(
				blueprint.Room.Map,
				[new ClusterMember(blueprint.Room, Vector2I.Zero, "", false, false)],
				roomType);
		}
		if (definition is not RoomGroup group)
		{
			throw new InvalidOperationException($"Unsupported RoomDefinition type {definition.GetType().Name}.");
		}

		var errors = group.Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));

		int count = group.Members.Length;
		var linkCount = new int[count];
		for (int i = 1; i < count; i++)
		{
			linkCount[i]++;
			linkCount[group.Members[i].AttachToIndex]++;
		}

		var memberSpecs = new (List<RoomDefinition> Candidates, int? MinDoorways, int? MaxDoorways)[count];
		var failedDefs = new HashSet<RoomDefinition>[count];
		for (int i = 0; i < count; i++)
		{
			var member = group.Members[i];
			int links = linkCount[i];
			// External doorway bounds apply only to external cluster connections; add internal bridge links per member.
			int? minDoorways = !member.AllowExternalDoorways
				? Math.Max(1, links)
				: options.MinDoorways.HasValue
					? options.MinDoorways.Value + links
					: links > 0 ? links + 1 : null;
			int? maxDoorways = !member.AllowExternalDoorways
				? Math.Max(1, links)
				: options.MaxDoorways.HasValue
					? options.MaxDoorways.Value + links
					: null;
			var candidates = CandidateDefinitions(member, pool, options.Realm, minDoorways, maxDoorways);
			if (candidates.Count == 0)
			{
				throw new InvalidOperationException($"RoomGroup {group.Id} seed {seed}: member {i} ({member.SlotId}) has no matching definitions.");
			}
			memberSpecs[i] = (candidates, minDoorways, maxDoorways);
			failedDefs[i] = [];
		}

		string lastFailure = "";
		for (int attempt = 0; attempt < group.Attempts; attempt++)
		{
			var assembled = new List<RoomCluster>(count);
			var accumMembers = new List<ClusterMember>();
			var memberRanges = new List<(int Start, int Count)>(count);
			MapData accumMap = null;
			bool succeeded = false;

			try
			{
				using var pickRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{group.Id}:pick:{attempt}");
				using var joinRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{group.Id}:join:{attempt}");

				int i = 0;
				for (; i < count; i++)
				{
					var member = group.Members[i];
					var (candidates, minDoorways, maxDoorways) = memberSpecs[i];
					var unfailed = candidates.Where(def => !failedDefs[i].Contains(def)).ToList();
					if (unfailed.Count == 0)
					{
						failedDefs[i].Clear();
						unfailed = candidates;
					}

					var selected = Weighted.Pick(unfailed, def => def.Weight, pickRng);
					bool applyFit = options.RequiredFit != null && (member.SlotId == "boss" || (count == 1 && i == 0));
					var memberOptions = new RoomAssemblyOptions
					{
						Realm = options.Realm,
						Furnish = options.Furnish,
						AllowSecretDoors = member.AllowSecretDoors ? options.AllowSecretDoors : false,
						DoorChance = member.DoorChanceOverride >= 0f ? member.DoorChanceOverride : options.DoorChance,
						SecretDoorChance = options.SecretDoorChance,
						SecretDetection = options.SecretDetection,
						MinDoorways = minDoorways,
						MaxDoorways = maxDoorways,
						RequiredFit = applyFit ? options.RequiredFit : null,
					};

					RoomCluster child;
					try
					{
						ulong memberSeed = GenerationRandom.DeriveSeed(seed, $"{group.Id}:member:{attempt}", i.ToString());
						child = AssembleCluster(selected, memberSeed, memberOptions, pool);
					}
					catch (InvalidOperationException ex)
					{
						failedDefs[i].Add(selected);
						lastFailure = ex.Message;
						break;
					}

					assembled.Add(child);
					if (i == 0)
					{
						var tagged = TagMembers(child.Members, member);
						accumMap = child.Map;
						accumMembers.AddRange(tagged);
						memberRanges.Add((0, tagged.Count));
						continue;
					}

					var parentTiles = new HashSet<Vector2I>();
					var (pStart, pCount) = memberRanges[member.AttachToIndex];
					for (int p = pStart; p < pStart + pCount; p++)
					{
						var pm = accumMembers[p];
						for (int lx = 0; lx < pm.Room.Map.Width; lx++)
						{
							for (int lz = 0; lz < pm.Room.Map.Height; lz++)
							{
								if (!pm.Room.Map.IsEmpty(lx, lz))
								{
									parentTiles.Add(pm.Offset + new Vector2I(lx, lz));
								}
							}
						}
					}

					if (!TryJoinClusterMaps(accumMap, child.Map, parentTiles, n => joinRng.RandiRange(0, n - 1), out var join))
					{
						lastFailure = $"cannot join member {i} onto member {member.AttachToIndex}";
						break;
					}

					child.Rotate(join.ChildTurns);
					accumMap = join.CombinedMap;
					for (int k = 0; k < accumMembers.Count; k++)
					{
						accumMembers[k] = accumMembers[k] with { Offset = accumMembers[k].Offset + join.ParentShift };
					}

					int childStart = accumMembers.Count;
					var rotatedTagged = TagMembers(child.Members, member);
					foreach (var rm in rotatedTagged)
					{
						accumMembers.Add(rm with { Offset = rm.Offset + join.ChildShift });
					}
					memberRanges.Add((childStart, rotatedTagged.Count));
				}

				if (i == count)
				{
					foreach (var c in assembled) c.DetachRooms();
					succeeded = true;
					return new RoomCluster(accumMap, accumMembers, group);
				}
			}
			finally
			{
				if (!succeeded)
				{
					foreach (var c in assembled) c.Free();
				}
			}
		}

		throw new InvalidOperationException($"RoomGroup {group.Id} seed {seed}: failed after {group.Attempts} attempts; last: {lastFailure}.");
	}

	private static List<ClusterMember> TagMembers(IReadOnlyList<ClusterMember> members, RoomGroupMember spec)
	{
		var tagged = new List<ClusterMember>(members.Count);
		foreach (var m in members)
		{
			string slotId = string.IsNullOrEmpty(m.SlotId) ? spec.SlotId : m.SlotId;
			tagged.Add(m with
			{
				SlotId = slotId,
				MustTraverse = m.MustTraverse || spec.MustTraverse,
				ReserveAccess = m.ReserveAccess || spec.ReserveAccess,
			});
		}
		return tagged;
	}

	private static List<RoomDefinition> CandidateDefinitions(
		RoomGroupMember member,
		RoomPool pool,
		string realm,
		int? minDoorways,
		int? maxDoorways)
	{
		RoomDefinition[] raw;
		if (member.Options != null && member.Options.Length > 0)
		{
			raw = member.Options;
		}
		else if (pool != null)
		{
			raw = member.PoolSlot switch
			{
				RoomPoolSlot.Entrance => pool.Entrances,
				RoomPoolSlot.Exit => pool.Exits,
				RoomPoolSlot.Gauntlet => pool.Gauntlets,
				_ => pool.Rooms,
			};
		}
		else
		{
			raw = [];
		}

		return raw.Where(def =>
		{
			if (def == null || !def.SupportsRealm(realm)) return false;
			if (def is RoomType rt)
			{
				if (maxDoorways.HasValue && rt.MinDoorways > maxDoorways.Value) return false;
				if (minDoorways.HasValue && rt.MaxDoorways < minDoorways.Value) return false;
			}
			return true;
		}).ToList();
	}

	/// <summary>
	/// Pure-C# bridge join: searches every pairing of an allowed external connector on <paramref name="parentMap"/>
	/// with all 4 rotations of <paramref name="childMap"/> across a 1-tile <see cref="MapTile.Corridor"/> bridge
	/// with sealed perpendicular walls, preserving a 1-tile room buffer and unlinked doorway isolation.
	/// </summary>
	internal static bool TryJoinClusterMaps(
		MapData parentMap,
		MapData childMap,
		IReadOnlySet<Vector2I> parentAnchorTiles,
		Func<int, int> pickIndex,
		out ClusterJoinResult result)
	{
		var allParentExt = RoomCluster.GetExternalConnectors(parentMap);
		var parentExt = parentAnchorTiles == null
			? allParentExt
			: allParentExt.Where(c => parentAnchorTiles.Contains(c.Tile)).ToList();

		var childMaps = new MapData[4];
		var childExts = new List<(Vector2I Tile, Vector2I Direction)>[4];
		childMaps[0] = childMap;
		childExts[0] = RoomCluster.GetExternalConnectors(childMap);
		for (int r = 1; r < 4; r++)
		{
			childMaps[r] = childMaps[r - 1].Turned();
			childExts[r] = RoomCluster.GetExternalConnectors(childMaps[r]);
		}

		var candidates = new List<(Vector2I pConn, Vector2I pDir, int Rot, Vector2I ChildOffset, Vector2I Bridge, int MinX, int MinY, int MaxX, int MaxY)>();
		int bestArea = int.MaxValue;

		foreach (var (pConn, pDir) in parentExt)
		{
			var bridge = pConn + pDir;
			if (HasTileWithin(parentMap, Vector2I.Zero, bridge, buffer: 0)) continue;
			for (int r = 0; r < 4; r++)
			{
				var rotatedChild = childMaps[r];
				foreach (var (cConn, cDir) in childExts[r])
				{
					if (cDir != -pDir) continue;

					var childOffset = pConn + pDir * 2 - cConn;
					if (HasTileWithin(rotatedChild, childOffset, bridge, buffer: 0)) continue;
					if (AreasHaveBufferOverlap(parentMap, rotatedChild, childOffset, buffer: 1)) continue;

					bool blocksDoorway = false;
					foreach (var (uTile, uDir) in allParentExt)
					{
						if (uTile == pConn && uDir == pDir) continue;
						var exitTile = uTile + uDir;
						if (exitTile == bridge || HasTileWithin(rotatedChild, childOffset, exitTile, buffer: 1))
						{
							blocksDoorway = true;
							break;
						}
					}
					if (blocksDoorway) continue;

					foreach (var (uTile, uDir) in childExts[r])
					{
						if (uTile == cConn && uDir == cDir) continue;
						var exitTile = uTile + childOffset + uDir;
						if (exitTile == bridge || HasTileWithin(parentMap, Vector2I.Zero, exitTile, buffer: 1))
						{
							blocksDoorway = true;
							break;
						}
					}
					if (blocksDoorway) continue;

					int minX = Math.Min(0, Math.Min(childOffset.X, bridge.X));
					int minY = Math.Min(0, Math.Min(childOffset.Y, bridge.Y));
					int maxX = Math.Max(parentMap.Width - 1, Math.Max(childOffset.X + rotatedChild.Width - 1, bridge.X));
					int maxY = Math.Max(parentMap.Height - 1, Math.Max(childOffset.Y + rotatedChild.Height - 1, bridge.Y));
					int area = (maxX - minX + 1) * (maxY - minY + 1);

					if (area < bestArea)
					{
						bestArea = area;
						candidates.Clear();
					}
					if (area == bestArea)
					{
						candidates.Add((pConn, pDir, r, childOffset, bridge, minX, minY, maxX, maxY));
					}
				}
			}
		}

		if (candidates.Count == 0)
		{
			result = default;
			return false;
		}

		var pick = candidates[pickIndex(candidates.Count)];
		var combined = new MapData(pick.MaxX - pick.MinX + 1, pick.MaxY - pick.MinY + 1);
		var parentShift = new Vector2I(-pick.MinX, -pick.MinY);
		var childShift = pick.ChildOffset + parentShift;
		var worldBridge = pick.Bridge + parentShift;

		combined.Stamp(parentMap, parentShift);
		combined.Stamp(childMaps[pick.Rot], childShift);
		combined.SetTile(worldBridge.X, worldBridge.Y, MapTile.Corridor);
		var perp = new Vector2I(-pick.pDir.Y, pick.pDir.X);
		combined.SealCorridorEdge(worldBridge, perp);
		combined.SealCorridorEdge(worldBridge, -perp);

		result = new ClusterJoinResult(combined, parentShift, childShift, worldBridge, perp, pick.Rot);
		return true;
	}

	private static bool AreasHaveBufferOverlap(MapData a, MapData b, Vector2I bOffset, int buffer)
	{
		for (int bx = 0; bx < b.Width; bx++)
		{
			for (int by = 0; by < b.Height; by++)
			{
				if (b.IsEmpty(bx, by)) continue;
				if (HasTileWithin(a, Vector2I.Zero, new Vector2I(bOffset.X + bx, bOffset.Y + by), buffer))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool HasTileWithin(MapData map, Vector2I offset, Vector2I point, int buffer)
	{
		int lx = point.X - offset.X;
		int ly = point.Y - offset.Y;
		int minX = Math.Max(0, lx - buffer);
		int maxX = Math.Min(map.Width - 1, lx + buffer);
		int minY = Math.Max(0, ly - buffer);
		int maxY = Math.Min(map.Height - 1, ly + buffer);
		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				if (!map.IsEmpty(x, y)) return true;
			}
		}
		return false;
	}
}