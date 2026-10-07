using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Quest;
using Yaml.Util;
using State = Shared.Quest.QuestState;

namespace DurangoServerNx;

internal static class QuestReactivationCheck
{
    static int passed;
    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); passed++; Console.WriteLine("[quest-reactivation-check] OK " + message); }
    static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    static SkillSave Skills(Player player) => (SkillSave)typeof(Player).GetField("_skills", InstanceFlags).GetValue(player);
    static State QuestState(EconomyProtocolCheck.Link link, string id) =>
        link.Request<GetQuestState, Messages.QuestState>(new GetQuestState { QuestIds = new[] { id } }).States[id];
    static void RefreshSkills(Player player) => typeof(Player).GetMethod("SaveSkillState", InstanceFlags).Invoke(player, null);

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-quest-reactivation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets"); TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "world")); var world = new World(wc);
            var pc = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "quest-reactivation-tester", PlayerName = "Tester", PlayerLevel = 10 } };
            string savePath = Path.Combine(root, "tester.player"); pc.Initialize(savePath); pc.AppearPlayer.IsAlive = true;
            CheckDefinitions();
            CheckRealHunting(root);
            string courseId = "combat_basic_1"; var course = LearningGuideCatalog.Courses[courseId];
            string[] inventoryIds;
            using (var link = new EconomyProtocolCheck.Link(pc, world, null))
            {
                link.Player.ContextChanged += pc.Save;
                var skills = Skills(link.Player);
                var findNode = typeof(Player).GetMethod("FindNode", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var (id, def) in LearningGuideCatalog.Courses)
                {
                    foreach (var skill in def.Skills)
                        Check(findNode.Invoke(null, new object[] { skill.SkillId, skill.SubId, skill.Level, -1 }) != null, id + " possui skill válida: " + skill.SkillId + "/" + skill.SubId);
                    foreach (var reward in def.RewardItems)
                        Check(Cheats.MakeItem(reward.PrototypeId, reward.Level).HasValue && reward.Count > 0, id + " possui item de recompensa válido");
                    Check(LearningGuideCatalog.Titles.ContainsKey(def.RewardTitleId), id + " possui título de recompensa válido");
                }
                link.Request<SelectTargetTitle, Abort>(new SelectTargetTitle { TitleId = "curso-inexistente" });
                link.Request<SelectTargetTitle, Abort>(new SelectTargetTitle { TitleId = "combat_melee_onehand_2" });
                Check(pc.LearningGuide.Target == null, "seleção inválida e curso bloqueado não alteram o alvo");
                int beforeTarget = link.Messages.OfType<TargetTitle>().Count();
                link.Send(new SelectTargetTitle { TitleId = courseId });
                link.PumpUntil(() => link.Messages.OfType<TargetTitle>().Count() > beforeTarget);
                Check(pc.LearningGuide.Target == courseId && pc.LearningGuide.SelectedOnce, "seleção válida persiste o curso");
                Check(QuestState(link, "advisor_selected") == State.ReachTheGoal, "iniciar guia conclui a conquista correspondente");
                var advised = link.Request<GetSkills, Messages.Skills>(default);
                Check(advised.AdvisedSkills.Length == course.Skills.Length, "cliente recebe as skills indicadas pelo curso");
                Check(link.Request<GetFactions, Factions>(default)._Factions.Any(f => f.Type == Shared.Faction.FactionType.Lama && f.Level > 0), "guia acessível pelo desbloqueio de Lama existente");
                link.Request<ReceiveAdvisorReward, Abort>(new ReceiveAdvisorReward { TitleId = courseId });
                var targets = link.Request<GetAdvisorTargets, AdvisorTargets>(default);
                Check(targets.Titles[courseId] < 1 && !targets.RemainingRewards.Contains(courseId), "curso incompleto não oferece recompensa");
                link.Send(new UpdateAdvisorProgress { Progress = new Pair<int, int>(999, 999) });
                targets = link.Request<GetAdvisorTargets, AdvisorTargets>(default);
                Check(!pc.LearningGuide.Completed.Contains(courseId), "contador forjado pelo cliente não conclui curso");
                foreach (var skill in course.Skills)
                {
                    if (!skills.Learned.TryGetValue(skill.SkillId, out var levels)) skills.Learned[skill.SkillId] = levels = new();
                    levels[skill.SubId] = Math.Max(skill.Level, levels.GetValueOrDefault(skill.SubId));
                }
                RefreshSkills(link.Player);
                targets = link.Request<GetAdvisorTargets, AdvisorTargets>(default);
                Check(targets.Titles[courseId] == 1 && targets.RemainingRewards.Contains(courseId), "skills já aprendidas concluem o curso");
                Check(QuestState(link, "advisor_achieved") == State.ReachTheGoal, "conclusão do curso avança conquista do guia");
                link.Request<CancelTargetTitle, OK>(default);
                Check(pc.LearningGuide.Target == null && targets.RemainingRewards.Contains(courseId), "cancelar seleção preserva conclusão e recompensa pendente");
                var filler = Cheats.MakeItem("stone", 1).Value; filler.Size = 100000; pc.InventoryItems.Add(filler);
                link.Request<ReceiveAdvisorReward, Abort>(new ReceiveAdvisorReward { TitleId = courseId });
                Check(!pc.LearningGuide.Claimed.Contains(courseId) && pc.LearningGuide.Points == 0, "mochila cheia não consome a recompensa nem credita pontos");
                pc.InventoryItems.RemoveAll(i => i.Id == filler.Id);
                string otherCourse = LearningGuideCatalog.Courses.First(p => p.Key != courseId && p.Value.RequiredSkill.Level == 1).Key;
                int beforeSelection = link.Messages.OfType<TargetTitle>().Count();
                link.Send(new SelectTargetTitle { TitleId = otherCourse });
                link.PumpUntil(() => link.Messages.OfType<TargetTitle>().Count() > beforeSelection);
                int beforeItems = pc.InventoryItems.Count;
                byte[] learnedBefore = pc.Storage[SkillTuning.StorageKey].ToArray();
                link.Request<ReceiveAdvisorReward, OK>(new ReceiveAdvisorReward { TitleId = courseId });
                Check(pc.InventoryItems.Count == beforeItems + course.RewardItems.Sum(r => r.Count), "resgate entrega quantidades originais dos itens");
                Check(pc.LearningGuide.Points == course.RewardPoint && pc.LearningGuide.Claimed.Contains(courseId), "resgate persiste pontos e trava repetição");
                Check(pc.Storage[SkillTuning.StorageKey].SequenceEqual(learnedBefore), "resgate preserva skills e SP existentes");
                Check(link.Request<GetTitles, Titles>(default).TitleIds.Contains(course.RewardTitleId), "título desbloqueado chega ao cliente");
                int okIndex = link.Messages.FindLastIndex(m => m is OK);
                Check(link.Messages.FindLastIndex(m => m is TargetTitle) > okIndex,
                    "curso ativo restaurado após o callback de resgate do cliente original");
                Check(link.Request<GetTargetTitle, TargetTitle>(default).TitleId == otherCourse, "resgatar outro curso preserva a seleção atual");
                link.Request<ReceiveAdvisorReward, Abort>(new ReceiveAdvisorReward { TitleId = courseId });
                Check(pc.InventoryItems.Count == beforeItems + course.RewardItems.Sum(r => r.Count) && pc.LearningGuide.Points == course.RewardPoint, "resgate duplicado não concede itens ou pontos");
                link.Send(new SelectTitle { TitleId = course.RewardTitleId });
                link.PumpUntil(() => pc.AppearPlayer.Title.TitleId == course.RewardTitleId);
                link.Request<SelectTitle, Abort>(new SelectTitle { TitleId = "combat_melee_onehand_3" });
                Check(pc.AppearPlayer.Title.TitleId == course.RewardTitleId, "apenas título realmente recebido pode ser equipado");
                var survival = (SurvivalState)typeof(Player).GetField("_survival", InstanceFlags).GetValue(link.Player);
                var stats = link.Messages.OfType<Statistics>().Last();
                Check(Math.Abs(stats.DerivedsAbilities[Shared.Ability.Derived.MaxHealth] - survival.MaxOf(SurvivalState.KeyHealth, Gauge.CurrentTime)) < 0.01,
                    "bônus de vida do título coincide com o limite real, sem duplicação");
                Check(Math.Abs(stats.DerivedsAbilities[Shared.Ability.Derived.MaxEnergy] - survival.MaxOf(SurvivalState.KeyEnergy, Gauge.CurrentTime)) < 0.01,
                    "bônus de energia do título coincide com o limite real");

                const string biomeQuest = "daily_hunting_d_01";
                link.Player.NoteQuestEvent(QuestEventType.Hunted, context: new QuestEventContext { Biome = 1, Role = 4, EntityType = 2001 });
                Check(QuestState(link, biomeQuest) == State.WorkInProgress, "caça tropical não avança diária temperada");
                link.Player.NoteQuestEvent(QuestEventType.Hunted, amount: 5, context: new QuestEventContext { Biome = 0, Role = 4, EntityType = 2001 });
                Check(QuestState(link, biomeQuest) == State.ReachTheGoal && QuestState(link, "permanent_hunt_raptor_01") == State.ReachTheGoal,
                    "caça válida avança diária por bioma e conquista da espécie");
                int rewards = link.Messages.OfType<QuestRewardResults>().Count(); link.Send(new RequestQuestReward { QuestId = biomeQuest });
                link.PumpUntil(() => link.Messages.OfType<QuestRewardResults>().Count() > rewards);
                Check(QuestState(link, biomeQuest) == State.Finished, "diária por bioma conclui e paga pelo protocolo real");
                link.Request<RequestQuestReward, Abort>(new RequestQuestReward { QuestId = biomeQuest });
                inventoryIds = pc.InventoryItems.Select(i => i.Id).OrderBy(i => i).ToArray();
                pc.Save(); Check(SafeSave.FlushPending(), "progresso e resgates gravados em save temporário");
            }
            Check(SafeSave.FlushPending(), "save de desconexão concluído");
            var loaded = Json.Read<PlayerContext>(File.ReadAllText(savePath)); loaded.Initialize(savePath);
            using (var link = new EconomyProtocolCheck.Link(loaded, world, null))
            {
                link.Player.ContextChanged += loaded.Save;
                var targets = link.Request<GetAdvisorTargets, AdvisorTargets>(default);
                Check(targets.Titles[courseId] == 1 && !targets.RemainingRewards.Contains(courseId), "reconexão conserva curso concluído e recompensa recebida");
                Check(loaded.LearningGuide.Target != null && loaded.AppearPlayer.Title.TitleId == course.RewardTitleId,
                    "reconexão conserva seleção de curso e título equipado");
                link.Request<ReceiveAdvisorReward, Abort>(new ReceiveAdvisorReward { TitleId = courseId });
                Check(loaded.LearningGuide.Points == course.RewardPoint && loaded.InventoryItems.Select(i => i.Id).OrderBy(i => i).SequenceEqual(inventoryIds), "reconexão não duplica itens nem pontos");
                Check(QuestState(link, "daily_hunting_d_01") == State.Finished, "reconexão conserva diária resgatada");
            }
            Check(SafeSave.FlushPending(), "save final concluído");
            Console.WriteLine($"[quest-reactivation-check] {passed} verificações aprovadas"); return 0;
        }
        catch (Exception error) { Console.WriteLine("[quest-reactivation-check] FAIL " + error); return 1; }
        finally { SafeSave.FlushPending(); try { Directory.Delete(root, true); } catch { } }
    }

    static void CheckDefinitions()
    {
        Check(QuestCatalog.ParseGoal("Caçar 1.000 animais.") == 1000 && QuestCatalog.ParseGoal("Coletar 1,000 vezes.") == 1000,
            "tradução preserva metas com separadores de milhar");
        Check(QuestCatalog.Find("permanent_hunt_any_animal_06").GoalCount == 1000 && QuestCatalog.Find("permanent_gathering_any_04").GoalCount == 1000,
            "metas antigas de mil ações preservadas");
        Check(QuestCatalog.InCategory("daily").Count(q => q.IsLive) == 22, "22 diárias habilitadas, mantendo facções e PvP fora do escopo");
        Check(LearningGuideCatalog.Courses.Count == 58, "58 cursos originais carregados");
        foreach (var def in QuestCatalog.Live.Where(d => d.Objective != null))
        {
            Check(!QuestCatalog.Matches(def, def.Event, def.Filter), def.Id + " não avança sem metadata validada");
            var objective = def.Objective;
            if (!string.IsNullOrEmpty(objective.Snapshot)) continue;
            var context = new QuestEventContext { Biome = objective.Biome, Role = objective.Role,
                EntityType = objective.EntityTypes?.FirstOrDefault() ?? -1, RecipeId = objective.Recipes?.FirstOrDefault(),
                Products = objective.Prototypes?.Select(p => new Item { Prototype = p }).ToArray() };
            Check(QuestCatalog.Matches(def, def.Event, def.Filter, context), def.Id + " aceita o alvo correto");
            if (objective.Biome >= 0)
            {
                for (int biome = 0; biome < 8; biome++)
                { context.Biome = biome; Check(QuestCatalog.Matches(def, def.Event, def.Filter, context) == (biome == objective.Biome), def.Id + " filtra bioma " + biome); }
                context.Biome = objective.Biome;
            }
            if (objective.Role >= 0) { context.Role = 9; Check(!QuestCatalog.Matches(def, def.Event, def.Filter, context), def.Id + " rejeita ilha particular"); }
            if (objective.Recipes?.Length > 0) { context.RecipeId = "receita-incorreta"; Check(!QuestCatalog.Matches(def, def.Event, def.Filter, context), def.Id + " rejeita receita incorreta"); }
            if (objective.EntityTypes?.Length > 0) { context.EntityType = 65535; Check(!QuestCatalog.Matches(def, def.Event, def.Filter, context), def.Id + " rejeita espécie incorreta"); }
            if (objective.Prototypes?.Length > 0) { context.Products = new[] { new Item { Prototype = "produto-incorreto" } }; Check(!QuestCatalog.Matches(def, def.Event, def.Filter, context), def.Id + " rejeita produto incorreto"); }
        }
    }

    static void CheckRealHunting(string root)
    {
        foreach (var terrain in new[] { "pe10gr_1", "ri35te", "ri40tr" })
        {
            var wc = new WorldContext { TerrainId = terrain }; wc.Initialize(Path.Combine(root, terrain + ".world"));
            var world = new World(wc);
            var pc = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "hunt-" + terrain, PlayerName = "Tester", PlayerLevel = 10 } };
            pc.Initialize(Path.Combine(root, terrain + ".player")); pc.AppearPlayer.IsAlive = true;
            using var link = new EconomyProtocolCheck.Link(pc, world, null);
            pc.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
                Path = new[] { new Location { Position = new WorldPosition(world.EntryPoint.x * 200, world.EntryPoint.y * 200), Time = Gauge.CurrentTime } } } };
            var attack = typeof(Player).GetMethod("TryAttackAnimal", InstanceFlags);
            for (int i = 0; i < 5; i++)
            {
                var animal = world.AnimalManager.SpawnAt(2001, 1, world.EntryPoint); animal.Life = 1;
                attack.Invoke(link.Player, new object[] { animal.EntityId, new BattleAttackInfo { damage_bonus = 1 }, Gauge.CurrentTime });
                Check(!animal.IsAlive, terrain + " abate real concluído");
                Check(!(bool)attack.Invoke(link.Player, new object[] { animal.EntityId, new BattleAttackInfo { damage_bonus = 1 }, Gauge.CurrentTime }),
                    terrain + " animal morto não pode gerar novo abate");
            }
            Check(QuestState(link, "daily_hunting_d_01") == (terrain == "ri35te" ? State.ReachTheGoal : State.WorkInProgress),
                terrain + " handler real de caça aplica bioma e tipo da ilha");
            Check(QuestState(link, "daily_hunting_e_02") == State.WorkInProgress,
                terrain + " ilha particular ou outro bioma não avança caça em pradaria instável");
            Check(QuestState(link, "permanent_hunt_raptor_01") == State.ReachTheGoal,
                terrain + " abate real avança a espécie correta");
        }
    }
}
