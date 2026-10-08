using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Faction;
using Shared.Quest;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// IDs de sequência e objetivos de caça recuperados do roteiro safehouse e de
// constants.skip_tutorial_missions. Meta de um abate e recompensas usam o ajuste
// brasileiro; a tabela de missões do servidor original não acompanha o cliente.
public sealed class SafehouseMissionSave
{
    public HashSet<string> Completed = new(StringComparer.Ordinal);
    public SafehouseMissionProgress Current;
}

public sealed class SafehouseMissionProgress
{
    public string Id;
    public string RegionId;
    public double? StartedAt;
    public int Progress;
    public long TStones;
    public int ExpWeight;
}

public partial class Player
{
    private static readonly string[] SafehouseHuntingSequence = { "sh_sq_01", "sh_sq_02" };
    private bool IsSafehouse => RegionCatalog.GetTemplate(_world.TerrainInfo?.region_template)?.Role == Role.Safehouse;
    private static ushort SafehouseHuntType(string id) => id switch
        { "sh_sq_01" => 2015, "sh_sq_02" => 2051, _ => 0 };
    private static string SafehouseHuntTodo(string id) => id == "sh_sq_01" ? "hunt_sh_2015" : "hunt_05tr_2051";
    private SafehouseMissionSave HuntingSave
    {
        get
        {
            _context.SafehouseMissions ??= new();
            _context.SafehouseMissions.Completed ??= new(StringComparer.Ordinal);
            return _context.SafehouseMissions;
        }
    }

    private bool ValidateMissionRadio(string id, Point2 tile, uint seq)
    {
        var radio = string.IsNullOrEmpty(id) ? null : _world.ArtifactManager.Get(id);
        if (!radio.HasValue || radio.Value.Tile.x != tile.x || radio.Value.Tile.y != tile.y ||
            BlueprintStore.GetBlueprint(radio.Value.EntityType)?.Components?.Contains("FactionCenter") != true)
        {
            Send(new Abort { Text = "Esta tenda de comunicação não está disponível." }, seq);
            return false;
        }
        if (!_context.AppearPlayer.IsAlive || !IsWithinTiles(radio.Value.Tile,
            ArtifactReachTiles + Math.Max(radio.Value.Size.x, radio.Value.Size.y)))
        {
            Send(new Abort { Text = "Aproxime-se da tenda de comunicação para receber a missão." }, seq);
            return false;
        }
        return true;
    }

    private void SaveHuntingMission()
    {
        OnContextChanged();
        if (!string.IsNullOrEmpty(_context.Path)) _context.Save();
    }

    private void RecommendSafehouseMission(RecommendMissions msg, uint seq)
    {
        if (!ValidateMissionRadio(msg.EntityId, msg.Tile, seq)) return;
        if (!IsSafehouse)
        {
            Send(new Abort { Text = "As missões de caça do tutorial são recebidas na ilha do abrigo." }, seq);
            return;
        }
        var save = HuntingSave;
        string next = SafehouseHuntingSequence.FirstOrDefault(id => !save.Completed.Contains(id));
        if (save.Current == null && next != null)
        {
            var (stones, weight) = QuestRewardTuning.For(QuestCatalog.Find("daily_hunting_a_01"), _skillLevel);
            save.Current = new() { Id = next, RegionId = _world.TerrainId, TStones = stones, ExpWeight = weight };
            SaveHuntingMission();
        }
        ActivateFactionLevel(FactionType.TheFirm, 1);
        Send(BuildFactionsMessage());
        Send(BuildMissionInfosReply(), seq);
        EnsureSafehouseHuntTarget();
    }

    private MissionInfos BuildSafehouseMissionInfos()
    {
        var current = _context.SafehouseMissions?.Current;
        var missions = Array.Empty<Mission>();
        if (current != null && SafehouseHuntType(current.Id) != 0 &&
            _context.SafehouseMissions.Completed?.Contains(current.Id) != true)
        {
            string target = current.Id == "sh_sq_01" ? "Compsognathus" : "Raptor covarde";
            missions = new[] { new Mission
            {
                Id = current.Id, RegionId = current.RegionId, Faction = FactionType.TheFirm,
                Subject = "Missão de caça: " + target,
                Description = "Cace 1 " + target + " na ilha do abrigo. A missão termina automaticamente após o abate.",
                StartedAt = current.StartedAt,
                Todos = new[] { new MissionToDo
                {
                    Id = SafehouseHuntTodo(current.Id), Type = MissionTodoType.Hunt,
                    Order = MissionTodoOrder.StartAfterPrev, Progress = Math.Clamp(current.Progress, 0, 1),
                    GoalCount = 1, Label = "Caçar " + target,
                    Tooltip = "Somente abates realizados após aceitar esta missão contam."
                } },
                Reward = SafehouseHuntingReward(current)
            } };
        }
        return new MissionInfos
        {
            Missions = missions, MissionActivatesAt = new(),
            RecommendFailReasons = IsSafehouse && missions.Length == 0 &&
                _context.SafehouseMissions?.Completed?.Contains("sh_sq_02") == true
                ? new() { [FactionType.TheFirm] = "As duas missões de caça foram concluídas." } : null,
            ShuffleCount = 0, ShuffleAt = null
        };
    }

