using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Quest;
using Yaml.Util;
using SkillCat = Shared.Skill.Category;
using QuestStateEnum = Shared.Quest.QuestState;

namespace Durango.Online;

/// <summary>
/// Missões diárias e conquistas com eventos de jogabilidade e metas de nível.
/// Progress lives in <see cref="QuestStore"/> and is flushed to <c>_context.Quests</c>.
/// </summary>
public partial class Player
{
    bool _questsHydrated;

    /// <summary>
    /// โหลดความคืบหน้าจากไฟล์เซฟ แล้วเติมแถว Daily ที่ยังไม่มี
    /// เรียกจาก <see cref="RegisterQuestHandlers"/> ก่อนส่ง QuestCategories
    /// </summary>
    void HydrateQuests()
    {
        if (_questsHydrated) return;
        _questsHydrated = true;
        _context.InductionScoreClaims ??= new();

        ContextChanged += FlushQuestSave;

        var loaded = new Dictionary<string, QuestStore.Entry>(StringComparer.Ordinal);
        if (_context.Quests != null)
        {
            foreach (KeyValuePair<string, QuestSaveData> kv in _context.Quests)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                loaded[kv.Key] = new QuestStore.Entry
                {
                    State = kv.Value.State,
                    Progress = Math.Max(0, kv.Value.Progress),
                    GoalCount = Math.Max(1, kv.Value.GoalCount)
                };
            }
        }
        QuestStore.ReplaceAll(EntityId, loaded);

