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
        .FirstOrDefault(e => e.Value.Type == (int)type && e.Value.OwnerId ==
            (type == OwnerType.ClanEstate ? CurrentClanId() : EntityId)).Key;

    private World WorldForClanEstate()
    {
        string clanId = CurrentClanId();
        if (clanId == null) return null;
        bool HasEstate(World world) => world?.EnumerateEstates().Any(e =>
            e.Value.Type == (int)OwnerType.ClanEstate && e.Value.OwnerId == clanId) == true;
        if (HasEstate(_world)) return _world;
        var registry = _world.Registry;
        if (registry?.EnsureDefaultSharedTamedRegion() == true)
        {
            var shared = registry.GetOrCreate(WorldRegistry.DefaultSharedTamedRegionId);
            if (HasEstate(shared)) return shared;
        }
        string region = EnsureClanWorldRegistered();
        var legacy = region == null ? null : registry?.GetOrCreate(region);
        return HasEstate(legacy) ? legacy : null;
    }

    private Point2 ConsumeEstateArrival()
    {
        EstateArrival arrival = _context.PendingEstateArrival;
        _context.PendingEstateArrival = null;
        if (arrival != null && string.Equals(arrival.RegionId, LogicalRegionId(), StringComparison.OrdinalIgnoreCase))
        {
            EstateRecord estate = _world.GetEstate(arrival.EstateId);
            bool owned = estate != null && (estate.OwnerId == EntityId &&
                estate.Type is (int)OwnerType.Player or (int)OwnerType.PersonalPlayer ||
                estate.Type == (int)OwnerType.ClanEstate && estate.OwnerId == CurrentClanId());
            if (owned &&
                _world.TryEstateArrivalTile(arrival.EstateId, out Point2 tile)) return tile;
        }
        return _world.EntryPoint;
    }
}
