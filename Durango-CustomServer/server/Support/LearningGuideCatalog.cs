using System;
using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json;

namespace Durango.Online;

public static class LearningGuideCatalog
{
    public static IReadOnlyDictionary<string, GuideCourse> Courses { get; private set; } = new Dictionary<string, GuideCourse>();
    public static IReadOnlyDictionary<string, GuideTitle> Titles { get; private set; } = new Dictionary<string, GuideTitle>();
    public static void Load()
    {
        Courses = Json.ReadFromFile<Dictionary<string, GuideCourse>>("advices") ?? new();
        Titles = Json.ReadFromFile<Dictionary<string, GuideTitle>>("titles") ?? new();
    }
}

public sealed class GuideCourse
{
    [JsonProperty("name")] public Gettext Name;
    [JsonProperty("skills")] public GuideSkill[] Skills = Array.Empty<GuideSkill>();
    [JsonProperty("category_levels")] public Dictionary<int, int> CategoryLevels = new();
    [JsonProperty("required_skill")] public GuideCategory RequiredSkill;
    [JsonProperty("reward_title_id")] public string RewardTitleId;
    [JsonProperty("reward_items")] public GuideRewardItem[] RewardItems = Array.Empty<GuideRewardItem>();
    [JsonProperty("advisor_reward_point")] public int RewardPoint;
}
public sealed class GuideSkill
{
    [JsonProperty("skill_id")] public string SkillId;
    [JsonProperty("sub_id")] public string SubId;
    [JsonProperty("level")] public int Level;
}
public sealed class GuideCategory
{
    [JsonProperty("skill_category")] public int Category = -1;
    [JsonProperty("level")] public int Level;
}
public sealed class GuideRewardItem
{
    [JsonProperty("prototype_id")] public string PrototypeId;
    [JsonProperty("level")] public int Level;
    [JsonProperty("count")] public int Count;
}
public sealed class GuideTitle
{
    [JsonProperty("name")] public Gettext Name;
    [JsonProperty("abilities")] public Dictionary<int, int> Abilities = new();
    [JsonProperty("modifiers")] public Dictionary<string, float> Modifiers = new();
}
public sealed class LearningGuideSave
{
    [JsonProperty("version")] public int Version = 1;
    [JsonProperty("target")] public string Target;
    [JsonProperty("selected_once")] public bool SelectedOnce;
    [JsonProperty("completed")] public HashSet<string> Completed = new(StringComparer.Ordinal);
    [JsonProperty("claimed")] public HashSet<string> Claimed = new(StringComparer.Ordinal);
    [JsonProperty("points")] public int Points;
}
