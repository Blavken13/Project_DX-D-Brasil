using Durango.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Yaml.Util;

namespace Durango.Online;

// Fontes originais: purchaser/vouchers e o cliente ArtifactInteractions.
// Quantidades: balanceamento configuravel do alfa, pois a tabela Nexon nao foi recuperada.
public static class InductionRewardTuning
{
    public sealed class ScoreBonus
    {
        [JsonProperty("score")] public int Score;
        [JsonProperty("stones")] public int Stones;
    }
    private sealed class Config
    {
        [JsonProperty("daily_quest")] public int DailyQuest = 2;
        [JsonProperty("achievement_per_tier")] public int AchievementPerTier = 2;
        [JsonProperty("achievement_max")] public int AchievementMax = 12;
        [JsonProperty("attendance_days")] public int AttendanceDays = 28;
        [JsonProperty("attendance_daily")] public int AttendanceDaily = 5;
        [JsonProperty("attendance_bonus")] public int AttendanceBonus = 20;
        [JsonProperty("daily_score_bonuses")] public ScoreBonus[] ScoreBonuses = Array.Empty<ScoreBonus>();
    }
    private static Config _config;
    private static int? _maximum;
    private static Config Settings => _config ??= Json.ReadFromFile<Config>("quests/induction_rewards_server") ?? new();
    public static void Load() { _config = null; _maximum = null; }
    public static int Maximum => _maximum ??= Math.Clamp((int?)Json.ReadFromFile<JObject>("purchaser/vouchers")?[CrackTuning.VoucherId]?["count_max"] ?? 240, 1, 1_000_000);
    public static int AttendanceDays => Math.Clamp(Settings.AttendanceDays, 1, 31);
    public static int AttendanceDaily => Math.Clamp(Settings.AttendanceDaily, 0, Maximum);
    public static int AttendanceBonus => Math.Clamp(Settings.AttendanceBonus, 0, Maximum);
    public static int ForQuest(QuestDef def) => def?.IsLive != true ? 0 : def.Category == QuestCatalog.DailyCategory
        ? Math.Clamp(Settings.DailyQuest, 0, Maximum)
        : (int)Math.Clamp((long)Settings.AchievementPerTier * Math.Max(1, def.RewardTier), 0, Math.Clamp(Settings.AchievementMax, 0, Maximum));
    public static ScoreBonus[] ScoreBonuses => (Settings.ScoreBonuses ?? Array.Empty<ScoreBonus>()).Where(b => b != null && b.Score > 0 && b.Stones > 0 && b.Stones <= Maximum)
        .GroupBy(b => b.Score).Select(g => g.First()).OrderBy(b => b.Score).ToArray();
}
