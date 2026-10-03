using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Attendance;
using Shared.Quest;
using Yaml.Util;
using State = Shared.Quest.QuestState;

namespace DurangoServerNx;

internal static class InductionRewardsCheck
{
    private static int _passed;
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine("[induction-check] OK " + message); _passed++;
    }
    private static QuestRewardResults Claim(EconomyProtocolCheck.Link link, string id)
    {
        int count = link.Messages.OfType<QuestRewardResults>().Count();
        link.Send(new RequestQuestReward { QuestId = id });
        link.PumpUntil(() => link.Messages.OfType<QuestRewardResults>().Count() > count);
        return link.Messages.OfType<QuestRewardResults>().Last();
    }
    private static QuestScoreInfos Scores(EconomyProtocolCheck.Link link) => link.Request<GetQuestScoreInfos, QuestScoreInfos>(new() { Category = "daily" });
    private static GiveAttendanceReward Attend(int day) => new() { Category = CategoryType.Event1, RewardNumber = day };

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-induction-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "world"));
            var world = new World(wc);
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "induction-tester", PlayerName = "Tester", PlayerLevel = 10 } };
            string savePath = Path.Combine(root, "tester.player"); context.Initialize(savePath);
            context.AppearPlayer.IsAlive = true;
            string dailyId = "daily_gathering_a_01";
            int claimedDay;
            using (var link = new EconomyProtocolCheck.Link(context, world, null))
            {
                link.Player.ContextChanged += context.Save;
                Check(InductionRewardTuning.Maximum == 240 && link.Player.InductionStones == 0, "save antigo inicia sem saldo inventado e conserva limite original 240");
                var initial = link.Messages.OfType<TodayAttendanceRewards>().Last().Rewards[CategoryType.Event1];
                Check(initial.RewardNumber == 0 && initial.NextAttendTime > Times.UnixTimeNow(), "calendario real anuncia primeira presenca e virada KST");
                var offered = link.Request<GetQuests, Quests>(new() { Category = "daily" }).Todos.Single(t => t.Id == dailyId).Reward.Value;
                Check(offered.Vouchers.Single().VoucherId == CrackTuning.VoucherId && offered.Vouchers.Single().Count == 2, "missao mostra pedra correta antes do resgate");
                link.Request<RequestQuestReward, Abort>(new() { QuestId = dailyId });
                Check(link.Player.InductionStones == 0, "missao incompleta nao entrega pedras");
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "daily", Score = 20 });
                Check(Scores(link).CurQuestScore == 0, "resgate sem pontos recusa e libera interface para nova consulta");
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 20);
                var paid = Claim(link, dailyId);
                Check(link.Player.InductionStones == 2 && paid.Reward.Value.Vouchers.Single().Count == 2 && paid.QuestScoreInfos.CurQuestScore == 10, "missao paga pedras, publica recibo e acumula pontos reais");
                link.Request<RequestQuestReward, Abort>(new() { QuestId = dailyId });
                Check(link.Player.InductionStones == 2, "pacote repetido nao duplica pagamento da missao");
                var second = QuestCatalog.InCategory("daily").First(d => d.IsLive && d.Event != QuestEventType.Collected);
                link.Player.NoteQuestEvent(second.Event, second.Filter, second.GoalCount);
                Claim(link, second.Id);
                var scores = Scores(link);
                Check(scores.CurQuestScore == 20 && scores.QuestScoreRewards.Single(s => s.QuestScore == 20).State == QuestScoreRewardState.Available, "duas diarias liberam bonus de pontos");
                context.Vouchers[CrackTuning.VoucherId] = 239;
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "daily", Score = 20 });
                Check(link.Player.InductionStones == 239 && Scores(link).QuestScoreRewards.Single(s => s.QuestScore == 20).State == QuestScoreRewardState.Available,
                    "carteira cheia conserva o bonus de pontos para resgate posterior");
                context.Vouchers[CrackTuning.VoucherId] = 4;
                var taken = link.Request<RequestQuestScoreReward, QuestScoreInfos>(new() { Category = "daily", Score = 20 });
                Check(link.Player.InductionStones == 6 && taken.QuestScoreRewards.Single(s => s.QuestScore == 20).State == QuestScoreRewardState.Taken, "bonus diario entrega duas pedras uma vez");
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "daily", Score = 20 });
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "permanent", Score = 20 });
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "daily", Score = int.MaxValue });
                Check(link.Player.InductionStones == 6, "duplicata, categoria indevida e pontuacao adulterada nao geram pedras");
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 30);
                var achievement = Claim(link, "permanent_gathering_any_01");
                Check(achievement.Reward.Value.Vouchers.Single().Count == 2 && link.Player.InductionStones == 8, "conquista tambem paga pedras junto de EXP e moedas T");

                var calendar = link.Request<GetAttendanceRewards, AttendanceRewards>(new() { Category = CategoryType.Event1 });
                Check(calendar.Rewards.Length == 28 && calendar.Rewards.All(r => r.Voucher.Value.VoucherId == CrackTuning.VoucherId) && calendar.Appendices.Length == 1, "cliente recebe calendario e bonus final com voucher nativo");
                Check(link.Request<GetAttendanceRewards, AttendanceRewards>(new() { Category = CategoryType.Monthly }).Rewards.Length == 0, "categoria nao publicada nao oferece pedras");
                link.Request<GiveAttendanceAppendix, Abort>(new() { Category = CategoryType.Event1, SelectedReward = 0 });
                link.Request<GiveAttendanceReward, Abort>(Attend(4));
                var restore = Attend(0); restore.IsRestore = true;
                link.Request<GiveAttendanceReward, Abort>(restore);
                Check(link.Player.InductionStones == 8, "dias futuros, reentrada e bonus prematuro sao recusados");
                context.Vouchers[CrackTuning.VoucherId] = 238;
                link.Request<GiveAttendanceReward, Abort>(Attend(0));
                Check(context.InductionAttendance.ClaimedDays == 0 && link.Player.InductionStones == 238, "carteira cheia nao consome presenca nem perde recompensa");
                context.Vouchers[CrackTuning.VoucherId] = 8;
                link.Request<GiveAttendanceReward, OK>(Attend(0));
                Check(link.Player.InductionStones == 13 && context.InductionAttendance.ClaimedDays == 1, "primeira presenca entrega cinco pedras e registra recebimento");
                link.Request<GiveAttendanceReward, Abort>(Attend(0));
                link.Request<GiveAttendanceReward, Abort>(Attend(1));
                Check(link.Player.InductionStones == 13, "uma presenca por dia mesmo com indice adulterado");
                context.Save(); Check(SafeSave.FlushPending(), "saldo e marcadores de resgate gravados juntos");
                claimedDay = context.InductionAttendance.ClaimedDays;
            }
            var loaded = Json.Read<PlayerContext>(File.ReadAllText(savePath)); loaded.Initialize(savePath);
            using (var link = new EconomyProtocolCheck.Link(loaded, world, null))
            {
                link.Player.ContextChanged += loaded.Save;
                Check(link.Player.InductionStones == 13 && loaded.InductionAttendance.ClaimedDays == claimedDay, "reconexao preserva carteira e presenca");
                link.Request<GiveAttendanceReward, Abort>(Attend(0));
                link.Request<RequestQuestScoreReward, Abort>(new() { Category = "daily", Score = 20 });
                Check(link.Player.InductionStones == 13, "reconectar nao repete bonus nem presenca");
                loaded.QuestDailyResetDay = "2000-01-01";
                Check(Scores(link).CurQuestScore == 0 && !loaded.InductionScoreClaims.Any(), "virada diaria reinicia pontos e bonus; saldo permanece");
                loaded.Vouchers[CrackTuning.VoucherId] = 239;
                link.Player.NoteQuestEvent(QuestEventType.Collected, QuestCatalog.Filters.Gather, 20);
                var capped = Claim(link, dailyId);
                Check(link.Player.InductionStones == 240 && capped.Reward.Value.Vouchers.Single().Count == 1, "missao respeita limite e informa quantidade efetivamente entregue");
                loaded.Vouchers[CrackTuning.VoucherId] = 13;
                for (int day = 1; day < 28; day++)
                {
                    loaded.InductionAttendance.LastClaimDay = "2000-01-01";
                    link.Request<GiveAttendanceReward, OK>(Attend(day));
                }
                Check(link.Player.InductionStones == 148 && loaded.InductionAttendance.ClaimedDays == 28, "28 presencas reais completam ciclo sem antecipar dias");
                loaded.Vouchers[CrackTuning.VoucherId] = 230;
                link.Request<GiveAttendanceAppendix, Abort>(new() { Category = CategoryType.Event1, SelectedReward = 0 });
                Check(!loaded.InductionAttendance.BonusTaken, "bonus final aguarda espaco na carteira");
                loaded.Vouchers[CrackTuning.VoucherId] = 148;
                link.Request<GiveAttendanceAppendix, OK>(new() { Category = CategoryType.Event1, SelectedReward = 0 });
                Check(link.Player.InductionStones == 168 && loaded.InductionAttendance.BonusTaken, "bonus final entrega vinte pedras");
                link.Request<GiveAttendanceAppendix, Abort>(new() { Category = CategoryType.Event1, SelectedReward = 0 });
                link.Request<GiveAttendanceReward, Abort>(Attend(0));
                loaded.InductionAttendance.LastClaimDay = "2000-01-01";
                link.Request<GiveAttendanceReward, OK>(Attend(0));
                Check(link.Player.InductionStones == 173 && loaded.InductionAttendance.ClaimedDays == 1, "ciclo renova somente em outro dia e segue funcional");
                Check(link.Messages.OfType<WalletUpdated>().Last().Wallet.Vouchers.Single(v => v.VoucherId == CrackTuning.VoucherId).Count == 173, "saldo publicado no protocolo corresponde ao saldo salvo");
                loaded.Save(); SafeSave.FlushPending();
            }
            var finalSave = Json.Read<PlayerContext>(File.ReadAllText(savePath));
            Check(finalSave.Vouchers[CrackTuning.VoucherId] == 173 && finalSave.InductionAttendance.ClaimedDays == 1,
                "novo ciclo e saldo final tambem sobrevivem ao save");
            Console.WriteLine($"[induction-check] PASS {_passed} verificacoes"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
}
