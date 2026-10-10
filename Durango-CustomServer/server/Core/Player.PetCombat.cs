using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Ability;
using Shared.Battle;

namespace Durango.Online;

public partial class Player
{
    // The client follows the owner outside battle; BattleBegun hands movement to the server.
    private sealed class PetBattle
    {
        public PetStore.Entry Entry;
        public AnimalManager.Animal Body;
        public string EnemyId;
        public double HitAt;
        public double MotionUntil;
        public WorldPosition Aim;
    }
    private PetBattle _petBattle;
    private string _petEnemyId;
    private readonly Dictionary<string, double> _petAttackReadyAt = new(StringComparer.Ordinal);
    private const float PetCombatLeash = 2400;
    private const float PetAttackReach = 150;
    private static class PetBattleTuning
    {
        private static readonly Newtonsoft.Json.Linq.JToken Rules =
            Json.ReadFromFile<Newtonsoft.Json.Linq.JObject>("constants")?["pet"]?["battle"];
        public static float EnterFoodRatio => (float?)Rules?["hungry_ratio_enter_battle"] ?? .5f;
        public static float RemainFoodRatio => (float?)Rules?["hungry_ratio_remain_battle"] ?? 0;
    }

    private static float PetAbility(PetStore.Entry pet, Derived ability) =>
        pet.Pet.Statistics.DerivedAbilities?.GetValueOrDefault(ability) ?? 0;

    private void StopPetCombat()
    {
        if (_petBattle == null) return;
        string id = _petBattle.Entry.Pet.EntityId;
        _petBattle = null;
        _world.BroadCast(new BattleEnded { EntityId = id, EventAt = Gauge.CurrentTime });
    }

    private bool PetEnemyPosition(string id, double now, out WorldPosition position, out float radius)
    {
        position = default; radius = BattleDataStore.Stats.bound_radius;
        if (_world.AnimalManager?.Get(id) is { } animal)
        {
            if (!animal.IsAlive || animal.Captured || animal.CorpseDisposed || animal.CaptureOwnerId != null) return false;
            position = animal.PositionAt(now);
            var info = AnimalTypes.Get(animal.EntityType);
            radius = (info?.BoundRadius ?? 0) * (info?.BaseScale ?? 1);
            return true;
        }
        return TryResolveVictim(id) is { } victim && victim.TryPlayerPositionAt(now, out position);
    }

