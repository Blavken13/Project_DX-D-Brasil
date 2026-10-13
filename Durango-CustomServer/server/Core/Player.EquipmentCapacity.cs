using System;
using System.Linq;

namespace Durango.Online;

public partial class Player
{
    private static int EquipmentInventoryBonus(PlayerContext context)
    {
        if (context?.EquippedItems == null || context.InventoryItems == null) return 0;
        double bonus = 0;
        foreach (string id in context.EquippedItems.Values.Distinct())
        {
            int index = context.InventoryItems.FindIndex(item => item.Id == id);
            if (index < 0) continue;
            var item = context.InventoryItems[index];
            bonus += EquipmentStats.Value(item, "armor", "bag_size") + EquipmentStats.Value(item, "modifiers", "carry_capacity");
        }
        return (int)Math.Min(int.MaxValue, Math.Floor(bonus));
    }
}
