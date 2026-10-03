using Durango.Utils;
using Messages;
using Shared.Attendance;
using Shared.Quest;
using Yaml.Util;

namespace Durango.Online;

public sealed class InductionAttendanceSave
{
    public int ClaimedDays;
    public string LastClaimDay;
    public double Since;
    public bool BonusTaken;
}

public partial class Player
{
    // O widget Event possui o botao de bonus final; Monthly nao exibe appendices.
    private const CategoryType InductionCalendar = CategoryType.Event1;
    private QuestScoreInfos BuildQuestScoreInfos(string category)
    {
        EnsureDailyReset();
        int score = category == QuestCatalog.DailyCategory ? QuestCatalog.InCategory(category)
            .Count(d => d.IsLive && QuestStore.StateOf(EntityId, d.Id) == Shared.Quest.QuestState.Finished) * 10 : 0;
        return new QuestScoreInfos
        {
            Category = category,
            CurQuestScore = score,
            QuestScoreRewards = category != QuestCatalog.DailyCategory ? Array.Empty<QuestScoreReward>() :
                InductionRewardTuning.ScoreBonuses.Select(b => new QuestScoreReward
                {
                    QuestScore = b.Score,
                    State = _context.InductionScoreClaims.Contains(b.Score) ? QuestScoreRewardState.Taken :
                        score >= b.Score ? QuestScoreRewardState.Available : QuestScoreRewardState.NotAvailable,
                    Reward = new RewardInfo { Vouchers = new[] { StoneVoucher(b.Stones) } }
                }).ToArray()
        };
    }

    private static VoucherInfo StoneVoucher(int count) => new() { VoucherId = CrackTuning.VoucherId, Count = count };

    private void ClaimQuestScoreStones(RequestQuestScoreReward msg, uint seq)
    {
        var info = BuildQuestScoreInfos(msg.Category);
        var bonus = info.QuestScoreRewards.FirstOrDefault(b => b.QuestScore == msg.Score);
        if (bonus.State != QuestScoreRewardState.Available || bonus.Reward.Vouchers?.Length != 1 ||
            InductionRewardTuning.Maximum - InductionStones < bonus.Reward.Vouchers[0].Count)
        {
            Send(default(ReplySequenceMark), seq);
            Send(new Abort { Text = "Pontos insuficientes, recompensa já recebida ou carteira de pedras cheia." }, seq);
            Send(info, seq);
            Send(default(ReplySequenceMark), seq);
            return;
        }
        _context.InductionScoreClaims.Add(msg.Score);
        AddInductionStones(bonus.Reward.Vouchers[0].Count, $"Pontos diarios {msg.Score}");
        Send(BuildQuestScoreInfos(msg.Category), seq);
    }

    private static string AttendanceDay() => QuestCatalog.CurrentResetDay();
    private static double NextAttendanceDay() => QuestCatalog.NextResetUnix();

    private InductionAttendanceSave AttendanceState()
    {
        var state = _context.InductionAttendance ??= new();
        if (state.Since <= 0) state.Since = Times.UnixTimeNow();
        // Um ciclo pessoal avanca por presencas, sem perder os dias ausentes.
        // Preservar LastClaimDay impede outra recompensa diaria ao virar o ciclo.
        if (state.ClaimedDays >= InductionRewardTuning.AttendanceDays && (state.BonusTaken || InductionRewardTuning.AttendanceBonus == 0) && state.LastClaimDay != AttendanceDay())
        {
            state.ClaimedDays = 0; state.BonusTaken = false; state.Since = Times.UnixTimeNow();
        }
        return state;
    }

    private void SendInductionAttendance()
    {
        var state = AttendanceState();
        bool takenToday = state.LastClaimDay == AttendanceDay();
        Send(new TodayAttendanceRewards { Rewards = new()
        {
            [InductionCalendar] = new TodayAttendanceReward
            {
                Name = "Pedras de portal — presença", ShortName = "Presença",
                RewardNumber = Math.Min(InductionRewardTuning.AttendanceDays - 1, state.ClaimedDays - (takenToday ? 1 : 0)),
                RestorableDays = -1,
                AppendixRewardable = state.ClaimedDays >= InductionRewardTuning.AttendanceDays && !state.BonusTaken,
                Since = state.Since,
                Until = NextAttendanceDay() + Math.Max(1, InductionRewardTuning.AttendanceDays - state.ClaimedDays) * 86400.0,
                NextAttendTime = NextAttendanceDay(), Image = "", BgImage = ""
            }
        }});
    }

    private void SendInductionAttendanceRewards(CategoryType category, uint seq)
    {
        var state = AttendanceState();
        bool supported = category == InductionCalendar;
        Send(new AttendanceRewards
        {
            Category = category,
            Rewards = !supported ? Array.Empty<AttendanceReward>() : Enumerable.Range(0, InductionRewardTuning.AttendanceDays)
                .Select(i => new AttendanceReward { Rewarded = i < state.ClaimedDays,
                    Voucher = StoneVoucher(InductionRewardTuning.AttendanceDaily), RewardType = RewardType.None }).ToArray(),
            Appendices = !supported || InductionRewardTuning.AttendanceBonus == 0 ? Array.Empty<AttendanceReward>() :
                new[] { new AttendanceReward { Rewarded = state.BonusTaken, RewardType = RewardType.Rare,
                    Voucher = StoneVoucher(InductionRewardTuning.AttendanceBonus) } }
        }, seq);
    }

    private void ClaimInductionAttendance(CategoryType category, int number, bool restore, bool appendix, uint seq)
    {
        var state = AttendanceState();
        int amount = appendix ? InductionRewardTuning.AttendanceBonus : InductionRewardTuning.AttendanceDaily;
        bool eligible = category == InductionCalendar && !restore && amount > 0 &&
            (appendix ? number == 0 && state.ClaimedDays >= InductionRewardTuning.AttendanceDays && !state.BonusTaken :
                number == state.ClaimedDays && number < InductionRewardTuning.AttendanceDays && state.LastClaimDay != AttendanceDay());
        if (!eligible || InductionRewardTuning.Maximum - InductionStones < amount)
        {
            Send(new Abort { Text = "Presença indisponível, recompensa já recebida ou carteira de pedras cheia." }, seq);
            return;
        }
        if (appendix) state.BonusTaken = true;
        else { state.ClaimedDays++; state.LastClaimDay = AttendanceDay(); }
        AddInductionStones(amount, appendix ? "Bonus de presenca" : $"Presenca {state.ClaimedDays}");
        Send(default(OK), seq);
        SendInductionAttendance();
    }
}
