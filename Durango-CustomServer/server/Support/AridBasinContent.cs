using System;

namespace Durango.Online;

internal static class AridBasinContent
{
    public const string TerrainId = "arid_basin_60_v2";
    public const string MeshPrefab = "Models/Maps/AridBasin/AridBasinTerrain.prefab";
    public const ushort MeshLandmarkId = 60000;

    public static bool IsTerrain(string id) =>
        string.Equals(id, TerrainId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, "arid_basin_60", StringComparison.OrdinalIgnoreCase);
}
