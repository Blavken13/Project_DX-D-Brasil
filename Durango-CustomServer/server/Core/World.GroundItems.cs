using System.Collections.Generic;
using Messages;

namespace Durango.Online;

public partial class World
{
    internal List<Item> GroundItems(string id) => id != null && _context.GroundPackages.TryGetValue(id, out var items) ? items : null;

    internal bool DropItems(Point2 tile, int? floor, List<Item> items)
    {
        if (tile.x < 0 || tile.y < 0 || tile.x >= NumTilesX || tile.y >= NumTilesY
            || ArtifactManager.FindOverlapping(tile, new Point2(1, 1), floor) != null) return false;
        var package = Cheats.MakeAppearArtifact(new[] { "prop", "8000", $"position:{tile.x},{tile.y}" }, out var addons);
        if (!package.HasValue) return false;
        var artifact = package.Value;
        artifact.Floor = floor;
        _context.GroundPackages[package.Value.EntityId] = new List<Item>(items);
        ConstructArtifact(artifact, addons);
        return true;
    }
}
