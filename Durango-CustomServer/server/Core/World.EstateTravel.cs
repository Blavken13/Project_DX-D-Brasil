using System;
using System.Linq;
using Messages;

namespace Durango.Online;

public partial class World
{
    internal bool TryEstateArrivalTile(string estateId, out Point2 tile)
    {
        tile = default;
        EstateRecord estate = GetEstate(estateId);
        if (estate == null) return false;
        bool Available(Point2 point) => TerrainEcology.IsLand(_terrainData, point) &&
            TryGetEstateIdAtCell(CellFromTile(point), out string ownerEstate) && ownerEstate == estateId;
        bool Unoccupied(Point2 point) => !ArtifactManager.Enumerable(a =>
            point.x >= a.Tile.x && point.x < a.Tile.x + Math.Max(1, a.Size.x) &&
            point.y >= a.Tile.y && point.y < a.Tile.y + Math.Max(1, a.Size.y)).Any();

        // The original anchor can be outside the estate after shrinking it.
        var anchor = new Point2(estate.TileX, estate.TileY);
        if (Available(anchor) && Unoccupied(anchor)) { tile = anchor; return true; }
        Point2? fallback = Available(anchor) ? anchor : null;
        foreach (string key in estate.Cells ?? Enumerable.Empty<string>())
        {
            string[] parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int cx) || !int.TryParse(parts[1], out int cy)) continue;
            Point2 origin = TileFromCell(new Point2(cx, cy));
            for (int y = 0; y < EstateGridSize; y++)
            for (int x = 0; x < EstateGridSize; x++)
            {
                var candidate = new Point2(origin.x + x, origin.y + y);
                if (!Available(candidate)) continue;
                fallback ??= candidate;
                if (Unoccupied(candidate)) { tile = candidate; return true; }
            }
        }
        if (!fallback.HasValue) return false;
        tile = fallback.Value;
        return true;
    }
}
