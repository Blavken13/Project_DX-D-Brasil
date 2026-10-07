using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Messages;
using SkillCat = Shared.Skill.Category;

namespace Durango.Online;

public partial class Player
{
    LearningGuideSave LearningGuide
    {
        get
        {
            _context.LearningGuide ??= new();
            _context.LearningGuide.Completed ??= new(StringComparer.Ordinal);
            _context.LearningGuide.Claimed ??= new(StringComparer.Ordinal);
            return _context.LearningGuide;
        }
    }

    void RegisterLearningGuideHandlers()
    {
        _connection.Recv(delegate(GetAdvisorTargets msg, PacketHeader header)
        {
            RefreshAchievementLevels(); Send(BuildAdvisorTargets(), header.Seq);
        });
        _connection.Recv(delegate(GetTargetTitle msg, PacketHeader header)
        { Send(new TargetTitle { TitleId = LearningGuide.Target }, header.Seq); });
        _connection.Recv(delegate(SelectTargetTitle msg, PacketHeader header)
        {
            if (!LearningGuideCatalog.Courses.TryGetValue(msg.TitleId ?? "", out var course) || !CanStartCourse(course))
            { Send(new Abort { Text = "Este curso não está disponível para o seu nível de habilidade." }, header.Seq); return; }
            LearningGuide.Target = msg.TitleId; LearningGuide.SelectedOnce = true;
            RefreshAchievementLevels(save: false); OnContextChanged();
            Send(new TargetTitle { TitleId = msg.TitleId }); Send(BuildAdvisorTargets()); SendSkills();
        });
        _connection.Recv(delegate(CancelTargetTitle msg, PacketHeader header)
        {
            LearningGuide.Target = null; OnContextChanged(); Send(new TargetTitle { TitleId = null });
            Send(default(OK), header.Seq); SendSkills();
        });
        _connection.Recv(delegate(ReceiveAdvisorReward msg, PacketHeader header)
        { ClaimGuideReward(msg.TitleId, header.Seq); });
        _connection.Recv(delegate(UpdateAdvisorProgress msg, PacketHeader header)
        {
            // The supplied client counters are hints only. All progress comes from learned skills.
            RefreshAchievementLevels(); Send(BuildAdvisorTargets());
        });
        _connection.Recv(delegate(GetTitles msg, PacketHeader header)
        { Send(new Titles { TitleIds = EarnedGuideTitles() }, header.Seq); });
        _connection.Recv(delegate(GetSkillAdvisorPoint msg, PacketHeader header)
        { Send(new AdvisorRewardPoint { Point = LearningGuide.Points }, header.Seq); });
        _connection.Recv(delegate(GetSkillCategoryAdvisorPoint msg, PacketHeader header)
        { Send(new AdvisorRewardPoint { Point = LearningGuide.Points }, header.Seq); });
        RefreshAchievementLevels();
    }

    bool CanStartCourse(GuideCourse course) => course != null && (course.RequiredSkill == null ||
        course.RequiredSkill.Category < 0 || CategoryState(course.RequiredSkill.Category).Level >= course.RequiredSkill.Level);

    float CourseRatio(GuideCourse course)
    {
        if (_skills == null || course == null || !CanStartCourse(course)) return 0;
        int required = 0, learned = 0;
        foreach (var skill in course.Skills ?? Array.Empty<GuideSkill>())
        {
            // Missing/invalid nodes never count as learned, even if a malformed save claims them.
            required++;
            if (FindNode(skill.SkillId, skill.SubId, skill.Level, out _) != null &&
                (_skills.Learned.GetValueOrDefault(skill.SkillId)?.GetValueOrDefault(skill.SubId ?? BaseSubId) ?? 0) >= skill.Level) learned++;
        }
        foreach (var level in course.CategoryLevels ?? new())
        { required++; if (CategoryState(level.Key).Level >= level.Value) learned++; }
        return required > 0 ? (float)learned / required : 0;
    }

    bool RefreshLearningGuide(bool save = true)
    {
        if (_skills == null) return false;
        bool changed = false;
        foreach (var (id, course) in LearningGuideCatalog.Courses)
        {
            if (LearningGuide.Completed.Contains(id) || CourseRatio(course) < 1) continue;
            LearningGuide.Completed.Add(id); changed = true;
            Send(new AdviceCompletedEffect { Type = Shared.System.RewardEffect.AdviceCompleted, TitleId = id });
        }
        if (changed) { Send(BuildAdvisorTargets()); if (save) OnContextChanged(); }
        return changed;
    }

