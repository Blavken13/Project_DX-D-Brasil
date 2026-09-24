using System;
using System.Collections.Generic;
using Algorithms;
using Durango.Utils;
using Shared.Region;
using UnityEngine;

namespace Durango.Terrain;

public static class NavGrid
{
	public const byte CostBase = 10;

	public const byte CostWater = 30;

	public const byte CostNearWater = 20;

	public const byte CostNearCliff = 40;

	public const byte CostNearStructure = 0;

	public const byte CostNatural = 60;

	public const byte CostNearNatural = 20;

	public const byte CostOcean = 200;

	public const byte ClearCostMax = 30;

	private const int DynamicPadding = 12;

	private static string _terrainId;

	private static byte[,] _static;

	private static byte[,] _work;

	private static PathFinderFast _finder;

	private static bool _loading;

	private static int _size;

	private static readonly HashSet<int> _tempBlocked = new HashSet<int>();

	private const int MaxLegTiles = 14;

	private const float ProbeRadiusCore = 22f;

	private const float ProbeRadiusEdge = 70f;

	private static readonly Collider[] _probeHits = new Collider[16];

	public static string LastError { get; private set; }

	public static List<Vector2> LastPathTiles { get; private set; }

	public static Vector3? LastAdjustedTarget { get; private set; }

	public static bool Ready
	{
		get
		{
			if (_static != null)
			{
				return _terrainId == CurrentTerrainId();
			}
			return false;
		}
	}

	public static int Size => _size;

	private static string CurrentTerrainId()
	{
		if (GameManager.Region == null)
		{
			return null;
		}
		return GameManager.Region.TerrainId;
	}

	public static void EnsureLoaded()
	{
		if (Ready || _loading)
		{
			return;
		}
		string text = CurrentTerrainId();
		if (!string.IsNullOrEmpty(text))
		{
			_loading = true;
			string requested = text;
			MapSystem.RequestBiomes(requested, delegate(byte[] bytes)
			{
				_loading = false;
				Build(requested, bytes);
			}, delegate
			{
				_loading = false;
				LastError = "โหลด whole_biomes ของ " + requested + " ไม\u0e48ได\u0e49";
			});
		}
	}

	public static void Reset()
	{
		_static = null;
		_work = null;
		_finder = null;
		_terrainId = null;
		_tempBlocked.Clear();
	}

	private static void Build(string terrainId, byte[] biomes)
	{
		if (biomes == null)
		{
			LastError = "whole_biomes ว\u0e48าง";
			return;
		}
		int num = (int)Math.Round(Math.Sqrt(biomes.Length));
		if (num * num != biomes.Length || num < 16 || (num & (num - 1)) != 0)
		{
			LastError = "ขนาด whole_biomes ผ\u0e34ด: " + biomes.Length;
			return;
		}
		byte[,] array = new byte[num, num];
		for (int i = 0; i < num; i++)
		{
			for (int j = 0; j < num; j++)
			{
				byte raw = biomes[j + i * num];
				array[j, i] = BaseCost(raw);
			}
		}
		byte[,] array2 = new byte[num, num];
		for (int k = 0; k < num; k++)
		{
			for (int l = 0; l < num; l++)
			{
				byte b = array[l, k];
				if (b == 0)
				{
					array2[l, k] = 0;
					continue;
				}
				int num2 = 0;
				for (int m = -1; m <= 1; m++)
				{
					for (int n = -1; n <= 1; n++)
					{
						if (n == 0 && m == 0)
						{
							continue;
						}
						int num3 = l + n;
						int num4 = k + m;
						if (num3 < 0 || num4 < 0 || num3 >= num || num4 >= num)
						{
							continue;
						}
						byte maskedBiome = biomes[num3 + num4 * num];
						if (Util.IsCollidableMasked(maskedBiome))
						{
							num2 = Math.Max(num2, 40);
							continue;
						}
						Biome unmaskedBiome = Util.GetUnmaskedBiome(maskedBiome);
						if (unmaskedBiome == Biome.River || unmaskedBiome == Biome.Lake)
						{
							num2 = Math.Max(num2, 20);
						}
					}
				}
				array2[l, k] = (byte)Math.Min(255, b + num2);
			}
		}
		_static = array2;
		_work = new byte[num, num];
		_size = num;
		_terrainId = terrainId;
		_finder = new PathFinderFast(_work);
		_finder.Formula = HeuristicFormula.Manhattan;
		_finder.Diagonals = true;
		_finder.HeavyDiagonals = true;
		_finder.HeuristicEstimate = 10;
		_finder.SearchLimit = 40000;
		_finder.PunishChangeDirection = false;
		_finder.ReopenCloseNodes = false;
		_finder.TieBreaker = true;
		_tempBlocked.Clear();
		LastError = null;
	}

