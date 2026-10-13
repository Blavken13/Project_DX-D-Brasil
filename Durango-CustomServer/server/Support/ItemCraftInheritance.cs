using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

internal static class ItemCraftInheritance
{
    private static JObject _tags;
    internal static void Apply(ref Item product, CraftRecipeData recipe, Dictionary<string, Item[]> slots)
    {
        _tags ??= Json.ReadFromFile<JObject>("tags");
        var inherited = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var material in slots.Values.SelectMany(v => v).DistinctBy(m => m.Id))
            foreach (var tag in material.Tags ?? Array.Empty<Tag>())
            {
                var definition = _tags[tag.Id];
                // Structural tags (metal, knife, eatable, etc.) describe the output prototype.
                if (((string)definition?["type"] != "minor" && tag.Id != "purity_high") || tag.Id == "raw_food" || tag.Id.StartsWith("reform_")) continue;
                int intensity = (string)definition?["type"] == "major" ? Math.Min(tag.Level, product.Level) : tag.Level;
                inherited[tag.Id] = Math.Max(inherited.GetValueOrDefault(tag.Id), intensity);
            }
        var tags = (product.Tags ?? Array.Empty<Tag>()).ToDictionary(t => t.Id, t => t.Level);
        var changes = new Dictionary<string, int>();
        foreach (var tag in inherited)
        {
            tags[tag.Key] = Math.Max(tags.GetValueOrDefault(tag.Key), tag.Value);
            changes[tag.Key] = tags[tag.Key];
        }
        product.Tags = tags.Select(t => new Tag { Id = t.Key, Level = t.Value }).ToArray();
        product.TagModifications = changes.Select(t => new Tag { Id = t.Key, Level = t.Value }).ToArray();
        foreach (var color in recipe.replace_color ?? new())
        {
            string[] source = color.Value.Split('.');
            if (source.Length != 2) continue;
            Item material = slots.GetValueOrDefault(source[0])?.FirstOrDefault() ?? default;
            if (material.Id == null) material = slots.Values.SelectMany(v => v).FirstOrDefault(m =>
                m.Tags?.Any(t => t.Id == source[0]) == true);
            string value = source[1] switch { "color_r" => material.ColorR, "color_g" => material.ColorG,
                "color_b" => material.ColorB, _ => null };
            if (string.IsNullOrEmpty(value)) continue;
            if (color.Key == "0") product.ColorR = value; else if (color.Key == "1") product.ColorG = value;
            else if (color.Key == "2") product.ColorB = value;
        }
        ItemCraftModifications.Recalculate(ref product);
    }
}
