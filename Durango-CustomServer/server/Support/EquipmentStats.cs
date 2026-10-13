using System;
using System.Linq;
using Messages;

namespace Durango.Online;

internal static class EquipmentStats
{
    internal static float Value(Item item, string block, string field)
    {
        ItemCraftModifications.Recalculate(ref item);
        float value = item.Performance?.FirstOrDefault(p => p.Id == block).Nums?.GetValueOrDefault(field) ?? 0;
        return float.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}
