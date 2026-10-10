using System;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

internal static class EquipmentStats
{
    private static JObject _tags;
    internal static float Value(Item item, string block, string field)
    {
        var saved = item.Performance?.FirstOrDefault(p => p.Id == block).Nums;
        float value = saved?.TryGetValue(field, out float stored) == true ? stored :
            ItemPerformance.Of(item.Prototype, item.Level).FirstOrDefault(p => p.Id == block).Nums?.GetValueOrDefault(field) ?? 0;
        _tags ??= Json.ReadFromFile<JObject>("tags");
        foreach (var tag in (item.Tags ?? Array.Empty<Tag>()).OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            if (item.TagModifications?.Any(t => t.Id == tag.Id) == true) continue;
            var definition = _tags?[tag.Id];
            if ((string)definition?["required_performance"] != block) continue;
            var modifier = definition?["modifiers"]?[field];
            if (!StatFormula.TryEval((string)modifier?["formula"], "level", tag.Level, out double extra)) continue;
            value = (string)modifier?["function"] switch {
                "ratio" => value * (float)extra, "set" => (float)extra,
                "decr" => value - (float)extra, _ => value + (float)extra };
        }
        return float.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}
