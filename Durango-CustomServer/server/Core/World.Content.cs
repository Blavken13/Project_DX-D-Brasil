using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Terrain;
using Messages;
using Shared.Region;
using Yaml;

namespace Durango.Online;

public partial class World
{
    private readonly HashSet<string> _reservedCraters = new(StringComparer.Ordinal);
    public bool ReserveCrater(string id) => id != null && _reservedCraters.Add(id);
    public void ReleaseCrater(string id) => _reservedCraters.Remove(id);

    public bool ActivateCrater(string id, double now)
    {
        ProcessCraterStates(now);
        if (ArtifactManager.Get(id) is not { } crater || crater.States.Crack is not { } crack ||
            crack.ActivatedUntil > now) return false;
        // As tabelas de biocoms não estão no pacote. Usar os recursos nativos
        // do próprio mapa, preservando terreno, spots e construções existentes.
        var types = NaturalInfo.FromBytes(_terrainData.Garden ?? Array.Empty<byte>()).Select(n => n.EntityType)
            .Concat(RegionCatalog.GetTemplate(_terrainData.Info?.region_template)?.CollectibleLevels.Keys ?? Enumerable.Empty<ushort>())
            .Distinct().Where(t => t is >= 10000 and < 20000 && t is not (15001 or 15002 or 15004 or 15005 or 15006) &&
                !IsFishingNatural(t) && CollectibleTable.GeneratorCount(t) > 0)
            .OrderBy(t => t).ToArray();
        if (types.Length == 0) return false;
        var spots = new List<NaturalInfo>();
        for (int radius = 7; radius <= 19 && spots.Count < 12; radius += 4)
        for (int dy = -radius; dy <= radius && spots.Count < 12; dy += 4)
        for (int dx = -radius; dx <= radius && spots.Count < 12; dx += 4)
        {
            var tile = new Point2(crater.Tile.x + dx, crater.Tile.y + dy);
            if (!CanPlaceSystemContent(tile) || NaturalTypeAt(tile) != 0 ||
                _removedNatural.Contains(tile) ||
                _context.NaturalRegrow.Any(n => n.X == tile.x && n.Y == tile.y)) continue;
            ushort type = types[spots.Count % types.Length];
            AddNatural(tile, type);
            spots.Add(new NaturalInfo { X = (ushort)tile.x, Y = (ushort)tile.y, EntityType = type });
        }
        if (spots.Count == 0) return false;
        _context.CraterResources ??= new();
        _context.CraterResources[id] = spots;
        ArtifactManager.UpdateCrack(id, c =>
        {
            c.CurrentInvestment = c.RequiredInvestment;
            c.ActivatedSince = now; c.ActivatedUntil = now + CrackTuning.ActivatedTime;
            return c;
        });
        ArtifactManager.SetDisplayPart(id, "common", CrackTuning.CraterLook);
        Save();
        return true;
    }

    private void ProcessCraterStates(double now)
    {
        foreach (var crater in ArtifactManager.Enumerable(a => a.States.Crack?.ActivatedUntil <= now).ToArray())
        {
            if (_context.CraterResources?.Remove(crater.EntityId, out var spots) == true)
            foreach (var spot in spots)
            {
                var tile = new Point2(spot.X, spot.Y);
                ushort currentType = NaturalTypeAt(tile);
                if (currentType != 0 && currentType != spot.EntityType) continue;
                if (currentType == 0 && _context.NaturalRegrow.Any(n => n.X == tile.x && n.Y == tile.y &&
                    n.EntityType != spot.EntityType)) continue;
                // Não remover substituições colocadas por outra mecânica/admin.
                if (currentType == spot.EntityType)
                {
                    RemoveNaturalFromGarden(tile, out _);
                    _addedNatural.RemoveAll(n => n.X == tile.x && n.Y == tile.y);
                    NaturalDestroyed?.Invoke(tile);
                }
                _context.NaturalRegrow.RemoveAll(n => n.X == tile.x && n.Y == tile.y);
                // Spots temporários nasceram em espaços livres. A coleta não pode
                // deixar um tombstone permanente que impeça a próxima indução.
                _removedNatural.RemoveAll(p => p.x == tile.x && p.y == tile.y);
                _context.NaturalHarvests.Remove($"{tile.x},{tile.y}");
                _naturalGenerations[$"{tile.x},{tile.y}"] = NaturalGeneration(tile) + 1;
            }
            ArtifactManager.UpdateCrack(crater.EntityId, _ => MakeClosedCrack(RegionLevel));
            ArtifactManager.SetDisplayPart(crater.EntityId, "common", BlueprintStore.GetBlueprint(crater.EntityType).DefaultLook);
            Save();
        }
    }

    public bool CanPlaceSystemContent(Point2 tile, int clearance = 3)
    {
        if (!TerrainEcology.IsLand(_terrainData, tile)) return false;
        if (Math.Abs(tile.x - EntryPoint.x) < 8 && Math.Abs(tile.y - EntryPoint.y) < 8) return false;
        return !ArtifactManager.Enumerable(_ => true).Any(a =>
            Math.Abs(a.Tile.x - tile.x) <= Math.Max(1, a.Size.x) + clearance &&
            Math.Abs(a.Tile.y - tile.y) <= Math.Max(1, a.Size.y) + clearance);
    }

    private void PopulateSafehouseResources()
    {
        var template = RegionCatalog.GetTemplate(_terrainData.Info?.region_template);
        if (template?.Role != Role.Safehouse || _context.SafehouseEcologyVersion >= 1) return;
        var types = template.CollectibleLevels.Keys.OrderBy(t => t).Where(type =>
            !IsFishingNatural(type) && CollectibleTable.GeneratorCount(type) > 0).ToArray();
        if (types.Length == 0) return;
        int added = 0;
        foreach (var tile in TerrainEcology.LandGrid(_terrainData, 6))
        {
            if (added >= 180) break;
            if (!CanPlaceSystemContent(tile) || NaturalTypeAt(tile) != 0 || _removedNatural.Contains(tile) ||
                _context.NaturalRegrow.Any(n => n.X == tile.x && n.Y == tile.y)) continue;
            AddNatural(tile, types[added % types.Length]);
            added++;
        }
        _context.SafehouseEcologyVersion = 1;
        Save();
        Console.WriteLine($"[safehouse] adicionados {added} spots em {types.Length} tipos nativos, sem sobrepor construções.");
    }

    public Point2 PortalLanding(Point2 portal)
    {
        for (int radius = 4; radius <= 32; radius++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            var tile = new Point2(portal.x + dx, portal.y + dy);
            if (CanPlaceSystemContent(tile, 0) && NaturalTypeAt(tile) == 0) return tile;
        }
        return portal;
    }
}