    AdvisorTargets BuildAdvisorTargets() => new()
    {
        Titles = LearningGuideCatalog.Courses.ToDictionary(p => p.Key,
            p => LearningGuide.Completed.Contains(p.Key) ? 1f : CourseRatio(p.Value), StringComparer.Ordinal),
        RemainingRewards = LearningGuide.Completed.Where(id => LearningGuideCatalog.Courses.ContainsKey(id) &&
            !LearningGuide.Claimed.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray()
    };

    void ClaimGuideReward(string id, uint seq)
    {
        RefreshAchievementLevels();
        if (!LearningGuideCatalog.Courses.TryGetValue(id ?? "", out var course) ||
            !LearningGuide.Completed.Contains(id) || LearningGuide.Claimed.Contains(id))
        { Send(new Abort { Text = "A recompensa deste curso ainda não está disponível ou já foi recebida." }, seq); return; }
        var items = new List<Item>();
        foreach (var reward in course.RewardItems ?? Array.Empty<GuideRewardItem>())
        {
            if (reward.Count < 1 || reward.Count > 100 || reward.Level < 1)
            { Send(new Abort { Text = "Não foi possível preparar a recompensa deste curso." }, seq); return; }
            for (int i = 0; i < reward.Count; i++)
            {
                var item = Cheats.MakeItem(reward.PrototypeId, reward.Level);
                if (!item.HasValue) { Send(new Abort { Text = "Não foi possível preparar a recompensa deste curso." }, seq); return; }
                items.Add(item.Value);
            }
        }
        if (!string.IsNullOrEmpty(course.RewardTitleId) && !LearningGuideCatalog.Titles.ContainsKey(course.RewardTitleId))
        { Send(new Abort { Text = "O título de recompensa deste curso não está disponível." }, seq); return; }
        if (_context.InventoryItems.Sum(i => (long)Math.Max(1, i.Size)) + items.Sum(i => (long)Math.Max(1, i.Size)) > CurrentInventoryCapacity)
        { Send(new Abort { Text = "Não há espaço na mochila para esta recompensa. Libere espaço e tente novamente." }, seq); return; }
        // All validation precedes mutation; items, claim and points share one saved player context.
        LearningGuide.Claimed.Add(id);
        LearningGuide.Points = (int)Math.Min(int.MaxValue, (long)Math.Max(0, LearningGuide.Points) + Math.Max(0, course.RewardPoint));
        _context.InventoryItems.AddRange(items);
        if (LearningGuide.Target == id) LearningGuide.Target = null;
        OnContextChanged();
        if (items.Count > 0) Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });
        Send(new Titles { TitleIds = EarnedGuideTitles() });
        Send(new AdvisorRewardPoint { Point = LearningGuide.Points });
        // The original client's OK callback clears its selected course. Restore
        // any other active course after that callback, using protocol order.
        Send(default(OK), seq);
        Send(new TargetTitle { TitleId = LearningGuide.Target });
        Send(BuildAdvisorTargets()); SendSkills();
    }

    string[] EarnedGuideTitles() => LearningGuide.Claimed.Where(LearningGuideCatalog.Courses.ContainsKey)
        .Select(id => LearningGuideCatalog.Courses[id].RewardTitleId)
        .Where(id => !string.IsNullOrEmpty(id) && LearningGuideCatalog.Titles.ContainsKey(id))
        .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    GuideTitle SelectedGuideTitle()
    {
        string id = _context.AppearPlayer.Title.TitleId;
        return !string.IsNullOrEmpty(id) && EarnedGuideTitles().Contains(id, StringComparer.Ordinal) &&
            LearningGuideCatalog.Titles.TryGetValue(id, out var title) ? title : null;
    }

    Messages.Skill[] AdvisedGuideSkills() => LearningGuideCatalog.Courses.TryGetValue(LearningGuide.Target ?? "", out var course)
        ? (course.Skills ?? Array.Empty<GuideSkill>()).Select(s => new Messages.Skill { SkillId = s.SkillId, SubId = s.SubId, Level = s.Level }).ToArray()
        : Array.Empty<Messages.Skill>();
    Dictionary<SkillCat, int> AdvisedGuideCategories() => LearningGuideCatalog.Courses.TryGetValue(LearningGuide.Target ?? "", out var course)
        ? (course.CategoryLevels ?? new()).ToDictionary(p => (SkillCat)p.Key, p => p.Value) : new();
}
