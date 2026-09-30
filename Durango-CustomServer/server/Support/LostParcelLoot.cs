using System;
using System.Collections.Generic;
using Yaml;

namespace Durango.Online;

internal static class LostParcelLoot
{
    // As tabelas de loot dos pacotes não vieram no servidor. Usar somente
    // protótipos existentes, sem liberar equipamento de eventos ou premium.
    private static readonly string[] Seeds = { "corn_seed", "cotton_seed", "onion_seed", "potato_seed", "tomato_seed", "wheat_seed" };
    private static readonly string[] Supplies = { "fabric_plastic", "medicine_modern_01", "sheet_plastic" };
    private static readonly string[] Weapons = { "sword_onehand_loose_stone", "axe_onehand_loose_stone" };
    private static readonly string[] Clothes = { "clothes_leaf_01", "clothes_straw_01" };

    public static IEnumerable<string> For(string collectibleId)
    {
        string[] pool = collectibleId switch
        {
            "paperbox_normal_mixed" or "paperbox_season2" or "woodenbox_beginner" => Supplies,
            "woodenbox_seed" => Seeds,
            "woodenbox_weapon" => Weapons,
            "woodenbox_clothes" => Clothes,
            _ => Array.Empty<string>()
        };
        foreach (string id in pool) if (PrototypeYaml.GetItemPrototype(id) != null) yield return id;
        if (collectibleId == "paperbox_normal_mixed")
            foreach (string id in Seeds) if (PrototypeYaml.GetItemPrototype(id) != null) yield return id;
    }
}
