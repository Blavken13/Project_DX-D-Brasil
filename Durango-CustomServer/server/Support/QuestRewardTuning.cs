using Durango.Utils;
using Newtonsoft.Json;
using Shared.Quest;
using Yaml.Util;

namespace Durango.Online;

// Valores do alfa brasileiro, configuráveis. A tabela de pagamentos da Nexon
// não está em quests_for_client; não confundir esta progressão com valores recuperados.
public static class QuestRewardTuning
{
    public sealed class Band
    {
        [JsonProperty("level")] public int Level;
        [JsonProperty("t_stones")] public long TStones;
    }
    public sealed class Tier
    {
        [JsonProperty("tier")] public int Number;
        [JsonProperty("t_stones")] public long TStones;
        [JsonProperty("exp_multiplier")] public int ExpMultiplier;
    }
    private sealed class Config
    {
        [JsonProperty("daily")] public Band[] Daily { get; set; }
        [JsonProperty("achievements")] public Tier[] Achievements { get; set; }
    }
    private static Config _config;
    public static void Load()
    {
        _config = Json.ReadFromFile<Config>("quests/rewards_server") ?? new Config();
    }
    public static (long TStones, int ExpWeight) For(QuestDef quest, int playerLevel)
    {
        if (quest?.IsLive != true) return (0, 0);
        if (_config == null) Load();
        if (quest.Type == QuestType.Daily)
        {
            var band = (_config.Daily ?? Array.Empty<Band>()).Where(b => b.Level <= Math.Max(1, playerLevel))
                .OrderByDescending(b => b.Level).FirstOrDefault();
            return (Math.Clamp(band?.TStones ?? 100, 1, 99_999_999), SkillTuning.QuestClaimWeight);
        }
        var tier = (_config.Achievements ?? Array.Empty<Tier>()).Where(t => t.Number <= Math.Max(1, quest.RewardTier))
            .OrderByDescending(t => t.Number).FirstOrDefault();
        return (Math.Clamp(tier?.TStones ?? 250, 1, 99_999_999),
            SkillTuning.QuestClaimWeight * Math.Clamp(tier?.ExpMultiplier ?? 1, 1, 12));
    }
}