	private static byte BaseCost(byte raw)
	{
		if (raw == byte.MaxValue)
		{
			return 0;
		}
		if (Util.IsCollidableMasked(raw))
		{
			return 0;
		}
		switch (Util.GetUnmaskedBiome(raw))
		{
		case Biome.Invalid:
		case Biome.Lava:
			return 0;
		case Biome.ColdOcean:
		case Biome.WarmOcean:
			return 200;
		case Biome.River:
		case Biome.Lake:
			return 30;
		default:
			return 10;
		}
	}

	private static int Key(int x, int y)
	{
		return (y << 16) | x;
	}

	public static void MarkTempBlocked(Vector3 clientPos)
	{
		Vector2 vector = Util.ClientPositionToTilePosition(clientPos);
		_tempBlocked.Add(Key((int)vector.x, (int)vector.y));
	}

	public static void ClearTempBlocked()
	{
		_tempBlocked.Clear();
	}

	public static int CostAt(int x, int y)
	{
		if (!Ready || x < 0 || y < 0 || x >= _size || y >= _size)
		{
			return -1;
		}
		PrepareWork(x, y, x, y);
		return _work[x, y];
	}

	private static void PrepareWork(int x0, int y0, int x1, int y1)
	{
		Buffer.BlockCopy(_static, 0, _work, 0, _static.Length);
		TerrainBase terrainBase = (Singleton<TerrainBase>.HasInstance() ? Singleton<TerrainBase>.Instance() : null);
		if (terrainBase == null)
		{
			return;
		}
		int num = Mathf.Clamp(Mathf.Min(x0, x1) - 12, 0, _size - 1);
		int num2 = Mathf.Clamp(Mathf.Max(x0, x1) + 12, 0, _size - 1);
		int num3 = Mathf.Clamp(Mathf.Min(y0, y1) - 12, 0, _size - 1);
		int num4 = Mathf.Clamp(Mathf.Max(y0, y1) + 12, 0, _size - 1);
		float referenceY = ((PlayerBehavior.LocalPlayer != null) ? PlayerBehavior.LocalPlayer.CurrentPosition.y : 0f);
		for (int i = num3; i <= num4; i++)
		{
			for (int j = num; j <= num2; j++)
			{
				if (_work[j, i] == 0)
				{
					continue;
				}
				TileObject tileObject = terrainBase.GetTileObject(new Point2(j, i), warning: false);
				if (tileObject == null)
				{
					continue;
				}
				switch (ProbeTile(j, i, referenceY))
				{
				case 2:
					Stamp(j, i, 0, (byte)((!(tileObject.Artifact != null)) ? 20u : 0u));
					continue;
				case 1:
					Stamp(j, i, 60, 20);
					continue;
				}
				if (tileObject.Artifact != null)
				{
					Stamp(j, i, 0, 0);
				}
			}
		}
		foreach (int item in _tempBlocked)
		{
			int num5 = item & 0xFFFF;
			int num6 = item >> 16;
			if (num5 >= 0 && num6 >= 0 && num5 < _size && num6 < _size)
			{
				_work[num5, num6] = 0;
			}
		}
	}

	private static int ProbeTile(int x, int y, float referenceY)
	{
		Vector3 vector = Util.TilePositionToClientPosition(new Vector2(x, y), tileCenter: true);
		float? worldHeight = LocalMoveOperator.GetWorldHeight(new Vector3(vector.x, referenceY, vector.z), 0, 0f);
		float num = (worldHeight.HasValue ? worldHeight.Value : referenceY);
		Vector3 p = new Vector3(vector.x, num + 25f, vector.z);
		Vector3 p2 = new Vector3(vector.x, num + 160f, vector.z);
		if (HasBlockingCollider(p, p2, 22f))
		{
			return 2;
		}
		return HasBlockingCollider(p, p2, 70f) ? 1 : 0;
	}

