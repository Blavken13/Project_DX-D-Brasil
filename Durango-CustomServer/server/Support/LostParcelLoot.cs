using System;
using System.Collections.Generic;
using System.Linq;
using Yaml;

namespace Durango.Online;

/// <summary>Loot de caixas naturais; os resultados são fixos por entidade para que
/// reabrir o menu ou reconectar não altere o conteúdo da mesma caixa.</summary>
internal static class LostParcelLoot
{
    // A wiki lista 6 itens para a caixa de envio, 4 para roupas e 23 para sementes.
    // Incluímos também os pools de caixas adicionais que já existem nos dados deste servidor.
    private static readonly Dictionary<string, string[]> Pools = new(StringComparer.Ordinal)
    {
        ["paperbox_season2"] = new[] { "metal_stick", "nail_metal", "metal_connection", "metal_set", "petbottle", "s02_shoes_highheel", "lock" },
        ["paperbox_normal_mixed"] = new[] { "fabric_plastic", "medicine_modern_01", "sheet_plastic", "corn_seed", "cotton_seed", "onion_seed", "potato_seed", "tomato_seed", "wheat_seed", "lock" },
        ["woodenbox_beginner"] = new[] { "fabric_plastic", "medicine_modern_01", "sheet_plastic", "corn_seed", "cotton_seed", "onion_seed", "potato_seed", "tomato_seed", "wheat_seed", "lock" },
        ["woodenbox_seed"] = new[]
        {
            "onion_seed", "flax_seed", "carnation_seed", "dogrose_seed", "lavender_seed", "cotton_seed",
            "sugarcane_seed", "mushroom_seed", "fig_seed", "coffee_seed", "desertagave_seed", "banana_seed",
            "teatree_seed", "violettulip_seed", "chilipepper_seed", "pepper_seed", "flax_tropical_seed",
            "cherryblossom_01_seed", "watermelon_01_seed", "taro_seed", "persimmontree_seed", "cosmos_seed",
            "mapletree_seed", "ginkgotree_seed", "lock"
        },
        ["woodenbox_weapon"] = new[] { "sword_onehand_loose_stone", "axe_onehand_loose_stone", "lock" },
        ["woodenbox_clothes"] = new[]
        {
            "clothes_recruiter_common", "clothes_recruiter_lead", "clothes_tube",
            "clothes_northernface", "lock"
        }
    };

    public static bool IsPackage(string collectibleId) => Pools.ContainsKey(collectibleId ?? "");

    public static string[] Candidates(string collectibleId) =>
        Pools.TryGetValue(collectibleId ?? "", out string[] pool)
            ? pool.Where(id => PrototypeYaml.GetItemPrototype(id) != null).Distinct(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    /// <summary>Returns 1–2 distinct, uniformly selected valid entries for one box.</summary>
    public static string[] For(string collectibleId, string entityId)
    {
        string[] valid = Candidates(collectibleId);
        if (valid.Length == 0) return Array.Empty<string>();
        Random random = CreateRandom(collectibleId + "|" + entityId);
        for (int i = valid.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (valid[i], valid[j]) = (valid[j], valid[i]);
        }
        int count = random.Next(1, Math.Min(2, valid.Length) + 1);
        return valid.Take(count).ToArray();
    }

    /// <summary>Each selected entry yields 2–4 units, selected once per box and item.</summary>
    public static int Quantity(string collectibleId, string entityId, string prototypeId)
    {
        Random random = CreateRandom(collectibleId + "|" + entityId + "|" + prototypeId);
        return random.Next(2, 5);
    }

    private static Random CreateRandom(string value)
    {
        // Stable FNV-1a seed (String.GetHashCode is intentionally process-randomized).
        uint hash = 2166136261;
        foreach (char c in value ?? "") { hash ^= c; hash *= 16777619; }
        return new Random(unchecked((int)hash));
    }
}
