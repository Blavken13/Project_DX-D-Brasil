using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Faction;
using Shared.Quest;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SafehouseMissionCheck
{
    private static int passed;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); passed++; Console.WriteLine("[safehouse-missions-check] OK " + message); }
    private static void Place(PlayerContext pc, Point2 tile) => pc.AppearPlayer.Move.Movements = new[]
        { new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[]
            { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
    private static void Kill(EconomyProtocolCheck.Link link, PlayerContext pc, World world, ushort type)
    {
        var animal = world.AnimalManager.SpawnAt(type, 1, world.EntryPoint); animal.Life = 1;
        Place(pc, animal.HomeTile);
        var method = typeof(Player).GetMethod("TryAttackAnimal", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(link.Player, new object[] { animal.EntityId, new BattleAttackInfo { damage_bonus = 1 }, Gauge.CurrentTime });
        Check(!animal.IsAlive, "abate real de " + type);
        Check(!(bool)method.Invoke(link.Player, new object[] { animal.EntityId, new BattleAttackInfo { damage_bonus = 1 }, Gauge.CurrentTime }),
            "animal morto não produz outro abate");
        link.Request<GetMissions, MissionInfos>(default); // Drena as notificações do abate.
    }
    private static PlayerContext NewPlayer(string root, string name)
    {
        var pc = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = name, PlayerName = name, PlayerLevel = 5 } };
        pc.Initialize(Path.Combine(root, name + ".player")); pc.AppearPlayer.IsAlive = true;
        return pc;
    }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-safehouse-missions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets"); TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "grass_company_safehouse_01" };
            wc.Initialize(Path.Combine(root, "safehouse.world")); var world = new World(wc);
            Check(world.AnimalManager.All.Count(a => a.EntityType == 2051) == WorldTuning.SafehouseRaptorCount,
                "Safehouse inicia com os raptores medrosos configurados");
            Check(!world.AnimalManager.All.Any(a => a.EntityType == 2027) &&
                world.AnimalManager.All.Count(a => a.EntityType == 2015) == WorldTuning.SafehouseCompsognathusCount,
                "raptores substituem os Sebrossaurus e preservam os compsognatos");
            var radio = world.ArtifactManager.Enumerable(a => a.EntityType == 9100).First();
            var offer = new RecommendMissions { EntityId = radio.EntityId, Tile = radio.Tile };
            var pc = NewPlayer(root, "hunter");
            long firstReward;
            double started;
            using (var link = new EconomyProtocolCheck.Link(pc, world, null))
            {
                Check(link.Request<GetMissions, MissionInfos>(default).Missions.Length == 0, "login não aceita missão automaticamente");
                Place(pc, radio.Tile);
                var menu = link.Request<Touch, Touched>(new Touch { EntityId = radio.EntityId, EntityType = radio.EntityType, Tile = radio.Tile });
                Check(menu.Interactions.Contains((int)Shared.System.Interaction.AcceptMission), "rádio oferece interação de missão");
                link.Request<RecommendMissions, Abort>(new RecommendMissions { EntityId = "forged", Tile = radio.Tile });
                link.Request<RecommendMissions, Abort>(new RecommendMissions { EntityId = radio.EntityId, Tile = new Point2(0, 0) });
                Check(pc.SafehouseMissions == null, "rádio e coordenadas falsos não criam missão");
                Place(pc, new Point2(radio.Tile.x + 50, radio.Tile.y + 50));
                link.Request<RecommendMissions, Abort>(offer);
                Check(pc.SafehouseMissions == null, "pedido remoto não cria missão");
                Place(pc, radio.Tile);
                var first = link.Request<RecommendMissions, MissionInfos>(offer).Missions.Single();
                Check(first.Id == "sh_sq_01" && first.Todos.Single().Id == "hunt_sh_2015", "IDs originais da primeira caça");
                Check(first.StartedAt == null && first.Todos.Single().GoalCount == 1, "missão oferecida ainda não está ativa");
                Check(first.RegionId == world.TerrainId && first.Faction == FactionType.TheFirm, "região e facção compatíveis com o roteiro");
                Check(link.Request<GetFactions, Factions>(default)._Factions.Any(f => f.Type == FactionType.TheFirm && f.Level > 0), "facção disponível na interface");
                firstReward = first.Reward.Value.Currency[Shared.Economy.Currency.TStone];
                Check(firstReward > 0, "prêmio usa a tabela brasileira existente");
                long before = pc.TStone;
                Kill(link, pc, world, 2015);
                Check(pc.TStone == before && !pc.SafehouseMissions.Completed.Contains(first.Id), "caça anterior à aceitação não vale");
                Place(pc, radio.Tile);
                link.Request<AcceptMission, Abort>(new AcceptMission { EntityId = radio.EntityId, Tile = radio.Tile, MissionId = "sh_sq_02" });
                var accept = new AcceptMission { EntityId = radio.EntityId, Tile = radio.Tile, MissionId = first.Id };
                pc.AppearPlayer.IsAlive = false;
                link.Request<AcceptMission, Abort>(accept); pc.AppearPlayer.IsAlive = true;
                Check(pc.SafehouseMissions.Current.StartedAt == null, "jogador morto não aceita missão");
                var active = link.Request<AcceptMission, MissionInfos>(accept).Missions.Single();
                started = active.StartedAt.Value;
                Check(link.Request<AcceptMission, MissionInfos>(accept).Missions.Single().StartedAt == started, "aceitação repetida preserva início");
                Kill(link, pc, world, 2027);
                Check(!pc.SafehouseMissions.Completed.Contains(first.Id), "espécie errada não conclui caça");
                Kill(link, pc, world, 2015);
                Check(pc.SafehouseMissions.Completed.Contains(first.Id) && pc.SafehouseMissions.Current == null, "abate correto conclui a missão");
                Check(pc.TStone == before + firstReward, "recompensa creditada uma vez");
                Check(link.Messages.OfType<Rewarded>().Count(r => r.Effect is MissionCompletedEffect e && e.MissionId == first.Id) == 1,
                    "evento original libera after_hunting_flow no cliente");
                Kill(link, pc, world, 2015);
                Check(pc.TStone == before + firstReward, "outros abates não repetem a recompensa");
                Check(link.Request<CheckSequenceMissionCleared, SequenceMissionCleared>(new() { MissionId = first.Id }).Cleared, "consulta confirma conclusão");
                Place(pc, radio.Tile);
                var second = link.Request<RecommendMissions, MissionInfos>(offer).Missions.Single();
                Check(second.Id == "sh_sq_02" && second.Todos.Single().Id == "hunt_05tr_2051", "segunda caça segue o roteiro");
                link.Request<CancelMission, OK>(new() { MissionId = "unknown" });
                Check(pc.SafehouseMissions.Current.Id == second.Id, "cancelamento desconhecido não apaga missão");
                link.Request<CancelMission, OK>(new() { MissionId = second.Id });
                Check(pc.SafehouseMissions.Current == null && pc.SafehouseMissions.Completed.Contains(first.Id), "cancelamento preserva conclusão anterior");
                Check(link.Request<RecommendMissions, MissionInfos>(offer).Missions.Single().Id == second.Id, "cancelamento permite receber a mesma etapa");
                link.Request<AcceptMission, MissionInfos>(new() { EntityId = radio.EntityId, Tile = radio.Tile, MissionId = second.Id });
                int targets = world.AnimalManager.All.Count(a => a.EntityType == 2051 && a.IsAlive && !a.Captured);
                Check(targets > 0, "segunda caça possui raptor covarde disponível");
                link.Request<GetMissions, MissionInfos>(default);
                Check(world.AnimalManager.All.Count(a => a.EntityType == 2051 && a.IsAlive && !a.Captured) == targets, "consulta não multiplica os alvos");
                link.Request<SkipTutorialMission, Abort>(new() { MissionId = second.Id });
                Check(pc.SafehouseMissions.Current != null, "não permite pular antes de quatro minutos");
                Check(SafeSave.FlushPending(), "save persistido antes da reconexão");
            }
            pc = PlayerContext.Load(pc.Path);
            Check(pc.SafehouseMissions.Completed.Contains("sh_sq_01") && pc.SafehouseMissions.Current.Id == "sh_sq_02" &&
                pc.SafehouseMissions.Current.StartedAt.HasValue, "reconexão conserva conclusão e missão aceita");
            var wrongWc = new WorldContext { TerrainId = "pe10gr_1" }; wrongWc.Initialize(Path.Combine(root, "other.world"));
            using (var wrongLink = new EconomyProtocolCheck.Link(pc, new World(wrongWc), null))
            {
                wrongLink.Player.NoteQuestEvent(QuestEventType.Hunted, context: new QuestEventContext { EntityType = 2051 });
                Check(!pc.SafehouseMissions.Completed.Contains("sh_sq_02"), "abate em outra ilha não conclui missão");
            }
            using (var link = new EconomyProtocolCheck.Link(pc, world, null))
            {
                Check(link.Request<GetMissions, MissionInfos>(default).Missions.Single().Id == "sh_sq_02", "protocolo restaura missão em andamento");
                long before = pc.TStone;
                long prize = link.Request<GetMissions, MissionInfos>(default).Missions.Single().Reward.Value.Currency[Shared.Economy.Currency.TStone];
                Kill(link, pc, world, 2051);
                Check(pc.TStone == before + prize && pc.SafehouseMissions.Completed.Count == 2, "segunda caça conclui e paga corretamente");
                Check(link.Messages.OfType<Rewarded>().Any(r => r.Effect is MissionCompletedEffect e && e.MissionId == "sh_sq_02"), "segunda conclusão libera diálogo seguinte");
                Place(pc, radio.Tile);
                link.Request<AcceptMission, Abort>(new() { EntityId = radio.EntityId, Tile = radio.Tile, MissionId = "sh_sq_01" });
                link.Request<SkipTutorialMission, Abort>(new() { MissionId = "sh_sq_02" });
                Check(pc.TStone == before + prize, "repetição de aceitação ou skip não duplica prêmio");
                Check(link.Request<RecommendMissions, MissionInfos>(offer).Missions.Length == 0, "missões concluídas não são oferecidas novamente");
                Check(SafeSave.FlushPending(), "segunda conclusão gravada");
            }
            var reloaded = PlayerContext.Load(pc.Path);
            Check(reloaded.TStone == pc.TStone && reloaded.SafehouseMissions.Completed.Count == 2, "saldo e conclusões sobrevivem ao reload");
            var other = NewPlayer(root, "another-hunter");
            using (var link = new EconomyProtocolCheck.Link(other, world, null))
            {
                Place(other, radio.Tile);
                Check(link.Request<RecommendMissions, MissionInfos>(offer).Missions.Single().Id == "sh_sq_01", "progresso isolado por personagem");
                link.Request<AcceptMission, MissionInfos>(new() { EntityId = radio.EntityId, Tile = radio.Tile, MissionId = "sh_sq_01" });
                other.SafehouseMissions.Current.StartedAt = Gauge.CurrentTime - 241;
                long before = other.TStone;
                link.Send(new SkipTutorialMission { MissionId = "sh_sq_01" });
                link.PumpUntil(() => link.Messages.OfType<Rewarded>().Any(r => r.Effect is MissionCompletedEffect));
                Check(other.SafehouseMissions.Completed.Contains("sh_sq_01") && other.TStone > before, "skip nativo após espera conclui etapa sem travar tutorial");
            }
            Console.WriteLine("[safehouse-missions-check] PASS " + passed + " verificações. Saves temporários: " + root);
            return 0;
        }
        catch (Exception e) { Console.WriteLine("[safehouse-missions-check] FAIL " + e); return 1; }
        finally { SafeSave.FlushPending(); }
    }
}