        string today = QuestCatalog.CurrentResetDay();
        if (QuestCatalog.ShouldResetDaily(_context.QuestDailyResetDay))
        {
            ResetDailyQuests();
            Console.WriteLine($"[เควส] {Short(EntityId)} รีเซ็ต Daily (วัน {_context.QuestDailyResetDay} → {today})");
        }
        _context.QuestDailyResetDay = today;

        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            QuestStore.Ensure(EntityId, def);
        }

        FlushQuestSave();
    }

    void EnsureDailyReset()
    {
        if (!QuestCatalog.ShouldResetDaily(_context.QuestDailyResetDay)) return;
        string today = QuestCatalog.CurrentResetDay();
        ResetDailyQuests();
        _context.QuestDailyResetDay = today;
        OnContextChanged();
        Console.WriteLine($"[เควส] {Short(EntityId)} รีเซ็ต Daily → {today}");
    }

    void ResetDailyQuests()
    {
        _context.InductionScoreClaims ??= new();
        _context.InductionScoreClaims.Clear();
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            QuestStore.Set(EntityId, def.Id, QuestStateEnum.WorkInProgress, 0, Math.Max(1, def.GoalCount));
        }
    }

    void FlushQuestSave()
    {
        // ห้ามประทับ CurrentResetDay() ที่นี่ — ContextChanged ยิงจากกระเป๋า/สกิล/วาร์ป ฯลฯ
        // ถ้าข้ามเที่ยงคืน KST แล้วยังไม่ผ่าน EnsureDailyReset การประทับ "วันนี้" จะทำให้
        // ShouldResetDaily เป็นเท็จทั้งเซสชันและไฟล์ .player ⇒ Daily ไม่รีเซ็ตเลย
        if (QuestCatalog.ShouldResetDaily(_context.QuestDailyResetDay))
        {
            string today = QuestCatalog.CurrentResetDay();
            ResetDailyQuests();
            _context.QuestDailyResetDay = today;
            Console.WriteLine($"[เควส] {Short(EntityId)} รีเซ็ต Daily ตอนเซฟ → {today}");
        }

        Dictionary<string, QuestStore.Entry> snap = QuestStore.Snapshot(EntityId);
        var save = new Dictionary<string, QuestSaveData>(snap.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, QuestStore.Entry> kv in snap)
        {
            save[kv.Key] = new QuestSaveData
            {
                State = kv.Value.State,
                Progress = kv.Value.Progress,
                GoalCount = kv.Value.GoalCount
            };
        }
        _context.Quests = save;
    }

    /// <summary>โฆษณาแท็บ Daily หลัง QuestCategories ถึงฝั่งเกมแล้ว</summary>
    void AnnouncePlayableQuests()
    {
        RefreshAchievementLevels();
        QuestToDo[] todos = DailyTodos();
        if (todos.Length == 0) return;
        Send(new QuestStarted
        {
            Category = QuestCatalog.DailyCategory,
            Quests = todos
        });
    }

    QuestToDo[] DailyTodos()
    {
        IReadOnlyList<QuestDef> defs = QuestCatalog.InCategory(QuestCatalog.DailyCategory);
        var todos = new QuestToDo[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            QuestStore.Ensure(EntityId, defs[i]);
            todos[i] = QuestStore.ToQuestToDo(EntityId, defs[i].Id, BuildQuestReward(defs[i]));
        }
        return todos;
    }

    int CountClaimableDaily()
    {
        int n = 0;
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            if (QuestStore.StateOf(EntityId, def.Id) == QuestStateEnum.ReachTheGoal) n++;
        }
        return n;
    }

    /// <summary>
    /// จุดรวมจากระบบเล่นจริง — Gathering/Crafting/Building/Hunting/Farm เรียกตรงนี้
    /// <paramref name="detail"/> = gather/carcass หรือ recipe.category
    /// </summary>
    public void NoteQuestEvent(QuestEventType ev, string detail = null, int amount = 1, QuestEventContext context = null)
    {
        if (amount <= 0) return;
        NoteSafehouseHuntingEvent(ev, context);
        EnsureDailyReset();

        bool any = false;
        foreach (QuestDef def in QuestCatalog.Live)
        {
            if (!QuestCatalog.IsTracked(def.Id) || !QuestCatalog.Matches(def, ev, detail, context)) continue;
            QuestStore.Entry entry = QuestStore.Ensure(EntityId, def);
            if (entry == null) continue;
            var row = new QuestProgressRow
            {
                State = entry.State,
                Progress = entry.Progress,
                GoalCount = entry.GoalCount
            };
            if (!QuestCatalog.TryApply(row, def, amount)) continue;
            QuestStore.Set(EntityId, def.Id, row.State, row.Progress, row.GoalCount);
            any = true;
            bool finished = row.State == QuestStateEnum.Finished;
            Send(new NotifyQuestProceed
            {
                QuestId = def.Id,
                Progress = row.Progress,
                GoalCount = row.GoalCount,
                Finished = finished
            });
            if (row.State == QuestStateEnum.ReachTheGoal)
            {
                Console.WriteLine($"[เควส] {Short(EntityId)} ถึงเป้า '{def.Id}' ({row.Progress}/{row.GoalCount})");
            }
        }
        if (any) OnContextChanged();
    }

    QuestEventContext QuestActionContext(string recipeId = null, Item[] products = null, int entityType = -1)
    {
        var template = RegionCatalog.GetTemplate(_world.TerrainInfo?.region_template);
        return new QuestEventContext { Biome = (int)(template?.Biome ?? Shared.Region.Biome.Invalid),
            Role = (int)(template?.Role ?? Shared.Region.Role.Invalid), RecipeId = recipeId,
            Products = products, EntityType = entityType };
    }

    /// <summary>
    /// Resgata EXP e moedas T de uma diária ou conquista concluída.
    /// Retorna true quando a solicitação foi tratada, inclusive se houve Abort.
    /// </summary>
    bool TryClaimPlayableQuestReward(string questId, uint seq)
    {
        EnsureDailyReset();
        RefreshAchievementLevels();

        QuestDef def = QuestCatalog.Find(questId);
        if (def == null || !QuestCatalog.IsTracked(questId)) return false;

        if (!def.IsLive || !QuestStore.TryFinishReward(EntityId, questId, out QuestStore.Entry entry))
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอรับ '{questId}' แต่ยังไม่ถึงเป้า");
            Send(new Abort { Text = "A recompensa desta missão ainda não está disponível." }, seq);
            return true;
        }

        var reward = BuildQuestReward(def);
        var (_, weight) = QuestRewardTuning.For(def, _skillLevel);
        int exp = reward.Exp ?? 0;
        SkillCat? skill = SkillForQuest(def);
        // O estado de resgate e o saldo são gravados no mesmo PlayerContext.
        long stones = reward.Currency?.GetValueOrDefault(Shared.Economy.Currency.TStone) ?? 0;
        int induction = reward.Vouchers?.FirstOrDefault(v => v.VoucherId == CrackTuning.VoucherId).Count ?? 0;
        if (induction > 0) AddInductionStones(induction, $"Missao {questId}");
        AddTStone(stones, $"Recompensa {questId}");
        AddExpForAction(weight, skill, $"Missão {questId}");
        OnContextChanged();

        Send(new NotifyQuestProceed
        {
            QuestId = questId,
            Progress = entry.GoalCount,
            GoalCount = entry.GoalCount,
            Finished = true
        });

        Send(new QuestRewardResults
        {
            Category = def.Category,
            QuestId = questId,
            Reward = reward,
            QuestScoreInfos = BuildQuestScoreInfos(def.Category)
        });

        Console.WriteLine($"[missões] {Short(EntityId)} resgatou '{questId}': {exp} EXP e {stones} moedas T");
        return true;
    }

    static SkillCat? SkillForQuest(QuestDef def)
    {
        if (def == null) return null;
        if (def.Event == QuestEventType.Hunted) return SkillCat.MeleeCombat;
        if (def.Event == QuestEventType.Built) return SkillCat.Constructing;
        if (def.Event == QuestEventType.Farmed) return SkillCat.Farming;
        if (def.Event == QuestEventType.Collected)
        {
            return def.Filter == QuestCatalog.Filters.Carcass ? SkillCat.Butchery : SkillCat.Gathering;
        }
        if (def.Event == QuestEventType.Crafted)
        {
            if (def.Filter == QuestCatalog.Filters.Cook) return SkillCat.Cooking;
            if (def.Filter == QuestCatalog.Filters.Clothing) return SkillCat.Armorcrafting;
            if (def.Filter == QuestCatalog.Filters.Process) return SkillCat.Process;
            if (def.Filter == QuestCatalog.Filters.Weapon || def.Filter == QuestCatalog.Filters.Tool)
                return SkillCat.Weaponcrafting;
        }
        return null;
    }

    int CountClaimableAchievements() => QuestCatalog.InCategory(QuestCatalog.AchievementCategory)
        .Count(d => d.IsLive && QuestStore.StateOf(EntityId, d.Id) == QuestStateEnum.ReachTheGoal);

    internal RewardInfo BuildQuestReward(QuestDef def)
    {
        var (stones, weight) = QuestRewardTuning.For(def, _skillLevel);
        int induction = Math.Min(InductionRewardTuning.ForQuest(def), Math.Max(0, InductionRewardTuning.Maximum - InductionStones));
        int exp = Math.Min(Math.Max(0, ExpCap() - (_skills?.Exp ?? 0)), PreviewActionExp(weight));
        return new RewardInfo
        {
            Exp = exp,
            Currency = stones > 0 ? new() { [Shared.Economy.Currency.TStone] = Math.Min(stones, Math.Max(0, MaxCurrencyBalance - TStone)) } : null,
            QuestScore = def?.Category == QuestCatalog.DailyCategory ? 10 : null,
            Vouchers = induction > 0 ? new[] { StoneVoucher(induction) } : Array.Empty<VoucherInfo>()
        };
    }

    void RefreshAchievementLevels(bool save = true)
    {
        if (_skills == null || !_questsHydrated) return;
        bool changed = RefreshLearningGuide(save: false);
        foreach (var def in QuestCatalog.InCategory(QuestCatalog.AchievementCategory).Where(d => d.IsLive && (d.LevelCategory >= -1 || d.Objective?.Snapshot != null)))
        {
            var entry = QuestStore.Ensure(EntityId, def);
            if (entry.State is QuestStateEnum.Finished or QuestStateEnum.ReachTheGoal) continue;
            int level = def.Objective?.Snapshot switch
            {
                "advisor_selected" => LearningGuide.SelectedOnce ? 1 : 0,
                "advisor_completed" => def.Objective.Courses?.Length > 0 ?
                    LearningGuide.Completed.Count(id => def.Objective.Courses.Contains(id, StringComparer.Ordinal)) : LearningGuide.Completed.Count,
                _ => def.LevelCategory == -1 ? _skillLevel : CategoryState(def.LevelCategory).Level
            };
            int progress = Math.Min(Math.Max(0, level), def.GoalCount);
            if (progress <= entry.Progress) continue;
            QuestStore.Set(EntityId, def.Id, progress >= def.GoalCount ? QuestStateEnum.ReachTheGoal : QuestStateEnum.WorkInProgress,
                progress, def.GoalCount);
            Send(new NotifyQuestProceed { QuestId = def.Id, Progress = progress, GoalCount = def.GoalCount, Finished = false });
            changed = true;
        }
        if (changed && save) OnContextChanged();
    }
}
