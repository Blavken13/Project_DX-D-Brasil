using System;
using System.Linq;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public partial class Player
{
    private static JObject _equipmentCapacityTags;
    private static int EquipmentInventoryBonus(PlayerContext context)
    {
        if (context?.EquippedItems == null || context.InventoryItems == null) return 0;
        double bonus = 0;
        foreach (string id in context.EquippedItems.Values.Distinct())
        {
            int index = context.InventoryItems.FindIndex(item => item.Id == id);
            if (index < 0) continue;
            var item = context.InventoryItems[index];
            // Use the saved performance: pocket/reform tags modify bag_size.
            var performance = (item.Performance ?? Array.Empty<Messages.Performance>()).Select(block =>
                new Messages.Performance { Id = block.Id, Strs = block.Strs,
                    Nums = block.Nums == null ? null : new(block.Nums) }).ToList();
            ItemPerformance.MergeInto(performance, item.Prototype, item.Level);
            double itemBonus = 0;
            foreach (var block in performance)
            {
                string field = block.Id == "armor" ? "bag_size" : block.Id == "modifiers" ? "carry_capacity" : null;
                if (field != null && block.Nums?.TryGetValue(field, out float value) == true && float.IsFinite(value))
                    itemBonus += Math.Max(0, value);
            }
            // Prototype/loot pocket tags are not recorded as crafting modifications.
            // Those already applied by reform must not be counted twice.
            _equipmentCapacityTags ??= Json.ReadFromFile<JObject>("tags");
            foreach (var tag in item.Tags ?? Array.Empty<Messages.Tag>())
            {
                if (item.TagModifications?.Any(t => t.Id == tag.Id) == true) continue;
                var definition = _equipmentCapacityTags?[tag.Id];
                if ((string)definition?["required_performance"] != "armor" || !performance.Any(p => p.Id == "armor")) continue;
                var modifier = definition?["modifiers"]?["bag_size"];
                if ((string)modifier?["function"] == "incr" &&
                    StatFormula.TryEval((string)modifier["formula"], "level", tag.Level, out double extra) && double.IsFinite(extra))
                    itemBonus += Math.Max(0, extra);
            }
            bonus += itemBonus;
        }
        return (int)Math.Min(int.MaxValue, Math.Floor(bonus));
    }
}
