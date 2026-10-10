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

    private void RestoreCraterMechanics()
    {
        bool repaired = false;
        foreach (var crater in ArtifactManager.Enumerable(a => a.IsAlive && a.EntityType == 7037).ToArray())
        {
            if (ArtifactManager.RestoreCrack(crater.EntityId, MakeClosedCrack(RegionLevel)))
            {
                ArtifactManager.SetDisplayPart(crater.EntityId, "common", BlueprintStore.GetBlueprint(7037).DefaultLook);
                repaired = true;
            }
        }
        if (repaired) Save();
        ProcessCraterStates(Gauge.CurrentTime);
    }

    public bool ActivateCrater(string id, double now)
    {
        ProcessCraterStates(now);
        if (ArtifactManager.Get(id) is not { } crater || crater.States.Crack is not { } crack ||
            crack.ActivatedUntil > now) return false;
        // As tabelas de biocoms não estão no pacote. Usar os recursos nativos
        // do próprio mapa, preservando terreno, spots e construções existentes.
        var types = CraterMineralTypes(crater.Tile);
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

    public ushort GatheringTypeAt(Point2 tile, ushort entityType)
    {
        string collectible = DataHelper.GetBiomeSpriteInfo(entityType)?.CollectibleId;
        if (collectible is not ("rock" or "stone")) return entityType;
        if (WorldStatusRules.UnmaskBiome(BiomeAt(tile)) == Biome.Desert &&
            ArtifactManager.Enumerable(a => a.IsAlive && a.EntityType == 7037)
                .Any(c => NearCrater(tile, c.Tile))) return 13048; // native obsidian
        return entityType;
    }

    private static bool NearCrater(Point2 tile, Point2 crater) =>
        Math.Max(Math.Abs(tile.x - crater.x), Math.Abs(tile.y - crater.y)) <= 24;

    private static bool IsCraterMineral(ushort type)
    {
        string id = DataHelper.GetBiomeSpriteInfo(type)?.CollectibleId;
        return id != null && (id.StartsWith("ore_", StringComparison.Ordinal) ||
            id.StartsWith("jewel_", StringComparison.Ordinal) || id.StartsWith("rock", StringComparison.Ordinal) ||
            id.StartsWith("stone", StringComparison.Ordinal) || id.StartsWith("basalt", StringComparison.Ordinal) ||
            id.StartsWith("granite", StringComparison.Ordinal) || id is "obsidian" or "sulphur") &&
            CollectibleTable.AllSpecs(type).Any(s => PrototypeYaml.GetItemPrototype(s.PrototypeId)?.Category == "mineral");
    }

    private ushort[] CraterMineralTypes(Point2 crater)
    {
        var native = NaturalInfo.FromBytes(_terrainData.Garden ?? Array.Empty<byte>())
            .Where(n => IsCraterMineral(n.EntityType)).ToArray();
        var near = native.Where(n => NearCrater(new Point2(n.X, n.Y), crater)).ToArray();
        ushort Resolve(NaturalInfo n) => GatheringTypeAt(new Point2(n.X, n.Y), n.EntityType);
        bool Specific(ushort type) => DataHelper.GetBiomeSpriteInfo(type)?.CollectibleId is { } id &&
            id is not ("rock" or "stone") && !id.StartsWith("rock", StringComparison.Ordinal) &&
            !id.StartsWith("stone", StringComparison.Ordinal);
        var types = near.Select(Resolve).Where(Specific).ToArray();
        if (types.Length == 0) types = native.Select(Resolve).Where(Specific).ToArray();
        if (types.Length == 0) types = near.Select(Resolve).ToArray();
        if (types.Length == 0) types = native.Select(Resolve).ToArray();
        return types.Distinct().OrderBy(t => t)
            .GroupBy(t => DataHelper.GetBiomeSpriteInfo(t).CollectibleId).Select(g => g.First()).ToArray();
    }

    private void PopulateCraterMinerals()
    {
        if (_context.CraterMineralEcologyVersion >= 1 || IsTutorialIsland) return;
        var current = new List<NaturalInfo>();
        foreach (var chunk in _chunkData) current.AddRange(NaturalInfo.FromBytes(chunk.Garden ?? Array.Empty<byte>()));
        foreach (var crater in ArtifactManager.Enumerable(a => a.IsAlive && a.EntityType == 7037).ToArray())
        foreach (ushort type in CraterMineralTypes(crater.Tile))
        {
            string collectible = DataHelper.GetBiomeSpriteInfo(type).CollectibleId;
            int count = current.Count(n => NearCrater(new Point2(n.X, n.Y), crater.Tile) &&
                DataHelper.GetBiomeSpriteInfo(GatheringTypeAt(new Point2(n.X, n.Y), n.EntityType))?.CollectibleId == collectible);
            // Harvested spots already have a regrowth timer; never replace them.
            count += _context.NaturalRegrow.Count(n => NearCrater(new Point2(n.X, n.Y), crater.Tile) &&
                NaturalTypeAt(new Point2(n.X, n.Y)) == 0 && DataHelper.GetBiomeSpriteInfo(n.EntityType)?.CollectibleId == collectible);
            for (int radius = 8; radius <= 20 && count < 3; radius += 4)
            for (int dy = -radius; dy <= radius && count < 3; dy += 4)
            for (int dx = -radius; dx <= radius && count < 3; dx += 4)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                var tile = new Point2(crater.Tile.x + dx, crater.Tile.y + dy);
                if (!CanPlaceSystemContent(tile) || NaturalTypeAt(tile) != 0 || _removedNatural.Contains(tile) ||
                    _context.NaturalRegrow.Any(n => n.X == tile.x && n.Y == tile.y) ||
                    current.Any(n => Math.Abs(n.X - tile.x) <= 1 && Math.Abs(n.Y - tile.y) <= 1)) continue;
                AddNatural(tile, type);
                current.Add(new NaturalInfo { X = (ushort)tile.x, Y = (ushort)tile.y, EntityType = type });
                count++;
            }
        }
        _context.CraterMineralEcologyVersion = 1;
        Save();
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

    private void PopulateFlowerResources()
    {
        // Uma migracao por mundo, inclusive saves com garden persistido. Nao e
        // uma rotina por frame e nao restaura imediatamente plantas ja coletadas.
        if (_context.FlowerEcologyVersion >= 1 ||
            RegionCatalog.GetTemplate(_terrainData.Info?.region_template)?.Role == Role.Tutorial) return;
        ushort[] types = { 11008, 11020, 11091, 14063, 14064 };
        const int minimumPerType = 8;
        var current = new List<NaturalInfo>();
        foreach (var chunk in _chunkData)
            current.AddRange(NaturalInfo.FromBytes(chunk.Garden ?? Array.Empty<byte>()));
        var candidates = TerrainEcology.LandGrid(_terrainData, 4).ToArray();
        var landmarks = LandmarkInfo.FromBytes(_terrainData.Landmarks ?? Array.Empty<byte>());
        int added = 0;
        foreach (ushort type in types)
        {
            var info = DataHelper.GetBiomeSpriteInfo(type);
            if (info?.Survivability == null || !CollectibleTable.AllSpecs(type).Any(s => s.PrototypeId == "flower")) continue;
            int count = current.Count(n => n.EntityType == type) + _context.NaturalRegrow.Count(n => n.EntityType == type);
            if (count >= minimumPerType) continue;
            // Ordem estavel, distribuida pelo mapa; sem Random compartilhado com combate.
            foreach (var tile in candidates.OrderBy(p => unchecked((uint)(p.x * 73856093 ^ p.y * 19349663 ^ type))))
            {
                if (count >= minimumPerType) break;
                if (!info.Survivability.Contains(TerrainEcology.BiomeAt(_terrainData, tile)) ||
                    !CanPlaceSystemContent(tile) || _removedNatural.Contains(tile) ||
                    _context.NaturalRegrow.Any(n => Math.Abs(n.X - tile.x) <= 1 && Math.Abs(n.Y - tile.y) <= 1) ||
                    current.Any(n => Math.Abs(n.X - tile.x) <= 1 && Math.Abs(n.Y - tile.y) <= 1) ||
                    landmarks.Any(n => Math.Abs(n.X - tile.x) <= 2 && Math.Abs(n.Y - tile.y) <= 2)) continue;
                AddNatural(tile, type);
                current.Add(new NaturalInfo { X = (ushort)tile.x, Y = (ushort)tile.y, EntityType = type });
                count++; added++;
            }
        }
        _context.FlowerEcologyVersion = 1;
        Save();
        if (added > 0) Console.WriteLine($"[flores] {TerrainId}: restaurados {added} spots nos biomas nativos.");
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