	private static bool HasBlockingCollider(Vector3 p0, Vector3 p1, float radius)
	{
		int num = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, _probeHits, LayerHelper.PropMask, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < num; i++)
		{
			Collider collider = _probeHits[i];
			_probeHits[i] = null;
			if (collider != null && !collider.CompareTag("Steppable"))
			{
				return true;
			}
		}
		return false;
	}

	private static void Stamp(int x, int y, byte center, byte around)
	{
		if (_work[x, y] != 0)
		{
			_work[x, y] = (byte)((center != 0) ? ((byte)Math.Min(255, Math.Max((int)_work[x, y], (int)center))) : 0);
		}
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				if (j != 0 || i != 0)
				{
					int num = x + j;
					int num2 = y + i;
					if (num >= 0 && num2 >= 0 && num < _size && num2 < _size && _work[num, num2] != 0)
					{
						_work[num, num2] = (byte)Math.Min(255, Math.Max(_work[num, num2], 10 + around));
					}
				}
			}
		}
	}

	public static List<Vector2> FindPathTiles(Vector3 fromClient, Vector3 toClient)
	{
		if (!Ready)
		{
			return null;
		}
		Vector2 vector = Util.ClientPositionToTilePosition(fromClient);
		Vector2 vector2 = Util.ClientPositionToTilePosition(toClient);
		int num = Mathf.Clamp((int)vector.x, 0, _size - 1);
		int num2 = Mathf.Clamp((int)vector.y, 0, _size - 1);
		int num3 = Mathf.Clamp((int)vector2.x, 0, _size - 1);
		int num4 = Mathf.Clamp((int)vector2.y, 0, _size - 1);
		PrepareWork(num, num2, num3, num4);
		LastAdjustedTarget = null;
		if (_work[num, num2] == 0)
		{
			_work[num, num2] = 10;
		}
		if (_work[num3, num4] == 0)
		{
			if (NearestWalkable(num3, num4, 4, out var outX, out var outY))
			{
				num3 = outX;
				num4 = outY;
				LastAdjustedTarget = Util.TilePositionToClientPosition(new Vector2(num3, num4), tileCenter: true);
			}
			else
			{
				_work[num3, num4] = 10;
			}
		}
		List<PathFinderNode> list = _finder.FindPath(new Point(num, num2), new Point(num3, num4));
		if (list == null || list.Count == 0)
		{
			LastPathTiles = null;
			return null;
		}
		List<Vector2> list2 = new List<Vector2>(list.Count);
		for (int num5 = list.Count - 1; num5 >= 0; num5--)
		{
			list2.Add(new Vector2(list[num5].X, list[num5].Y));
		}
		LastPathTiles = list2;
		return list2;
	}

	public static List<Vector3> FindWaypoints(Vector3 fromClient, Vector3 toClient)
	{
		List<Vector2> list = FindPathTiles(fromClient, toClient);
		if (list == null)
		{
			return null;
		}
		List<Vector3> list2 = new List<Vector3>();
		if (list.Count <= 2)
		{
			return list2;
		}
		int num = 0;
		int num2 = list.Count - 1;
		while (num < num2)
		{
			int num3 = num + 1;
			for (int num4 = Math.Min(num2, num + 14); num4 > num + 1; num4--)
			{
				if (LineClear(list[num], list[num4]))
				{
					num3 = num4;
					break;
				}
			}
			if (num3 == num2)
			{
				break;
			}
			list2.Add(Util.TilePositionToClientPosition(list[num3], tileCenter: true));
			num = num3;
		}
		return list2;
	}

	private static bool NearestWalkable(int x, int y, int radius, out int outX, out int outY)
	{
		for (int i = 1; i <= radius; i++)
		{
			for (int j = -i; j <= i; j++)
			{
				for (int k = -i; k <= i; k++)
				{
					if (Math.Max(Math.Abs(k), Math.Abs(j)) == i)
					{
						int num = x + k;
						int num2 = y + j;
						if (num >= 0 && num2 >= 0 && num < _size && num2 < _size && _work[num, num2] != 0 && _work[num, num2] <= 60)
						{
							outX = num;
							outY = num2;
							return true;
						}
					}
				}
			}
		}
		outX = x;
		outY = y;
		return false;
	}

	private static bool LineClear(Vector2 from, Vector2 to)
	{
		int num = (int)from.x;
		int num2 = (int)from.y;
		int num3 = (int)to.x;
		int num4 = (int)to.y;
		int num5 = Math.Abs(num3 - num);
		int num6 = -Math.Abs(num4 - num2);
		int num7 = ((num < num3) ? 1 : (-1));
		int num8 = ((num2 < num4) ? 1 : (-1));
		int num9 = num5 + num6;
		while (true)
		{
			byte b = _work[num, num2];
			if (b == 0 || b > 30)
			{
				return false;
			}
			if (num == num3 && num2 == num4)
			{
				break;
			}
			int num10 = 2 * num9;
			if (num10 >= num6)
			{
				num9 += num6;
				num += num7;
			}
			if (num10 <= num5)
			{
				num9 += num5;
				num2 += num8;
			}
		}
		return true;
	}
}