    private static float DistanceSquared(WorldPosition a, WorldPosition b) =>
        (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

    private void UpdatePetCombat(double now)
    {
        var entry = PetStore.Of(EntityId).FirstOrDefault(e => e.Pet.IsSpawned);
        var definition = entry == null ? null : PetTables.PetOf(entry.Pet.EntityType);
        bool continuing = _petBattle != null && ReferenceEquals(_petBattle.Entry, entry);
        float hungry = entry?.Pet.Stat.Hungry?.Get(now) ?? 0;
        float hungryMax = entry?.HungryMax ?? 0;
        // Original pet.battle thresholds: at least 50% food to enter, greater than 0% to remain.
        if (!_inBattle || !_context.AppearPlayer.IsAlive || entry == null || !PetIsAlive(entry) ||
            entry.Grazing || entry.Pet.IsBoarding || definition?.IsFightable != true ||
            hungryMax <= 0 || hungry <= hungryMax * PetBattleTuning.RemainFoodRatio ||
            !continuing && hungry < hungryMax * PetBattleTuning.EnterFoodRatio ||
            !TryPlayerPositionAt(now, out var ownerPosition) ||
            !PetEnemyPosition(_petEnemyId, now, out var target, out float targetRadius) ||
            DistanceSquared(ownerPosition, target) > PetCombatLeash * PetCombatLeash)
        { StopPetCombat(); return; }

        if (!continuing)
        {
            StopPetCombat();
            _petBattle = new PetBattle { Entry = entry, EnemyId = _petEnemyId,
                Body = new AnimalManager.Animal { EntityId = entry.Pet.EntityId,
                    EntityType = (ushort)definition.VehicleEntityType, Position = ownerPosition, AggroTargetId = _petEnemyId } };
            _world.BroadCast(new BattleBegun { EntityId = entry.Pet.EntityId, EnemyId = _petEnemyId, EventAt = now });
        }
        var battle = _petBattle;
        if (battle.EnemyId != _petEnemyId)
        {
            battle.EnemyId = _petEnemyId; battle.HitAt = 0;
            battle.Body.AggroTargetId = _petEnemyId;
            _world.BroadCast(new BattleBegun { EntityId = entry.Pet.EntityId, EnemyId = _petEnemyId, EventAt = now });
        }
        var body = battle.Body;
        if (DistanceSquared(ownerPosition, body.PositionAt(now)) > PetCombatLeash * PetCombatLeash)
        { StopPetCombat(); return; }
        if (battle.HitAt > 0)
        {
            if (now < battle.HitAt) return;
            battle.HitAt = 0;
            float reach = PetAttackReach + targetRadius;
            if (DistanceSquared(body.PositionAt(now), target) <= reach * reach &&
                DistanceSquared(battle.Aim, target) <= AnimalAttackAimRadius * AnimalAttackAimRadius)
            {
                var hit = new BattleAttackInfo { damage_bonus = 1, accuracy_ratio = 1,
                    damage_type = DamageType.Melee, atk_ratio = new() { ["impact"] = 1 } };
                float power = PetAbility(entry, Derived.Attack);
                var attackType = BodyAttackTypeOf(AnimalTypes.Get(body.EntityType));
                if (_world.AnimalManager.Get(battle.EnemyId) is { } animal)
                    ApplyAnimalCombatHit(animal, hit, now, entry.Pet.EntityId, power, attackType, false);
                else if (TryResolveVictim(battle.EnemyId) is { } victim)
                    victim.ReceiveCombatHit(this, entry.Pet.EntityId, power, PetAbility(entry, Derived.Accuracy), attackType, hit, now, false);
            }
            if (!ReferenceEquals(_petBattle, battle)) return;
        }
        if (now < battle.MotionUntil || now < body.StopWalkingAt) return;
        float stopAt = Math.Max(PetAttackReach, PetAttackReach + targetRadius - 25);
        if (DistanceSquared(body.PositionAt(now), target) > stopAt * stopAt)
        {
            Move chase = _world.AnimalManager.BuildChase(body, target, stopAt, now);
            if (chase.Movements == null) { StopPetCombat(); return; }
            // Use the pet's derived speed, including milestone modifiers.
            float speed = Math.Max(1, PetAbility(entry, Derived.Speed));
            double travel = Math.Sqrt(DistanceSquared(body.WalkFrom, body.WalkTo)) / speed;
            body.WalkEndAt = body.StopWalkingAt = now + travel;
            chase.Movements[0].Path[1].Time = body.WalkEndAt;
            _world.BroadCast(chase);
            return;
        }
        if (now < _petAttackReadyAt.GetValueOrDefault(entry.Pet.EntityId))
        {
            _world.BroadCast(body.MakeMotion(body.CurrentMotion, body.Yaw, now, 2, true));
            battle.MotionUntil = now + 1;
            return;
        }
        Move motion = body.ToAttackMotionMessage(target, now, AttackMotionRng);
        if (motion.Movements == null) { StopPetCombat(); return; }
        float rate = entry.Pet.Stat.PlaybackRate > 0 ? entry.Pet.Stat.PlaybackRate : 1;
        motion.Movements[0].PlaybackRate = rate;
        motion.Movements[0].Path[1].Time = now + AnimalManager.Animal.AttackClipSeconds / rate;
        battle.MotionUntil = motion.Movements[0].Path[1].Time;
        battle.Aim = target;
        battle.HitAt = now + AnimalAttackImpactSeconds / rate;
        _petAttackReadyAt[entry.Pet.EntityId] = now + Math.Max(AnimalManager.Animal.AttackClipSeconds / rate,
            AnimalTypes.Get(body.EntityType)?.AttackCooltime ?? 2.2f);
        _world.BroadCast(motion);
    }
}
