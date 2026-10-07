using System;
using System.Collections.Generic;
using System.Linq;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private float AnimalDiscoveryRate(string templateId)
    {
        var animals = BuildDiscoveryInfo(templateId).AnimalTypes;
        return animals.Length == 0 ? 0 : (float)animals.Count(a => a.Item2) / animals.Length;
    }

    private DefoggedChunks ExploredMapChunks(string region, int width, int height)
    {
        if (_context.ExploredChunks?.TryGetValue(region, out var found) != true)
            return new DefoggedChunks { Chunks = Array.Empty<Point2>() };
        return new DefoggedChunks { Chunks = found.Select(p => new Point2(p >> 16, p & 65535))
            .Where(p => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height)
            .OrderBy(p => p.y).ThenBy(p => p.x).ToArray() };
    }

    private bool RevealPlayerSurroundings()
    {
        var position = PlayerPosition();
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || position.x < 0 || position.y < 0 ||
            position.x >= _world.NumTilesX * 200 || position.y >= _world.NumTilesY * 200) return false;
        int cx = (int)(position.x / 3200), cy = (int)(position.y / 3200);
        var maps = _context.ExploredChunks ??= new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        string region = LogicalRegionId();
        if (!maps.TryGetValue(region, out var found)) maps[region] = found = new HashSet<int>();
        bool changed = false;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(_world.NumChunksY - 1, cy + 1); y++)
        for (int x = Math.Max(0, cx - 1); x <= Math.Min(_world.NumChunksX - 1, cx + 1); x++)
            changed |= found.Add((x << 16) | y);
        if (changed) OnContextChanged();
        return changed;
    }
}
