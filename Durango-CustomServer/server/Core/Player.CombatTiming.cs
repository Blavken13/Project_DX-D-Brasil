using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private sealed record PendingBattleHit(BattleActionData Action, BattleAttackInfo Hit,
        string TargetId, string WeaponId, double DueAt, float AimX, float AimY);
    private readonly List<PendingBattleHit> _pendingBattleHits = new();
    private double _battleAnimationUntil;

    private string CombatWeaponId() => _context.EquippedItems.GetValueOrDefault("main") ??
        _context.EquippedItems.GetValueOrDefault("both");

    private void CancelBattleHits()
    {
        _pendingBattleHits.Clear();
        _battleAnimationUntil = 0;
    }

    private void QueueBattleHits(BattleActionData action, string targetId, double now)
    {
        _pendingBattleHits.Clear();
        if (string.IsNullOrEmpty(targetId)) return;
        var animal = _world.AnimalManager?.Get(targetId);
        var victim = TryResolveVictim(targetId);
        float aimX = 0, aimY = 1;
        if ((animal != null || victim != null) && TryPlayerPositionAt(now, out var from))
        {
            var to = animal != null ? animal.PositionAt(now) :
                victim.TryPlayerPositionAt(now, out var position) ? position : from;
            float dx = to.x - from.x, dy = to.y - from.y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > .001f) { aimX = dx / distance; aimY = dy / distance; }
        }
        foreach (var hit in action.attack_info ?? Array.Empty<BattleAttackInfo>())
            if (hit != null) _pendingBattleHits.Add(new(action, hit, targetId, CombatWeaponId(),
                now + CombatDamage.ImpactDelay(action, hit), aimX, aimY));
        _pendingBattleHits.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        _battleAnimationUntil = now + Math.Max(0, action.meta.action_length);
    }

    private bool HitIntersectsAnimal(AnimalManager.Animal animal, PendingBattleHit pending, double now)
    {
        if (animal == null || !animal.IsAlive || animal.Captured || animal.CorpseDisposed) return false;
        var info = AnimalTypes.Get(animal.EntityType);
        return HitIntersectsPosition(animal.PositionAt(now), (info?.BoundRadius ?? 0) * (info?.BaseScale ?? 1), pending, now);
    }

    private bool HitIntersectsPosition(WorldPosition to, float radius, PendingBattleHit pending, double now)
    {
        var hit = pending.Hit;
        // use_range is the distance at which a charge can START. Its actual
        // damage uses the circle/rectangle around the character at impact.
        if (!TryPlayerPositionAt(now, out var from)) return false;
        float dx = to.x - from.x, dy = to.y - from.y;
        if (dx * dx + dy * dy > MathF.Pow(CombatTuning.MaxServerAttackRangeTiles * 200, 2)) return false;
        if (hit.radius > 0 || hit.rect_half_size?.Length != 2)
        {
            float reach = Math.Min(CombatTuning.MaxServerAttackRangeTiles * 200,
                Math.Max(0, hit.radius > 0 ? hit.radius : pending.Action.meta.use_range ?? 0) + radius);
            return dx * dx + dy * dy <= reach * reach;
        }
        float lateral = dx * pending.AimY - dy * pending.AimX - (hit.offset?.ElementAtOrDefault(0) ?? 0);
        float forward = dx * pending.AimX + dy * pending.AimY - (hit.offset?.ElementAtOrDefault(1) ?? 0);
        float outsideX = Math.Max(0, Math.Abs(lateral) - hit.rect_half_size[0]);
        float outsideY = Math.Max(0, Math.Abs(forward) - hit.rect_half_size[1]);
        return outsideX * outsideX + outsideY * outsideY <= radius * radius;
    }

    private bool CanHitAnimal(AnimalManager.Animal animal, float reach, double now)
    {
        if (animal == null || !animal.IsAlive || animal.Captured || animal.CorpseDisposed) return false;
        if (!TryPlayerPositionAt(now, out var from)) return false;
        var to = animal.PositionAt(now);
        var info = AnimalTypes.Get(animal.EntityType);
        double range = Math.Min(CombatTuning.MaxServerAttackRangeTiles * 200,
            Math.Max(0, reach) + (info?.BoundRadius ?? 0) * (info?.BaseScale ?? 1));
        double dx = from.x - to.x, dy = from.y - to.y;
        return dx * dx + dy * dy <= range * range;
    }

    private bool TryPlayerPositionAt(double now, out WorldPosition position)
    {
        position = default;
        var movements = MovementPath();
        if (movements == null || movements.Length == 0) return false;
        var path = movements.Where(m => m.Path?.Length > 0).SelectMany(m => m.Path).OrderBy(p => p.Time).ToArray();
        if (path.Length == 0) return false;
        position = path[0].Position;
        for (int i = 1; i < path.Length; i++)
        {
            if (now < path[i].Time)
            {
                double duration = path[i].Time - path[i - 1].Time;
                float t = duration > 0 ? (float)Math.Clamp((now - path[i - 1].Time) / duration, 0, 1) : 1;
                position = new WorldPosition(path[i - 1].Position.x + (path[i].Position.x - path[i - 1].Position.x) * t,
                    path[i - 1].Position.y + (path[i].Position.y - path[i - 1].Position.y) * t);
                return true;
            }
            position = path[i].Position;
        }
        return true;
    }

    private void UpdatePendingBattleHits(double now)
    {
        if (!_context.AppearPlayer.IsAlive) { CancelBattleHits(); return; }
        while (_pendingBattleHits.Count > 0 && _pendingBattleHits[0].DueAt <= now)
        {
            var pending = _pendingBattleHits[0];
            _pendingBattleHits.RemoveAt(0);
            if (pending.WeaponId != CombatWeaponId()) { CancelBattleHits(); return; }
            if (pending.WeaponId != null)
            {
                int index = _context.InventoryItems.FindIndex(i => i.Id == pending.WeaponId);
                if (index < 0 || _context.InventoryItems[index].Durability?.Get() <= 0)
                    { CancelBattleHits(); return; }
            }
            var animal = _world.AnimalManager?.Get(pending.TargetId);
            if (HitIntersectsAnimal(animal, pending, now)) ApplyAnimalAttack(animal, pending.Hit, now);
            else if (TryResolveVictim(pending.TargetId) is { } victim &&
                victim.TryPlayerPositionAt(now, out var position) && HitIntersectsPosition(position, BattleDataStore.Stats.bound_radius, pending, now))
                victim.ReceiveAttack(this, pending.Hit, now);
        }
    }
}
