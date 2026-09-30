using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Region;

namespace Durango.Online;

public partial class World
{
    public bool HasTemporaryPlayerStructures =>
        !_context.IsSettlementIsland && RegionCatalog.GetTemplate(_terrainData.Info?.region_template)?.Role == Role.Risky;

    private void InitializeWildStructureExpirations()
    {
        _context.WildStructureExpirations ??= new Dictionary<string, double>();
        if (!HasTemporaryPlayerStructures) return;
        bool changed = false;
        foreach (var artifact in ArtifactManager.Enumerable(_ => true))
        {
            string owner = ArtifactManager.OwnerOf(artifact.EntityId);
            if (string.IsNullOrEmpty(owner) || _context.WildStructureExpirations.ContainsKey(artifact.EntityId)) continue;
            // Saves anteriores recebem 24 horas completas ao carregar esta atualização.
            _context.WildStructureExpirations[artifact.EntityId] = Gauge.CurrentTime + WorldTuning.WildStructureLifetimeSeconds;
            changed = true;
        }
        if (changed) Save();
        ProcessWildStructureExpirations(Gauge.CurrentTime);
    }

    private void TrackWildStructure(string id, string owner)
    {
        if (!HasTemporaryPlayerStructures || string.IsNullOrEmpty(owner)) return;
        _context.WildStructureExpirations ??= new Dictionary<string, double>();
        _context.WildStructureExpirations.TryAdd(id, Gauge.CurrentTime + WorldTuning.WildStructureLifetimeSeconds);
    }

    internal void ProcessWildStructureExpirations(double now)
    {
        // A whitelist de Role.Risky protege ilhas domadas, particulares e conteúdo do sistema.
        if (!HasTemporaryPlayerStructures || _context.WildStructureExpirations == null) return;
        foreach (var entry in _context.WildStructureExpirations.Where(e => e.Value <= now).ToArray())
        {
            if (!string.IsNullOrEmpty(ArtifactManager.OwnerOf(entry.Key))) DestructArtifact(entry.Key);
            else _context.WildStructureExpirations.Remove(entry.Key);
        }
    }
}
