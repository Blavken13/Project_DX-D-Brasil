using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Yaml;

namespace Durango.Online;

public partial class Player
{
    private double _clanBenefitsCheckAt;
    private string _clanBenefitsSignature;
    private readonly HashSet<string> _clanEffectIds = new();
    private float ClanModifier(string key)
    {
        float total = 0; double now = Times.UnixTimeNow();
        foreach (string id in _clanEffectIds)
        {
            if (!_timedStatusEffects.TryGetValue(id, out var effect) || effect.Until > 0 && effect.Until <= now) continue;
            foreach (var spec in StatusEffectCatalog.Get(id, effect.Level)?.EffectSpecs ?? new())
                if (spec.Type == 3 && spec.Key == key && StatFormula.TryEval(spec.ValueExpr, "level", effect.Level, out double value)) total += (float)value;
        }
        return total;
    }
    private float ClanAbilityBonus(Shared.Ability.Derived ability) => SkillDataStore.DerivedOfModifier
        .Where(m => m.Value == ability && m.Key.EndsWith("_plus", StringComparison.Ordinal)).Sum(m => ClanModifier(m.Key));
    private void SendClanFund(uint seq)
    {
        var clan = ClanStore.Find(ClanStore.ClanIdOf(EntityId));
        if (clan == null) { Send(new Abort { Text = "Você não pertence a um clã." }, seq); return; }
        Send(new Costs { _Costs = new() { [Shared.Economy.Currency.TStone] = clan.Fund } }, seq);
    }
    private bool InClanTerritory(string clanId, Shared.Estate.OwnerType? type = null)
    {
        var position = PlayerWorldPosition();
        var cell = World.CellFromTile(new Point2((int)(position.x / 200), (int)(position.y / 200)));
        if (!_world.TryGetEstateIdAtCell(cell, out string id)) return false;
        var estate = _world.GetEstate(id);
        return estate?.OwnerId == clanId && (type.HasValue ? estate.Type == (int)type.Value
            : estate.Type is (int)Shared.Estate.OwnerType.ClanEstate or (int)Shared.Estate.OwnerType.ClanWarphole);
    }
    private void SyncClanBenefits(bool force = false)
    {
        double now = Times.UnixTimeNow();
        if (!force && now < _clanBenefitsCheckAt) return;
        _clanBenefitsCheckAt = now + 2;
        var clan = ClanStore.Find(ClanStore.ClanIdOf(EntityId));
        var effects = new Dictionary<string, (int Level, double Until)>();
        if (clan != null)
        {
            int level = ClanRules.GrowthBuff(clan.Level);
            if (level > 0) effects["clan_growth_buff"] = (level, 0);
            foreach (var research in clan.Researches.Values.Where(r => r.Until > now))
            {
                var definition = ClanRules.Research(research.Id);
                if (definition == null) continue;
                int limit = (int)definition["effect"]["apply_limits"];
                bool applies = limit == 1 || limit == 0 && InClanTerritory(clan.Id)
                    || limit == 2 && InClanTerritory(clan.Id, Shared.Estate.OwnerType.ClanWarphole);
                if (applies) effects[(string)definition["effect"]["status_effect_id"]] = ((int)definition["effect"]["level"], research.Until);
            }
        }
        string signature = (clan?.Id ?? "") + ":" + (clan?.Level ?? 0) + ":" + string.Join("|", effects.Select(e => e.Key + ":" + e.Value));
        if (_clanBenefitsSignature == signature) return;
        _clanBenefitsSignature = signature;
        // Also clear clan buffs restored from an old character save after membership expired.
        var stale = _timedStatusEffects.Keys.Where(id => id == "clan_growth_buff" || ClanRules.ResearchIds.Any(r => (string)ClanRules.Research(r)["effect"]["status_effect_id"] == id)).ToArray();
        foreach (string id in stale) if (!effects.ContainsKey(id)) ClearTimedStatusEffect(id);
        _clanEffectIds.Clear();
        foreach (var effect in effects)
        {
            ApplyTimedStatusEffect(effect.Key, effect.Value.Level, effect.Value.Until > 0 ? effect.Value.Until - now : 0);
            _clanEffectIds.Add(effect.Key);
        }
        float recovery = ClanModifier("life_recovery");
        _survival.SetMomentum("clan_recovery", recovery > 0 ? new() { [SurvivalState.KeyLife] = recovery } : null);
        RefreshSurvivalMax(); SendStatusEffects(); SendRecipes(0); SyncFatigueVelocities();
    }
    private bool TryClanLab(string entityId, Point2 tile, out string blueprint)
    {
        blueprint = null;
        var clan = ClanStore.Find(ClanStore.ClanIdOf(EntityId));
        var artifact = _world.ArtifactManager.Get(entityId);
        if (clan == null || !artifact.HasValue || artifact.Value.Tile.x != tile.x || artifact.Value.Tile.y != tile.y
            || !artifact.Value.IsAlive || artifact.Value.States.BuildingState != Shared.Building.BuildingState.Completed
            || !IsWithinTiles(tile, ArtifactReachTiles + Math.Max(artifact.Value.Size.x, artifact.Value.Size.y))) return false;
        Point2 cell = World.CellFromTile(tile);
        if (!_world.TryGetEstateIdAtCell(cell, out string estateId) || _world.GetEstate(estateId)?.OwnerId != clan.Id) return false;
        blueprint = BlueprintStore.GetBlueprint(artifact.Value.EntityType)?.Id;
        return blueprint != null;
    }
    private string[] AvailableClanResearch(string entityId, Point2 tile)
    {
        if (!TryClanLab(entityId, tile, out string blueprint)) return Array.Empty<string>();
        var clan = ClanStore.Find(ClanStore.ClanIdOf(EntityId));
        double now = Times.UnixTimeNow();
        if (!ClanStore.HasPermission(clan, EntityId, Shared.Clan.Permissions.Research)
            || clan.Researches.Values.Any(r => r.Lab == entityId && Math.Max(r.Until, r.CooltimeUntil) > now)) return Array.Empty<string>();
        return ClanRules.ResearchIds.Where(id =>
        {
            var d = ClanRules.Research(id); int category = (int)d["category"];
            return blueprint == ClanRules.LabBlueprint(category) && clan.Level >= ClanRules.ResearchLevel(category)
                && (!clan.Researches.TryGetValue(id, out var existing) || Math.Max(existing.Until, existing.CooltimeUntil) <= now);
        }).ToArray();
    }
    private void StartClanResearch(StartClanResearch msg, uint seq)
    {
        if (!AvailableClanResearch(msg.EntityId, msg.Tile).Contains(msg.Id))
        { Send(new Abort { Text = "Pesquisa indisponível ou laboratório fora do território e alcance permitidos." }, seq); return; }
        if (!EconomyAvailable(seq)) return;
        var d = ClanRules.Research(msg.Id); double now = Times.UnixTimeNow();
        var research = new ClanResearchRecord { Id = msg.Id, Lab = msg.EntityId, Since = now,
            Until = now + (double)d["duration"], CooltimeUntil = now + (double)d["duration"] + (double)d["cooltime"] };
        if (!_economy.ClanTransaction(_context, "research", msg.Id, 0, research, out var clan, out string error))
        { Send(new Abort { Text = error }, seq); return; }
        PushClanStateToOnline(clan.Id);
        Send(new Messages.ClanResearch { ResearchId = research.Id, LabEntityId = research.Lab, Until = research.Until, CooltimeUntil = research.CooltimeUntil }, seq);
    }
}
