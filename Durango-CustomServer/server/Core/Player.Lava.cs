using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Region;

namespace Durango.Online;

public partial class Player
{
    private double _lavaCheckedAt;
    private World _lavaWorld;
    private Location[] _lavaSourcePath;
    private readonly List<Location> _lavaLocations = new();
    // The original client batches completed movement for 0.5 seconds. Keep a
    // small margin before assuming an unreported position is stationary.
    private const double LavaMovementGraceSeconds = .6;

    private void ObserveLavaMovement(Movement[] movements)
    {
        if (!ReferenceEquals(_lavaWorld, _world) || _lavaLocations.Count == 0) return;
        _lavaLocations.AddRange(movements.SelectMany(m => m.Path ?? Array.Empty<Location>())
            .Where(p => double.IsFinite(p.Time) && float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)));
        var sorted = _lavaLocations.OrderBy(p => p.Time).GroupBy(p => p.Time).Select(g => g.Last()).ToArray();
        _lavaLocations.Clear(); _lavaLocations.AddRange(sorted);
        _lavaSourcePath = _context.AppearPlayer.Move.Movements[0].Path;
    }

    private WorldPosition LavaPositionAt(double at)
    {
        var previous = _lavaLocations[0];
        foreach (var point in _lavaLocations.Skip(1))
        {
            if (point.Time > at)
            {
                float fraction = (float)Math.Clamp((at - previous.Time) / (point.Time - previous.Time), 0, 1);
                return new(previous.Position.x + (point.Position.x - previous.Position.x) * fraction,
                    previous.Position.y + (point.Position.y - previous.Position.y) * fraction);
            }
            previous = point;
        }
        return previous.Position;
    }

    private bool HasLavaProtection(double now)
    {
        foreach (var effect in _timedStatusEffects.Values)
            if (WorldStatusRules.IsActiveTimed(effect.Until, now) &&
                StatusEffectCatalog.Get(effect.Id, effect.Level)?.PackDetails(effect.Level)
                    .Any(e => (int)e.Type == 9 && e.Key == "lava" && e.Value > 0) == true) return true;
        foreach (var id in _context.EquippedItems.Values)
        {
            int index = _context.InventoryItems.FindIndex(i => i.Id == id);
            if (index >= 0 && _context.InventoryItems[index].Tags?.Any(t => t.Id == "immune_lava" && t.Level > 0) == true)
                return true;
        }
        return false;
    }

    private void UpdateLavaExposure(double now)
    {
        bool sameWorld = ReferenceEquals(_lavaWorld, _world);
        _lavaWorld = _world;
        if (!_context.AppearPlayer.IsAlive || !TryPlayerPositionAt(now, out WorldPosition current))
        {
            _lavaLocations.Clear();
            _lavaSourcePath = null;
            if (ClearTimedStatusEffect("lava")) SendStatusEffects();
            return;
        }
        var source = _context.AppearPlayer.Move.Movements[0].Path;
        if (!sameWorld || !ReferenceEquals(_lavaSourcePath, source) || _lavaLocations.Count == 0)
        {
            _lavaLocations.Clear();
            _lavaCheckedAt = now - LavaMovementGraceSeconds;
            _lavaLocations.Add(new Location { Time = _lavaCheckedAt, Position = current });
            _lavaSourcePath = source;
        }
        double since = _lavaCheckedAt;
        double until = Math.Max(now - LavaMovementGraceSeconds, Math.Min(now, _lavaLocations[^1].Time));
        bool protectedFromLava = HasLavaProtection(now);
        double exposure = 0;
        if (since < until && !protectedFromLava)
        {
            var times = _lavaLocations.Select(p => p.Time)
                .Where(t => t > since && t < until).Append(since).Append(until).Distinct().OrderBy(t => t).ToArray();
            for (int i = 1; i < times.Length; i++)
                exposure += LavaExposure.Seconds(LavaPositionAt(times[i - 1]), LavaPositionAt(times[i]),
                    times[i] - times[i - 1], _world.BiomeAt);
        }
        _lavaCheckedAt = Math.Max(since, until);
        int older = _lavaLocations.FindLastIndex(p => p.Time <= _lavaCheckedAt);
        if (older > 0) _lavaLocations.RemoveRange(0, older);
        if (exposure > 0)
        {
            float velocity = StatusEffectCatalog.Get("lava")?.Type1Velocities.GetValueOrDefault("life") ?? 0;
            _survival.Add(SurvivalState.KeyLife, (float)(velocity * exposure));
            FlushSurvival();
        }
        bool onLava = _context.AppearPlayer.IsAlive && !protectedFromLava &&
            WorldStatusRules.UnmaskBiome(_world.BiomeAt(WorldStatusRules.TileFromWorldPosition(current.x, current.y))) == Biome.Lava;
        bool changed = onLava ? EnsureTimedStatusEffect("lava") : ClearTimedStatusEffect("lava");
        if (changed) SendStatusEffects();
    }
}
