using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Economy;
using Shared.Quest;
using Yaml.Util;
using State = Shared.Quest.QuestState;

namespace DurangoServerNx;

// Exercita pagamentos, progresso e persistência através do TCP/MessagePack real.
internal static class QuestRewardCheck
{
    private static int _passed;
    private static void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        _passed++; Console.WriteLine("[quest-rewards-check] OK " + text);
    }
    private static int Exp(Player player) => ((SkillSave)typeof(Player)
        .GetField("_skills", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(player)).Exp;
    private static QuestRewardResults Claim(EconomyProtocolCheck.Link link, string id)
    {
        int before = link.Messages.OfType<QuestRewardResults>().Count();
        link.Send(new RequestQuestReward { QuestId = id });
        link.PumpUntil(() => link.Messages.OfType<QuestRewardResults>().Count() > before);
        return link.Messages.OfType<QuestRewardResults>().Last();
    }
    private static State StateOf(EconomyProtocolCheck.Link link, string id) => link
        .Request<GetQuestState, Messages.QuestState>(new GetQuestState { QuestIds = new[] { id } }).States[id];

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-quest-rewards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" };
            wc.Initialize(Path.Combine(root, "world"));
            var world = new World(wc);
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "quest-tester", PlayerName = "Tester", PlayerLevel = 10 } };
            string savePath = Path.Combine(root, "tester.player");
            context.Initialize(savePath); context.AppearPlayer.Level = 10; context.AppearPlayer.IsAlive = true;
            const string daily = "daily_gathering_a_01", first = "permanent_gathering_any_01", second = "permanent_gathering_any_02";
            using (var link = new EconomyProtocolCheck.Link(context, world, null))
            {
                link.Player.ContextChanged += context.Save;
                int level10Exp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { 10 });
                link.Player.AddExp(level10Exp - Exp(link.Player), "cheat");
                var d = link.Request<GetQuests, Quests>(new GetQuests { Category = "daily" }).Todos.Single(q => q.Id == daily);
                Check(d.Reward?.Exp > 0 && d.Reward.Value.Currency[Currency.TStone] > 0,
                    "cliente recebe valores de EXP e moedas T antes do resgate");
                var achievements = link.Request<GetQuests, Quests>(new GetQuests { Category = "permanent" });
                Check(achievements.Todos.Length >= 100 && achievements.Todos.All(q => QuestCatalog.Find(q.Id).IsLive),
                    "conquistas funcionais aparecem e objetivos ainda sem mecânica ficam fora da lista");
                string firstLevel = QuestCatalog.InCategory("permanent").Where(q => q.IsLive && q.LevelCategory == -1)
                    .OrderBy(q => q.GoalCount).First().Id;
                Check(StateOf(link, firstLevel) == State.ReachTheGoal,
                    "nível existente do personagem conta nas conquistas");
                long coins = context.TStone; int exp = Exp(link.Player);
                link.Request<RequestQuestReward, Abort>(new RequestQuestReward { QuestId = daily });
                Check(context.TStone == coins && Exp(link.Player) == exp, "objetivo incompleto não paga recompensa");
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Carcass, 20);
                Check(StateOf(link, daily) == State.WorkInProgress, "carcaça não conta como coleta comum");
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 20);
                var reward = Claim(link, daily).Reward.Value;
                Check(reward.Currency[Currency.TStone] == d.Reward.Value.Currency[Currency.TStone] &&
                    context.TStone - coins == reward.Currency[Currency.TStone] && Exp(link.Player) - exp == reward.Exp,
                    "pagamento real coincide com EXP e moedas anunciados no protocolo");
                coins = context.TStone; exp = Exp(link.Player);
                link.Request<RequestQuestReward, Abort>(new RequestQuestReward { QuestId = daily });
                Check(context.TStone == coins && Exp(link.Player) == exp, "resgate duplicado não repete moedas nem EXP");
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 30);
                Check(StateOf(link, first) == State.ReachTheGoal, "cinquenta coletas concluem a primeira conquista");
                var tier1 = Claim(link, first).Reward.Value;
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 50);
                Check(StateOf(link, second) == State.ReachTheGoal, "progresso acumulado alcança a segunda etapa");
                var tier2 = Claim(link, second).Reward.Value;
                Check(tier1.Currency[Currency.TStone] == 250 && tier2.Currency[Currency.TStone] == 500 && tier2.Exp > tier1.Exp,
                    "etapas maiores da conquista pagam mais moedas e EXP");
                context.Save(); Check(SafeSave.FlushPending(), "estado e carteira gravados no disco");
            }
            var loaded = Json.Read<PlayerContext>(File.ReadAllText(savePath));
            loaded.Initialize(savePath);
            using (var link = new EconomyProtocolCheck.Link(loaded, world, null))
            {
                link.Player.ContextChanged += loaded.Save;
                Check(StateOf(link, daily) == State.Finished && StateOf(link, first) == State.Finished &&
                    StateOf(link, second) == State.Finished, "reconexão preserva resgates diários e permanentes");
                long coins = loaded.TStone; int exp = Exp(link.Player);
                link.Request<RequestQuestReward, Abort>(new RequestQuestReward { QuestId = first });
                Check(loaded.TStone == coins && Exp(link.Player) == exp, "reconectar não libera recompensa já paga");
                loaded.QuestDailyResetDay = "2000-01-01";
                Check(StateOf(link, daily) == State.WorkInProgress && StateOf(link, first) == State.Finished,
                    "virada do dia reinicia diárias e preserva conquistas");
                loaded.TStone = 99_999_989;
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 20);
                var capped = Claim(link, daily).Reward.Value;
                Check(loaded.TStone == 99_999_999 && capped.Currency[Currency.TStone] == 10,
                    "limite da carteira é respeitado e recibo informa valor efetivamente recebido");
                link.Request<RequestQuestReward, Abort>(new RequestQuestReward { QuestId = "advisor_combat_onehand_master" });
                Check(loaded.TStone == 99_999_999, "missão não implementada não libera moedas");
            }
            var dailyDef = QuestCatalog.Find(daily);
            Check(QuestRewardTuning.For(dailyDef, 1).TStones == 100 && QuestRewardTuning.For(dailyDef, 40).TStones == 800 &&
                QuestRewardTuning.For(dailyDef, 60).TStones == 1200, "pagamentos diários acompanham nível do personagem");
            foreach (var chain in QuestCatalog.InCategory("permanent").Where(d => d.IsLive)
                .GroupBy(d => System.Text.RegularExpressions.Regex.Replace(d.Id, @"_\d+$", "")))
            {
                var rewards = chain.OrderBy(d => d.GoalCount).Select(d => QuestRewardTuning.For(d, 10)).ToArray();
                Check(rewards.Zip(rewards.Skip(1)).All(pair => pair.First.TStones <= pair.Second.TStones &&
                    pair.First.ExpWeight <= pair.Second.ExpWeight), "progressão crescente: " + chain.Key);
            }
            Console.WriteLine($"[quest-rewards-check] {_passed} verificações aprovadas");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
}
