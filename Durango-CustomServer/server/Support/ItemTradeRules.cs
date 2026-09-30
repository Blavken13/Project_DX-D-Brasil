using System.Collections.Generic;
using Messages;
using Yaml;

namespace Durango.Online;

public static class ItemTradeRules
{
    public static bool PrototypeCanTrade(string id)
    {
        var prototype = PrototypeYaml.GetItemPrototype(id);
        // O dump de prototypes não inclui PrototypePreset.trade_locked. Preservar
        // os bloqueios explícitos disponíveis: cápsula vinculada, avatar e itens protegidos.
        return prototype != null && !prototype.TradeLocked && !prototype.DumpLocked &&
            id != "trade_locked_artifact_capsule" && prototype.Tags?.ContainsKey("equipment_avatar") != true;
    }

    public static bool CanTrade(Item item) => item.Tradable && PrototypeCanTrade(item.Prototype);

    public static void Migrate(PlayerContext context)
    {
        if (context.MarketTradabilityVersion >= 1) return;
        Normalize(context.InventoryItems);
        foreach (var pet in context.Pets) if (pet?.Bag != null) Normalize(pet.Bag);
        context.MarketTradabilityVersion = 1;
    }

    public static void Migrate(WorldContext context)
    {
        if (context.MarketTradabilityVersion >= 1) return;
        if (context.Warehouses != null)
            foreach (var box in context.Warehouses.Values)
                if (box?.Sections != null)
                    foreach (var items in box.Sections.Values) if (items != null) Normalize(items);
        context.MarketTradabilityVersion = 1;
    }

    private static void Normalize(IList<Item> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.Tradable = PrototypeCanTrade(item.Prototype);
            items[i] = item;
        }
    }
}
