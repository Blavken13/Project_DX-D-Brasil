using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Online;
using System.Text.RegularExpressions;
using Durango.Utils;
using Newtonsoft.Json;
using Shared.Quest;

namespace Yaml.Util;

/// <summary>
/// Phase 1 quest definitions loaded from <c>quests/quests_for_client.json</c>.
///
/// The client asset has display text + <c>quest_type</c> + <c>category</c> only — no
/// structured objectives or rewards. This loader keeps the raw rows and applies a
/// small curated map from Daily (and Once, when the same count-event pipeline
/// applies) ids onto <see cref="QuestEventType"/> so gameplay can drive progress.
/// </summary>
public static class QuestCatalog
{
    public const string DailyCategory = "daily";
    public const string AchievementCategory = "permanent";

    /// <summary>Canonical event filters written into <see cref="QuestDef.Filter"/>.</summary>
    public static class Filters
    {
        public const string Gather = "gather";
        public const string Carcass = "carcass";
        public const string Weapon = "weapon";
        public const string Tool = "tool";
        public const string Clothing = "clothing";
        public const string Cook = "cook";
        public const string Process = "process";
    }

    static readonly Regex FirstNumber = new(@"(\d+(?:[.,]\d{3})*)", RegexOptions.Compiled);
    static readonly Dictionary<string, QuestDef> ById = new(StringComparer.Ordinal);
    static readonly Dictionary<string, List<QuestDef>> ByCategory = new(StringComparer.Ordinal);

    public static bool Loaded { get; private set; }

    public static IReadOnlyDictionary<string, QuestDef> All => ById;