    private RewardInfo SafehouseHuntingReward(SafehouseMissionProgress mission) => new()
    {
        Currency = new() { [Shared.Economy.Currency.TStone] =
            Math.Min(Math.Max(0, mission.TStones), Math.Max(0, MaxCurrencyBalance - TStone)) },
        Exp = Math.Min(Math.Max(0, ExpCap() - (_skills?.Exp ?? 0)), PreviewActionExp(mission.ExpWeight))
    };

    private void AcceptSafehouseMission(AcceptMission msg, uint seq)
    {
        if (!ValidateMissionRadio(msg.EntityId, msg.Tile, seq)) return;
        var current = _context.SafehouseMissions?.Current;
        if (!IsSafehouse || current == null || current.Id != msg.MissionId ||
            current.RegionId != _world.TerrainId || SafehouseHuntType(current.Id) == 0 ||
            HuntingSave.Completed.Contains(current.Id))
        {
            Send(new Abort { Text = "Solicite a missão de caça atual nesta tenda antes de aceitá-la." }, seq);
            return;
        }
        // Repetir o pacote não reinicia progresso nem o tempo de espera para pular.
        if (!current.StartedAt.HasValue)
        {
            current.StartedAt = Gauge.CurrentTime;
            SaveHuntingMission();
        }
        Send(BuildMissionInfosReply(), seq);
        EnsureSafehouseHuntTarget();
    }

    private void CancelSafehouseMission(string id)
    {
        if (_context.SafehouseMissions?.Current is not { } current || current.Id != id) return;
        _context.SafehouseMissions.Current = null;
        SaveHuntingMission();
    }

    private void NoteSafehouseHuntingEvent(QuestEventType ev, QuestEventContext action)
    {
        var current = _context.SafehouseMissions?.Current;
        if (ev != QuestEventType.Hunted || action == null || !IsSafehouse ||
            current?.StartedAt == null || current.RegionId != _world.TerrainId ||
            action.EntityType != SafehouseHuntType(current.Id)) return;
        CompleteSafehouseHuntingMission(current);
    }

    private void CompleteSafehouseHuntingMission(SafehouseMissionProgress current)
    {
        if (!HuntingSave.Completed.Add(current.Id)) return;
        var reward = SafehouseHuntingReward(current);
        // Marca a conclusão antes de creditar. As rotinas de saldo/EXP salvam o mesmo
        // PlayerContext; cancelar, reconectar ou repetir eventos não duplica o prêmio.
        current.Progress = 1;
        HuntingSave.Current = null;
        AddTStone(reward.Currency[Shared.Economy.Currency.TStone], "Missão " + current.Id);
        AddExpForAction(current.ExpWeight, null, "Missão " + current.Id);
        SaveHuntingMission();
        Send(new Rewarded { Effect = new MissionCompletedEffect
            { Type = Shared.System.RewardEffect.MissionCompleted, MissionId = current.Id,
                FactionType = FactionType.TheFirm }, Reward = reward });
        Send(BuildMissionInfosReply());
    }

    private void SkipSafehouseHuntingMission(string id, uint seq)
    {
        var current = _context.SafehouseMissions?.Current;
        var config = Durango.Utils.Json.ReadFromFile<Newtonsoft.Json.Linq.JObject>("constants")?
            ["skip_tutorial_missions"]?[id ?? ""];
        int wait = (int?)config?["skip_time"] ?? 0;
        if (!IsSafehouse || !_context.AppearPlayer.IsAlive || current?.Id != id ||
            current?.StartedAt == null || current.RegionId != _world.TerrainId || wait <= 0 ||
            Gauge.CurrentTime - current.StartedAt.Value < wait)
        {
            Send(new Abort { Text = "Esta missão só pode ser pulada após o tempo de espera do tutorial." }, seq);
            return;
        }
        CompleteSafehouseHuntingMission(current);
    }

    private void EnsureSafehouseHuntTarget()
    {
        var current = _context.SafehouseMissions?.Current;
        if (!IsSafehouse || current?.StartedAt == null || current.RegionId != _world.TerrainId) return;
        ushort type = SafehouseHuntType(current.Id);
        if (type == 0 || _world.AnimalManager.All.Any(a => a.EntityType == type && a.IsAlive && !a.Captured)) return;
        // Usa um ponto de fauna nativo já existente, longe do rádio. Somente um alvo
        // compartilhado é reposto quando falta a espécie exigida pelo tutorial.
        var habitat = _world.AnimalManager.All.FirstOrDefault(a => a.EntityType == 2015 && a.IsAlive && !a.Captured);
        if (habitat == null) return;
        var target = _world.AnimalManager.SpawnAt(type, Math.Max(1, habitat.CombatLevel), habitat.HomeTile);
        if (target != null) _world.BroadCast(target.ToMessage());
    }
}
