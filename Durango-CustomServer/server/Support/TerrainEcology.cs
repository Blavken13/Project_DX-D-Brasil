using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Terrain;
using Messages;
using Shared.Region;

namespace Durango.Online;

internal static class TerrainEcology
{
    public static bool IsFishing(string collectible) => collectible?.StartsWith("harpoon_fishing_point_", StringComparison.Ordinal) == true ||
        collectible?.StartsWith("harpoon_shrimp_point_", StringComparison.Ordinal) == true;

    public static Biome BiomeAt(TerrainData terrain, Point2 tile) =>
        tile.x >= 0 && tile.y >= 0 && tile.x < terrain.Width && tile.y < terrain.Height &&
        terrain.Biomes?.Length > tile.y * terrain.Width + tile.x
            ? (Biome)terrain.Biomes[tile.y * terrain.Width + tile.x] : Biome.Invalid;

    public static bool HasDepth(TerrainData terrain, Point2 tile, Func<byte, bool> predicate)
    {
        if (tile.x < 0 || tile.y < 0 || tile.x >= terrain.Width || tile.y >= terrain.Height) return false;
        int stride = terrain.Width + 1, index = tile.y * stride + tile.x;
        var ocean = terrain.Ocean;
        return ocean?.Length > index + stride + 1 &&
            (predicate(ocean[index]) || predicate(ocean[index + 1]) || predicate(ocean[index + stride]) || predicate(ocean[index + stride + 1]));
    }

    public static bool IsLand(TerrainData terrain, Point2 tile)
    {
        var biome = BiomeAt(terrain, tile);
        return biome is >= Biome.TemperateForest and <= Biome.SandBeach &&
            !HasDepth(terrain, tile, depth => depth != 0) && !HasRiver(terrain, tile);
    }

    private static bool HasRiver(TerrainData terrain, Point2 tile)
    {
        int stride = terrain.Width + 1, index = tile.y * stride + tile.x;
        var rivers = terrain.Rivers;
        if (rivers?.Length <= (index + stride + 1) * 3 + 2) return false;
        return rivers != null && new[] { index, index + 1, index + stride, index + stride + 1 }
            .Any(i => rivers[i * 3 + 2] >= 5); // RiverData nativo: terceiro byte é a profundidade.
    }

    public static IEnumerable<Point2> LandGrid(TerrainData terrain, int spacing, int margin = 12)
    {
        for (int y = margin; y < terrain.Height - margin; y += spacing)
        for (int x = margin; x < terrain.Width - margin; x += spacing)
        {
            var tile = new Point2(x, y);
            if (IsLand(terrain, tile)) yield return tile;
        }
    }

    public static void CompletePois(TerrainData terrain)
    {
        var role = RegionCatalog.GetTemplate(terrain.Info?.region_template)?.Role;
        terrain.Pois ??= new TerrainPois();
        var pois = terrain.Pois;
        // O garden também contém POIs nativos: não são recursos coletáveis.
        foreach (var natural in NaturalInfo.FromBytes(terrain.Garden ?? Array.Empty<byte>()))
        {
            var target = natural.EntityType switch
            {
                15001 => pois.Warpholes,
                15002 or 15004 => pois.Craters,
                15006 => pois.Rifts,
                _ => null
            };
            var tile = new Point2(natural.X, natural.Y);
            if (target != null && !target.Any(p => p.x == tile.x && p.y == tile.y)) target.Add(tile);
        }
        if (role == null || role is Role.Tutorial or Role.Personal) return;
        var occupied = pois.PortPoints.Concat(pois.Warpholes).Concat(pois.Craters).Concat(pois.Rifts).ToList();
        var landmarks = terrain.Landmarks == null ? Array.Empty<LandmarkInfo>() : LandmarkInfo.FromBytes(terrain.Landmarks);
        foreach (var tile in LandGrid(terrain, 20))
        {
            if (pois.Warpholes.Count >= 2 && pois.Craters.Count >= 1) break;
            if (occupied.Any(p => Math.Abs(p.x - tile.x) < 16 && Math.Abs(p.y - tile.y) < 16) ||
                landmarks.Any(p => Math.Abs(p.X - tile.x) < 6 && Math.Abs(p.Y - tile.y) < 6)) continue;
            if (pois.Warpholes.Count < 2) pois.Warpholes.Add(tile);
            else pois.Craters.Add(tile);
            occupied.Add(tile);
        }
    }
}