    public static void Load()
    {
        ById.Clear();
        ByCategory.Clear();
        Loaded = false;
        QuestRewardTuning.Load();
        InductionRewardTuning.Load();

        var raw = Json.ReadFromFile<Dictionary<string, QuestAssetRow>>("quests/quests_for_client");
        var objectives = Json.ReadFromFile<Dictionary<string, QuestObjective>>("quests/objectives_server") ?? new();
        if (raw == null || raw.Count == 0)
        {
            Console.WriteLine("[เควส] ไม่พบ quests/quests_for_client — แคตตาล็อกว่าง");
            Loaded = true;
            return;
        }

        int daily = 0, once = 0, live = 0;
        foreach (KeyValuePair<string, QuestAssetRow> pair in raw)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) continue;
            QuestDef def = FromAsset(pair.Key, pair.Value);
            if (objectives.TryGetValue(def.Id, out var objective) &&
                (def.Category == DailyCategory || def.Category == AchievementCategory) &&
                Enum.TryParse<QuestEventType>(objective.Event, out var objectiveEvent) && objectiveEvent != QuestEventType.Invalid && objective.Goal > 0)
            {
                def.Objective = objective;
                def.Event = objectiveEvent; def.Filter = objective.Filter ?? "";
                def.GoalCount = objective.Goal; def.IsLive = true; def.UnknownReason = "";
            }
            ById[def.Id] = def;
            if (!ByCategory.TryGetValue(def.Category, out List<QuestDef> list))
            {
                list = new List<QuestDef>();
                ByCategory[def.Category] = list;
            }
            list.Add(def);
            if (def.Type == QuestType.Daily) daily++;
            else if (def.Type == QuestType.Once) once++;
            if (def.IsLive) live++;
        }

        // Ordenar por meta, não pelo primeiro número da descrição nem pelo nível
        // atual de quem resgata: cada etapa da mesma conquista cresce de forma fixa.
        foreach (var chain in ById.Values.Where(d => d.Category == AchievementCategory && d.IsLive)
            .GroupBy(d => Regex.Replace(d.Id, @"_\d+$", "")))
        {
            int tier = 0;
            foreach (var def in chain.OrderBy(d => d.GoalCount).ThenBy(d => d.Id, StringComparer.Ordinal))
                def.RewardTier = ++tier;
        }
        Loaded = true;
        Console.WriteLine($"[เควส] โหลดแคตตาล็อก {ById.Count} แถว (Daily {daily} · Once {once} · เล่นได้ {live})");
    }

    public static QuestDef Find(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return null;
        return ById.TryGetValue(questId, out QuestDef def) ? def : null;
    }

    /// <summary>Categorias de missões e conquistas que o servidor oferece ao cliente.</summary>
    public static bool IsPlayableCategory(string category)
        => category == DailyCategory || category == AchievementCategory;

    /// <summary>Id is a Daily/Once row we persist and answer with real state (not sunset Finished).</summary>
    public static bool IsTracked(string questId)
    {
        QuestDef def = Find(questId);
        return def != null && (def.Category == DailyCategory || (def.Category == AchievementCategory && def.IsLive));
    }

    public static IReadOnlyList<QuestDef> InCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return Array.Empty<QuestDef>();
        return ByCategory.TryGetValue(category, out List<QuestDef> list) ? list : Array.Empty<QuestDef>();
    }

    public static IEnumerable<QuestDef> Live
    {
        get
        {
            foreach (QuestDef def in ById.Values)
            {
                if (def.IsLive) yield return def;
            }
        }
    }

    internal static QuestDef FromAsset(string id, QuestAssetRow row)
    {
        string category = row.Category ?? string.Empty;
        var type = (QuestType)row.QuestType;
        string description = row.Description?.MsgId ?? string.Empty;
        int goal = ParseGoal(description);
        Classify(id, category, type, description, goal, out QuestEventType ev, out string filter,
            out bool live, out string unknown);

        var def = new QuestDef
        {
            Id = id,
            Category = category,
            Type = type,
            Subject = row.Subject?.MsgId ?? id,
            Description = description,
            GoalCount = Math.Max(1, goal),
            Event = ev,
            Filter = filter ?? string.Empty,
            IsLive = live,
            UnknownReason = unknown ?? string.Empty,
            DisplayOnHud = row.DisplayOnHud,
            Order = row.Order
        };
        if (category == AchievementCategory && type == QuestType.Once) ClassifyAchievement(def);
        return def;
    }

    public static int ParseGoal(string description)
    {
        if (string.IsNullOrEmpty(description)) return 1;
        Match m = FirstNumber.Match(description);
        if (!m.Success) return 1;
        string number = m.Groups[1].Value.Replace(".", "").Replace(",", "");
        return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : 1;
    }

    private static void ClassifyAchievement(QuestDef def)
    {
        var counters = new (string Prefix, QuestEventType Event, string Filter)[]
        {
            ("permanent_hunt_any_animal_", QuestEventType.Hunted, ""),
            ("permanent_weaponcrafting_any_", QuestEventType.Crafted, Filters.Weapon),
            ("permanent_armorcrafting_any_", QuestEventType.Crafted, Filters.Clothing),
            ("permanent_cooking_any_", QuestEventType.Crafted, Filters.Cook),
            ("permanent_constructing_any_", QuestEventType.Built, ""),
            ("permanent_gathering_any_", QuestEventType.Collected, Filters.Gather),
            ("permanent_farming_any_", QuestEventType.Farmed, "")
        };
        foreach (var counter in counters)
        {
            if (!def.Id.StartsWith(counter.Prefix, StringComparison.Ordinal)) continue;
            def.Event = counter.Event; def.Filter = counter.Filter;
            def.IsLive = true; def.UnknownReason = ""; return;
        }
        if (def.Id.StartsWith("permanent_level_character_", StringComparison.Ordinal))
        {
            def.LevelCategory = -1; def.IsLive = true; def.UnknownReason = ""; return;
        }
        var skills = new (string Name, Shared.Skill.Category Category)[]
        {
            ("melee_combat", Shared.Skill.Category.MeleeCombat), ("ranged_combat", Shared.Skill.Category.RangedCombat),
            ("defense", Shared.Skill.Category.Defense), ("butchery", Shared.Skill.Category.Butchery),
            ("weaponcrafting", Shared.Skill.Category.Weaponcrafting), ("armorcrafting", Shared.Skill.Category.Armorcrafting),
            ("cooking", Shared.Skill.Category.Cooking), ("constructing", Shared.Skill.Category.Constructing),
            ("process", Shared.Skill.Category.Process), ("gathering", Shared.Skill.Category.Gathering),
            ("farming", Shared.Skill.Category.Farming)
        };
        foreach (var skill in skills)
        {
            if (!def.Id.StartsWith("permanent_level_skill_" + skill.Name + "_", StringComparison.Ordinal)) continue;
            def.LevelCategory = (int)skill.Category; def.IsLive = true; def.UnknownReason = ""; return;
        }
        if (def.Id == "taming_animal_zebra")
        {
            def.Event = QuestEventType.AnimalTamed; def.Filter = "2027";
            def.GoalCount = 1; def.IsLive = true; def.UnknownReason = "";
        }
    }

    static void Classify(string id, string category, QuestType type, string description, int goal,
        out QuestEventType ev, out string filter, out bool live, out string unknown)
    {
        ev = QuestEventType.Invalid;
        filter = string.Empty;
        live = false;
        unknown = string.Empty;

        // Phase 1 only activates the official Daily tab. Once rows are parsed so the
        // next agent can see them, but they are not the same count-event pipeline
        // (advisor courses, level gates, event islands).
        if (!string.Equals(category, DailyCategory, StringComparison.Ordinal) || type != QuestType.Daily)
        {
            unknown = type == QuestType.Once
                ? "Missão única fora da categoria diária; geralmente envolve conselheiros, níveis ou eventos, sem contador de eventos"
                : "Fora da categoria diária (eventos, Natal, cidades etc.); indisponível nesta fase";
            return;
        }

        if (id.StartsWith("mission_finish", StringComparison.Ordinal))
        {
            ev = QuestEventType.MissionUpdated;
            unknown = "Requer o sistema de missões de facção, ainda indisponível nesta fase";
            return;
        }

        if (id.StartsWith("daily_hunting_a", StringComparison.Ordinal) ||
            id.StartsWith("daily_hunting_b", StringComparison.Ordinal) ||
            id.StartsWith("daily_hunting_c", StringComparison.Ordinal))
        {
            ev = QuestEventType.Hunted;
            live = true;
            return;
        }

        if (id.StartsWith("daily_hunting_", StringComparison.Ordinal))
        {
            ev = QuestEventType.Hunted;
            unknown = "Requer caça em um bioma específico; o servidor ainda não filtra o bioma desta missão";
            return;
        }

        if (id.StartsWith("daily_weaponcrafting_a", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Weapon;
            live = true;
            return;
        }

        if (id.StartsWith("daily_weaponcrafting_b", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Tool;
            live = true;
            return;
        }

        if (id.StartsWith("daily_armorcrafting", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Clothing;
            live = true;
            return;
        }

        if (id.StartsWith("daily_cooking_a", StringComparison.Ordinal) ||
            id.StartsWith("daily_cooking_b", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Cook;
            live = true;
            if (id.StartsWith("daily_cooking_b", StringComparison.Ordinal))
            {
                unknown = "A descrição indica processamento de materiais, mas a receita não tem categoria própria; contabilizada como culinária";
            }
            return;
        }

        if (id.StartsWith("daily_constructing", StringComparison.Ordinal))
        {
            ev = QuestEventType.Built;
            live = true;
            return;
        }

        if (id.StartsWith("daily_process", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Process;
            live = true;
            if (id.StartsWith("daily_process_b", StringComparison.Ordinal))
            {
                unknown = "A descrição indica acabamento de materiais, mas usa a mesma categoria material_process de process_a";
            }
            return;
        }

        if (id.StartsWith("daily_gathering", StringComparison.Ordinal))
        {
            ev = QuestEventType.Collected;
            filter = Filters.Gather;
            live = true;
            return;
        }

        if (id.StartsWith("daily_butchery", StringComparison.Ordinal))
        {
            ev = QuestEventType.Collected;
            filter = Filters.Carcass;
            live = true;
            return;
        }

        if (id.StartsWith("daily_farming", StringComparison.Ordinal))
        {
            ev = QuestEventType.Farmed;
            live = true;
            return;
        }

        unknown = "Missão diária sem correspondência de QuestEventType";
        _ = description;
        _ = goal;
    }

    /// <summary>
    /// Does this gameplay event advance <paramref name="def"/>?
    /// <paramref name="detail"/> is carcass/gather for Collected, or the recipe
    /// <c>category</c> string for Crafted.
    /// </summary>
    public static bool Matches(QuestDef def, QuestEventType ev, string detail, QuestEventContext context = null)
    {
        if (def == null || !def.IsLive || ev == QuestEventType.Invalid || def.Event != ev) return false;
        var objective = def.Objective;
        if (objective != null)
        {
            if (context == null) return false;
            // Snapshot objectives are recalculated from server state, never from a client/event counter.
            if (!string.IsNullOrEmpty(objective.Snapshot)) return false;
            if (objective.Biome >= 0 && context?.Biome != objective.Biome) return false;
            if (objective.Role >= 0 && context?.Role != objective.Role) return false;
            if (objective.EntityTypes?.Length > 0 && (context == null || !objective.EntityTypes.Contains(context.EntityType))) return false;
            if (objective.Recipes?.Length > 0 && (context == null || !objective.Recipes.Contains(context.RecipeId, StringComparer.Ordinal))) return false;
            if (objective.Prototypes?.Length > 0 && (context?.Products == null || !context.Products.Any(p => objective.Prototypes.Contains(p.Prototype, StringComparer.Ordinal)))) return false;
        }
        if (string.IsNullOrEmpty(def.Filter)) return true;
        if (ev == QuestEventType.Collected || ev == QuestEventType.AnimalTamed || ev == QuestEventType.EstateManaged)
        {
            return string.Equals(def.Filter, detail, StringComparison.OrdinalIgnoreCase);
        }
        if (ev == QuestEventType.Crafted)
        {
            return CraftMatches(def.Filter, detail);
        }
        return true;
    }

    public static bool CraftMatches(string filter, string recipeCategory)
    {
        recipeCategory ??= string.Empty;
        if (string.Equals(filter, Filters.Weapon, StringComparison.Ordinal))
            return recipeCategory.StartsWith("weapon", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Tool, StringComparison.Ordinal))
            return recipeCategory.StartsWith("tool", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Clothing, StringComparison.Ordinal))
            return recipeCategory.StartsWith("clothing", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Cook, StringComparison.Ordinal))
            return recipeCategory.StartsWith("cook", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Process, StringComparison.Ordinal))
        {
            return recipeCategory.StartsWith("material_process", StringComparison.OrdinalIgnoreCase) ||
                   recipeCategory.StartsWith("process", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Apply one matching event to an in-memory row. Returns false when the row
    /// should not move (already claimed / already at the goal / not live).
    /// </summary>
    public static bool TryApply(QuestProgressRow row, QuestDef def, int amount)
    {
        if (row == null || def == null || !def.IsLive || amount <= 0) return false;
        if (row.State == QuestState.Finished || row.State == QuestState.ReachTheGoal) return false;
        int goal = Math.Max(1, def.GoalCount);
        int next = Math.Min(goal, row.Progress + amount);
        if (next == row.Progress) return false;
        row.Progress = next;
        row.GoalCount = goal;
        if (row.Progress >= goal) row.State = QuestState.ReachTheGoal;
        else row.State = QuestState.WorkInProgress;
        return true;
    }

    /// <summary>KST (UTC+9) calendar day used as the Daily reset key.</summary>
    public static string CurrentResetDay(DateTimeOffset? utcNow = null)
    {
        DateTimeOffset now = (utcNow ?? DateTimeOffset.UtcNow).ToOffset(KstOffset);
        return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Unix seconds of the next KST midnight — written into <c>QuestToDo.EndAt</c>.</summary>
    public static double NextResetUnix(DateTimeOffset? utcNow = null)
    {
        DateTimeOffset now = (utcNow ?? DateTimeOffset.UtcNow).ToOffset(KstOffset);
        DateTimeOffset next = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, KstOffset).AddDays(1);
        return next.ToUnixTimeSeconds();
    }

    public static bool ShouldResetDaily(string savedDay, DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrEmpty(savedDay)) return false;
        return !string.Equals(savedDay, CurrentResetDay(utcNow), StringComparison.Ordinal);
    }

    static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);
}

public sealed class QuestDef
{
    public string Id;
    public string Category;
    public QuestType Type;
    public string Subject;
    public string Description;
    public int GoalCount;
    public QuestEventType Event;
    public string Filter;
    public bool IsLive;
    public string UnknownReason;
    public bool DisplayOnHud;
    public int Order;
    public int RewardTier = 1;
    public int LevelCategory = -2; // -2 contador; -1 nível do personagem; demais: categoria de skill.
    public QuestObjective Objective;
}

public sealed class QuestObjective
{
    [JsonProperty("event")] public string Event;
    [JsonProperty("goal")] public int Goal;
    [JsonProperty("filter")] public string Filter;
    [JsonProperty("biome")] public int Biome = -1;
    [JsonProperty("role")] public int Role = -1;
    [JsonProperty("entity_types")] public int[] EntityTypes;
    [JsonProperty("recipes")] public string[] Recipes;
    [JsonProperty("prototypes")] public string[] Prototypes;
    [JsonProperty("snapshot")] public string Snapshot;
    [JsonProperty("courses")] public string[] Courses;
}

/// <summary>Metadata captured after a successful action by the server.</summary>
public sealed class QuestEventContext
{
    public int Biome = -1;
    public int Role = -1;
    public int EntityType = -1;
    public string RecipeId;
    public Messages.Item[] Products;
}

/// <summary>Plain progress row used by tests and by <see cref="Durango.Online.Player.QuestStore"/>.</summary>
public sealed class QuestProgressRow
{
    public QuestState State;
    public int Progress;
    public int GoalCount = 1;
}

public sealed class QuestAssetRow
{
    [JsonProperty("quest_type")] public int QuestType;
    [JsonProperty("category")] public string Category;
    [JsonProperty("subject")] public Gettext Subject;
    [JsonProperty("description")] public Gettext Description;
    [JsonProperty("display_on_hud")] public bool DisplayOnHud;
    [JsonProperty("auto_finish")] public bool AutoFinish;
    [JsonProperty("order")] public int Order;
    [JsonProperty("icon")] public string Icon;
}
