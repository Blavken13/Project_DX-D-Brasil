using System;
using System.Linq;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DurangoServerNx;

internal static class GatheredPropertiesCheck
{
    internal static bool Levels(Item item, int level) => item.Tags.All(t =>
        (string)Durango.Utils.Json.ReadFromFile<JObject>("tags")[t.Id]?["type"] == "minor"
            ? t.Level >= 1 && t.Level <= level : t.Level == level);

    internal static void Run(Action<bool, string> check)
    {
        var config = Durango.Utils.Json.ReadFromFile<JObject>("item/gathered_properties_server");
        var tags = Durango.Utils.Json.ReadFromFile<JObject>("tags");
        foreach (var rule in config["rules"])
        {
            var groups = rule["groups"].Select(g => g.Values<string>().ToArray()).ToArray();
            foreach (string material in rule["materials"].Values<string>())
            foreach (int level in new[] { 1, 10, 60 })
            foreach (bool negative in new[] { false, true })
            {
                var item = Cheats.MakeItem("bone_leg", level).Value;
                item.Tags = new[] { new Tag { Id = material, Level = level } };
                item.TagModifications = Array.Empty<Tag>();
                var original = item;
                int step = 0;
                double Roll() => ++step == 4 && negative ? .99 : 0;
                bool applied = GatheredItemProperties.Apply(ref item, Roll);
                check(applied && item.Tags.Length == (groups.Length > 1 ? 3 : 2) &&
                    item.Tags[0].Id == material && item.Tags[0].Level == level &&
                    item.Id == original.Id && item.Level == level &&
                    item.ModifiedCount == original.ModifiedCount && item.ModifiableCount == original.ModifiableCount &&
                    item.TagModifications.All(t => (string)tags[t.Id]["type"] == "minor" &&
                        t.Level >= 1 && t.Level <= Math.Min(3, level)) &&
                    groups.All(g => item.Tags.Count(t => g.Contains(t.Id)) <= 1),
                    $"sorteio {material}/{level}/{negative}: propriedades, identidade e limites");
                var loaded = JsonConvert.DeserializeObject<Item>(JsonConvert.SerializeObject(item));
                ItemCraftModifications.Initialize(ref loaded);
                check(item.Tags.All(t => loaded.Tags.Any(l => l.Id == t.Id && l.Level == t.Level)),
                    "salvar/carregar preserva propriedades sorteadas");
            }
        }
        var bone = Cheats.MakeItem("bone_leg", 60).Value;
        string before = JsonConvert.SerializeObject(bone);
        check(!GatheredItemProperties.Apply(ref bone, () => .99) && before == JsonConvert.SerializeObject(bone),
            "coleta sem sorte preserva integralmente o item");
        check(GatheredItemProperties.Apply(ref bone, () => .5, 2), "bonus aumenta chance de propriedade");
        bone.Tags = new[] { new Tag { Id = "bone", Level = 60 }, new Tag { Id = "hardness_hard", Level = 2 },
            new Tag { Id = "weight_light", Level = 2 }, new Tag { Id = "density_high", Level = 2 } };
        before = JsonConvert.SerializeObject(bone);
        check(!GatheredItemProperties.Apply(ref bone, () => 0) && before == JsonConvert.SerializeObject(bone),
            "propriedades fixas nao sao substituidas nem recebem opostos");
        var random = new System.Random(4127);
        var seen = new System.Collections.Generic.HashSet<string>();
        int successes = 0;
        for (int i = 0; i < 2000; i++)
        {
            bone = Cheats.MakeItem("bone_leg", 60).Value;
            if (GatheredItemProperties.Apply(ref bone, random.NextDouble)) successes++;
            foreach (var tag in bone.TagModifications ?? Array.Empty<Tag>()) seen.Add(tag.Id);
        }
        check(new[] { "hardness_hard", "hardness_soft", "weight_light", "weight_heavy", "density_high", "density_low" }
            .All(seen.Contains) && successes > 600 && successes < 800,
            "amostra reproduzivel gera todos os buffs/debuffs dos ossos com chance configurada");
    }
}
