using System;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

// Pools e probabilidades do alfa brasileiro, usando propriedades minor nativas.
// A tabela de sorteio do servidor original não está presente nos recursos do cliente.
public static class GatheredItemProperties
{
    private sealed class Rule
    {
        [JsonProperty("materials")] public string[] Materials { get; set; }
        [JsonProperty("groups")] public string[][] Groups { get; set; }
    }
    private sealed class Config
    {
        [JsonProperty("chance")] public double Chance { get; set; }
        [JsonProperty("second_property_chance")] public double SecondChance { get; set; }
        [JsonProperty("max_intensity")] public int MaxIntensity { get; set; } = 1;
        [JsonProperty("rules")] public Rule[] Rules { get; set; }
    }
    private static Config _config;
    private static JObject _tags;
    private static void Load()
    {
        _config ??= Json.ReadFromFile<Config>("item/gathered_properties_server") ?? new();
        _tags ??= Json.ReadFromFile<JObject>("tags") ?? new();
    }
    private static double Roll(Func<double> random) => Math.Clamp(random(), 0, Math.BitDecrement(1d));
    private static int Pick(int count, Func<double> random) => (int)(Roll(random) * count);
    private static bool Valid(string id) => id != null && (string)_tags[id]?["type"] == "minor" &&
        (bool?)_tags[id]?["visible"] == true && (int?)_tags[id]?["max_level"] > 0;

    public static bool Apply(ref Item item, Func<double> random, double chanceBonus = 0)
    {
        Load();
        if (random == null || item.Level <= 0 || item.Tags == null) return false;
        var existing = item.Tags.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var rule = (_config.Rules ?? Array.Empty<Rule>()).FirstOrDefault(r =>
            r.Materials?.Any(existing.Contains) == true);
        if (rule == null) return false;
        // Não substitui propriedades fixas nem adiciona seus opostos.
        var groups = (rule.Groups ?? Array.Empty<string[]>()).Where(g => g?.Length > 0 &&
            g.All(Valid) && !g.Any(existing.Contains)).ToList();
        if (groups.Count == 0) return false;
        double bonus = double.IsFinite(chanceBonus) ? chanceBonus : 0;
        double chance = Math.Clamp(_config.Chance * Math.Max(0, 1 + bonus), 0, 1);
        if (Roll(random) >= chance) return false;
        int count = groups.Count > 1 && Roll(random) < Math.Clamp(_config.SecondChance, 0, 1) ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            int index = Pick(groups.Count, random);
            var group = groups[index]; groups.RemoveAt(index);
            string id = group[Pick(group.Length, random)];
            int max = Math.Min(Math.Max(1, _config.MaxIntensity), Math.Min(item.Level, (int)_tags[id]["max_level"]));
            var tag = new Tag { Id = id, Level = 1 + Pick(max, random) };
            item.Tags = item.Tags.Append(tag).ToArray();
            item.TagModifications = (item.TagModifications ?? Array.Empty<Tag>()).Append(tag).ToArray();
        }
        return true;
    }
}
