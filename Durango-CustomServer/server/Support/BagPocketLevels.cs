using System;
using System.Linq;
using System.Text.RegularExpressions;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

internal static class BagPocketLevels
{
    private static JObject _prototypes;
    internal static int Of(string prototype, int level)
    {
        _prototypes ??= Json.ReadFromFile<JObject>("item/prototype_data");
        var rows = _prototypes?[prototype] as JArray;
        var row = rows?.OfType<JObject>().FirstOrDefault(r => ((int?)r["min_level"] ?? 1) <= level &&
            level <= ((int?)r["max_level"] ?? 60)) ?? rows?.OfType<JObject>().FirstOrDefault();
        string expression = (string)row?["tags"]?["pocket"];
        if (StatFormula.TryEval(expression, "level", level, out double direct))
            return Math.Clamp((int)direct, 1, 100);
        if (expression?.StartsWith("range_lookup(", StringComparison.Ordinal) == true)
        {
            var points = Regex.Matches(expression, @"\(\s*(\d+)\s*,\s*(\d+)\s*\)")
                .Select(m => (Level: int.Parse(m.Groups[1].Value), Value: int.Parse(m.Groups[2].Value)))
                .OrderBy(p => p.Level).ToArray();
            if (points.Length > 0)
            {
                // range_lookup selects the last threshold reached by the item.
                return Math.Clamp(points.LastOrDefault(p => p.Level <= level, points[0]).Value, 1, 100);
            }
        }
        return Math.Clamp(level, 1, 100);
    }
}
