using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Yaml;

namespace Durango.Online;

public static class ItemDurability
{
    // O pacote original não contém os presets de durabilidade. A capacidade
    // mantém os usos por material já adotados pelo servidor, na unidade das
    // deltas nativas de constants.json, em vez de expor 1 ponto ao cliente.
    public static float Maximum(string prototypeId, int level)
    {
        var prototype = PrototypeYaml.GetItemPrototype(prototypeId, level) ?? PrototypeYaml.GetItemPrototype(prototypeId);
        if (prototype == null || !IsEquipment(prototype)) return 1f;
        string id = prototypeId.ToLowerInvariant();
        int tier = id.Contains("metal") || id.Contains("brass") || id.Contains("iron") || id.Contains("steel") ? 3
            : id.Contains("bone") || id.Contains("horn") || id.Contains("tusk") ? 2 : 1;
        if (IsArmor(prototype) && tier == 1 &&
            !(prototype.SubCategories?.Contains("clothes_novice") ?? false)) tier = 2;
        return (40 * tier + Math.Clamp(level, 1, 70) - 1) * Delta("collect", 1.6f);
    }

    public static float Maximum(Item item)
    {
        float maximum = Maximum(item.Prototype, item.Level);
        if (maximum <= 1) return maximum;
        int density = (item.Tags ?? Array.Empty<Tag>()).Where(tag => tag.Id == "density_high")
            .Sum(tag => Math.Max(0, tag.Level));
        return Math.Max(1, maximum + density * 10f);
    }

    // equipment_avatar identifies decorative outfits in the original tables.
    public static bool IsArmor(Prototype prototype) => prototype != null &&
        !(prototype.Tags?.ContainsKey("equipment_avatar") ?? false) &&
        (prototype.Category == "clothing" || (prototype.Tags?.ContainsKey("armor") ?? false));

    public static bool IsArmor(string prototypeId, int level) => IsArmor(
        PrototypeYaml.GetItemPrototype(prototypeId, level) ?? PrototypeYaml.GetItemPrototype(prototypeId));

    private static bool IsEquipment(Prototype prototype) =>
        IsArmor(prototype) || prototype.Category is "weapon/tool" or "weapon" or "tool" ||
        prototype.Tags != null && (prototype.Tags.ContainsKey("weapon") || prototype.Tags.ContainsKey("axe") ||
            prototype.Tags.ContainsKey("knife") || prototype.Tags.ContainsKey("hammer") ||
            prototype.Tags.ContainsKey("pickaxe") || prototype.Tags.ContainsKey("shovel") ||
            prototype.Tags.ContainsKey("sickle") || prototype.Tags.ContainsKey("harpoon"));

    public static Gauge Full(string prototype, int level)
    {
        float maximum = Maximum(prototype, level);
        return new Gauge(maximum, 0f, new[] { new GaugeNode(0, maximum) });
    }

    private static string _dataDir;
    private static JObject _deltas;
    public static float Delta(string action, float fallback = 1.6f)
    {
        if (_dataDir != Json.DataDir)
        {
            _deltas = Json.ReadFromFile<JObject>("constants")?["durability"]?["deltas"] as JObject;
            _dataDir = Json.DataDir;
        }
        return (float?)_deltas?[action] ?? fallback;
    }

    public static bool Normalize(ref Item item)
    {
        float maximum = Maximum(item);
        if (maximum <= 1) return false;
        float previousMaximum = item.Durability?.Max() ?? 0;
        if (previousMaximum > 1 && Math.Abs(previousMaximum - maximum) < 0.01f) return false;
        float fraction = previousMaximum > 0
            ? Math.Clamp(item.Durability.Get() / previousMaximum, 0, 1) : 1;
        item.Durability = new Gauge(maximum, 0f, new[] { new GaugeNode(0, maximum * fraction) });
        return true;
    }

    public static void Normalize(List<Item> items)
    {
        if (items == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Normalize(ref item)) items[i] = item;
        }
    }
}
