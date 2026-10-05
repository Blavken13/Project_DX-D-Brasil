using System;
using System.Linq;
using Messages;
using Shared.Estate;

namespace Durango.Online;

/// <summary>One arrival after returning to an owned estate across islands.</summary>
public sealed class EstateArrival
{
    public string RegionId;
    public string EstateId;
}

public partial class Player
{
    private World WorldForOwnedEstate(OwnerType type)
    {
        WorldRegistry registry = _world.Registry;
        if (registry == null) return _world;
        if (type == OwnerType.Player)
            return registry.EnsureDefaultSharedTamedRegion()
                ? registry.GetOrCreate(WorldRegistry.DefaultSharedTamedRegionId) : null;
        if (type == OwnerType.PersonalPlayer && !string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            EnsurePersonalWorldRegistered();
            return registry.GetOrCreate(_context.PersonalRegionId);
        }
        return null;
    }

    private string OwnedEstateId(World world, OwnerType type) => world?.EnumerateEstates()
        .FirstOrDefault(e => e.Value.Type == (int)type && e.Value.OwnerId == EntityId).Key;

    private Point2 ConsumeEstateArrival()
    {
        EstateArrival arrival = _context.PendingEstateArrival;
        _context.PendingEstateArrival = null;
        if (arrival != null && string.Equals(arrival.RegionId, LogicalRegionId(), StringComparison.OrdinalIgnoreCase))
        {
            EstateRecord estate = _world.GetEstate(arrival.EstateId);
            if (estate?.OwnerId == EntityId && estate.Type is (int)OwnerType.Player or (int)OwnerType.PersonalPlayer &&
                _world.TryEstateArrivalTile(arrival.EstateId, out Point2 tile)) return tile;
        }
        return _world.EntryPoint;
    }
}
