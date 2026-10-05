using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Item;
using Yaml;

namespace Durango.Online;

/// <summary>
/// Processing changes the base item, not its level or identity. Material/shape tags
/// follow recipes.json and tags.json; reform effects/ranges come from tech_support.json.
/// The offline server uses three processing uses and two equipment reform slots.
/// </summary>
public static class ItemCraftModifications
{
    private static Dictionary<string, CraftRecipeData> _recipes;
    private static JObject _reforms;
    private static JObject _tags;
    private static void Load()
    {
        _recipes ??= Json.ReadFromFile<Dictionary<string, CraftRecipeData>>("item/recipes");
        _reforms ??= Json.ReadFromFile<JObject>("item/tech_support");
        _tags ??= Json.ReadFromFile<JObject>("tags");
    }

    private static bool Has(Item item, string id) => item.Tags?.Any(t => t.Id == id) == true;
    private static bool Matches(Item item, Dictionary<string, int> filter) => filter == null || filter.Count == 0 ||
        filter.Any(f => item.Tags?.Any(t => t.Id == f.Key && t.Level >= f.Value) == true);

    public static bool Initialize(ref Item item)
    {
        Load(); bool changed = false; Item current = item;
        // Never replenish an item which has already used its processing allowance.
        if (item.ModifiableCount == 0 && item.ModifiedCount == 0 && !Has(item, "eatable") &&
            _recipes.Values.Any(r => r.type == CraftType.Modify && r.deduct_modifiable_count &&
                r.category == "material_process" && r.slots?.FirstOrDefault(s => s.slot_id == "base") is { } slot &&
                Matches(current, slot.required_tags) && Matches(current, slot.required_materials)))
        { item.ModifiableCount = 3; changed = true; }
        if (item.ReformSlots == null && !Has(item, "not_able_to_reform") &&
            _recipes.Values.Any(r => r.type == CraftType.Reform &&
                r.slots?.FirstOrDefault(s => s.slot_id == "base") is { } slot &&
                Matches(current, slot.required_tags) && Matches(current, slot.required_materials)))
        {
            item.ReformSlots = new[] { new ReformSlot { Index = 0 }, new ReformSlot { Index = 1 } };
            changed = true;
        }
        return changed;
    }

    public static void Normalize(List<Item> items)
    {
        if (items == null) return;
        for (int i = 0; i < items.Count; i++) { Item item = items[i]; if (Initialize(ref item)) items[i] = item; }
    }

    private static void SetTag(ref Item item, string id, int level, params string[] remove)
    {
        Load();
        int cap = (int?)_tags[id]?["max_level"] ?? 100;
        var tag = new Tag { Id = id, Level = Math.Clamp(level, 1, cap) };
        item.Tags = (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id != id && !remove.Contains(t.Id)).Append(tag).ToArray();
        item.TagModifications = (item.TagModifications ?? Array.Empty<Tag>())
            .Where(t => t.Id != id && !remove.Contains(t.Id)).Append(tag).ToArray();
    }
    private static void Increase(ref Item item, string id, int amount = 1) =>
        SetTag(ref item, id, (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id == id).Select(t => t.Level).DefaultIfEmpty(0).Max() + amount);

    public static bool Apply(ref Item item, string id, CraftRecipeData recipe,
        Dictionary<string, Item[]> materials, int? reformIndex, out string error)
    {
        Load(); error = null;
        if (recipe.type == CraftType.Reform)
        {
            if (!Reform(ref item, id, reformIndex, out error)) return false;
            ApplyPerformance(ref item); return true;
        }
        if (id.StartsWith("dye_color_") || id.StartsWith("bleach_color_"))
            return Dye(ref item, id, recipe, materials, out error);
        if (recipe.category == "cook" || id is "s02_skewer" or "s02_cooking")
        {
            Cook(ref item, id);
            ApplyPerformance(ref item);
            return true;
        }

        if (id is "extend_rope" or "extend_stick" or "s02_extend_stick" or "extend_sheet")
        {
            string prefix = id == "extend_rope" ? "string_" : id == "extend_sheet" ? "sheet_" : "stick_";
            SetTag(ref item, prefix + (id == "extend_sheet" ? "wide" : "long"), item.Level,
                prefix + "short", prefix + "normal", prefix + "narrow");
        }
        else if (id.StartsWith("fertilizer_boost"))
        {
            Increase(ref item, "fertilizer_boost");
            string[] crops = id.EndsWith("02_1") ? new[] { "fruit", "grain", "vegetable" } :
                id.EndsWith("02_2") ? new[] { "wood", "leaf", "flower", "fiber" } : Array.Empty<string>();
            foreach (string crop in crops) Increase(ref item, "boost_crop_" + crop);
        }
        else if (id == "dry")
        {
            SetTag(ref item, "dried_leather", item.Level);
            if (Has(item, "leather_dire_snow")) SetTag(ref item, "dried_leather_dire_snow", item.Level);
        }
        else if (id.StartsWith("tan"))
        {
            SetTag(ref item, "tan_leather", item.Level, "dried_leather");
            if (Has(item, "dried_leather_dire_snow")) SetTag(ref item, "tan_leather_dire_snow", item.Level, "dried_leather_dire_snow");
            foreach (string property in id switch {
                "tan_02_t2" => new[] { "weight_light", "fiber_tender" },
                "tan_02" => new[] { "weight_light" }, "tan_03" => new[] { "water_proof" },
                "tan_snowfield" => new[] { "fleeciness" }, "tan_04" => new[] { "fiber_tough" },
                _ => new[] { "fiber_tough" } }) Increase(ref item, property);
        }
        else if (id is "trim" || id.StartsWith("process"))
        {
            foreach (string material in new[] { "wood", "bone", "stone" })
                if (Has(item, material) || Has(item, "trim_" + material))
                    SetTag(ref item, (id == "trim" ? "trim_" : "process_") + material, item.Level,
                        id == "trim" ? "" : "trim_" + material);
            if (id != "trim") foreach (string property in id switch {
                "process_02" => new[] { "surface_softness" }, "process_03" => new[] { "structure_compact" },
                "process_04" => new[] { "shape_pointed" }, "process_05" => new[] { "weight_light", "hardness_hard" },
                _ => new[] { "density_high" } }) Increase(ref item, property);
        }
        else if (id == "preserved") SetTag(ref item, "dried_rubber", item.Level);
        else if (id == "make_fur") SetTag(ref item, "fur", item.Level);
        else if (id == "fabric_waterproof") Increase(ref item, "water_proof");
        else if (id is "board" or "s02_board" or "board_02" or "board_03")
        {
            // Processing preserves the inventory identity and material properties,
            // but the resulting shape also needs its own native item prototype.
            Item source = item;
            string materialType = new[] { "wood", "bone", "stone", "metal" }
                .FirstOrDefault(material => Has(source, material));
            if (materialType != null)
            {
                string prototypeId = "board_" + materialType;
                Prototype prototype = PrototypeYaml.GetItemPrototype(prototypeId, item.Level);
                if (prototype == null) { error = "O produto desta receita não está disponível."; return false; }
                item.Prototype = prototypeId;
                item.Name = prototype.Name;
                item.Description = prototype.Description;
                item.Icon = prototype.Icon;
                item.SubIcon = null;
                item.Size = prototype.Size;
            }
            SetTag(ref item, "board_normal", item.Level, "pillar_thin", "pillar_normal", "pillar_thick");
            foreach (string material in new[] { "wood", "bone", "stone", "metal" })
                if (Has(item, material)) SetTag(ref item, "board_" + material + "_delimiter", item.Level);
            if (id == "board_02") SetTag(ref item, "board_normal_02", item.Level);
            if (id == "board_03") SetTag(ref item, "board_normal_03", item.Level);
        }
        else if (id == "smelt")
        {
            SetTag(ref item, "smelted_metal", item.Level, "ore"); SetTag(ref item, "metal", item.Level);
        }
        else if (id == "combine_metal") Increase(ref item, "hardness_hard");
        else if (id.StartsWith("refine"))
            Increase(ref item, id switch { "refine" => "purity_high", "refine_02" => "hardness_hard",
                "refine_03" or "refine_03_t2" => "structure_compact", "refine_snowfield" => "cold_resistant",
                "refine_04" => "surface_dense_volcanic", _ => "purity_high" });
        else if (id.StartsWith("jewel_"))
        {
            SetTag(ref item, "jewel_polished", item.Level, "jewel_gemstone");
            var jewelTypes = new[] { "ruby", "sapphire", "emerald", "amethyst", "diamond", "topaz" };
            Item jewel = item;
            int index = Array.FindIndex(jewelTypes, kind => Has(jewel, "jewel_" + kind) || jewel.Prototype.Contains(kind));
            if (index >= 0) Increase(ref item, "jewel_polish_" + (index + 1).ToString("00"));
            else Increase(ref item, "gloss_high");
        }
        else if (id == "re_accuracy_trans_to_critical")
        {
            int accuracy = item.Tags?.Where(t => t.Id == "accuracy_incr").Select(t => t.Level).FirstOrDefault() ?? 0;
            SetTag(ref item, "critical_incr", Math.Max(1, accuracy), "accuracy_incr");
        }
        else { error = "Esta receita não possui um processamento definido."; return false; }
        ApplyPerformance(ref item);
        return true;
    }

    private static void Cook(ref Item item, string id)
    {
        int tier = id.Contains("03") ? 3 : id.Contains("02") ? 2 : 1;
        string method = id == "fry" ? "fried" : id == "fry_stir" ? "fried_stir" :
            id.StartsWith("smoke_food") ? "smoked" : id.StartsWith("dry_food") ||
            id.StartsWith("salt_food") || id.StartsWith("can_food") || id == "ice_drink"
                ? "preserve_energy" : "energy_fire";
        SetTag(ref item, method, tier, "raw_food");
        if (id.StartsWith("dry_food")) SetTag(ref item, "dried", tier);
        if (id.StartsWith("dry_food") || id.StartsWith("smoke_food") ||
            id.StartsWith("salt_food") || id.StartsWith("can_food")) SetTag(ref item, "preserve_power", tier);
        if (id.EndsWith("seasoning") || id.EndsWith("food_02")) SetTag(ref item, "savory_plus", tier);
        if (id is "boil" or "steam" or "soup_01" or "herb_tea_01") SetTag(ref item, "digestive_easy", 2);
        if (id.StartsWith("medicine_")) SetTag(ref item, "medicine_high", tier);
    }

    private static void ApplyPerformance(ref Item item)
    {
        // Recompute modified numeric fields from the prototype baseline, avoiding
        // repeated addition when processing/reforming an item more than once.
        var baseline = ItemPerformance.Of(item.Prototype, item.Level);
        var blocks = (item.Performance ?? Array.Empty<Performance>()).Select(p => new Performance {
            Id = p.Id, Strs = p.Strs == null ? new() : new(p.Strs),
            Nums = p.Nums == null ? new Dictionary<string, float>() : new(p.Nums) }).ToList();
        var effects = new Dictionary<(string Block, string Field), List<(string Function, double Value)>>();
        foreach (var tag in item.TagModifications ?? Array.Empty<Tag>())
        {
            var definition = _tags[tag.Id];
            string block = (string)definition?["required_performance"];
            if (string.IsNullOrEmpty(block) || definition?["modifiers"] is not JObject modifiers) continue;
            foreach (var modifier in modifiers.Properties())
            {
                string formula = (string)modifier.Value["formula"];
                string function = (string)modifier.Value["function"];
                if (function == "set" && formula?.StartsWith("'") == true && formula.EndsWith("'"))
                {
                    int i = blocks.FindIndex(p => p.Id == block);
                    if (i < 0) { blocks.Add(new Performance { Id = block, Nums = new(), Strs = new() }); i = blocks.Count - 1; }
                    blocks[i].Strs[modifier.Name] = formula.Substring(1, formula.Length - 2);
                    continue;
                }
                if (!StatFormula.TryEval(formula, "level", tag.Level, out double value)) continue;
                var key = (block, modifier.Name);
                if (!effects.TryGetValue(key, out var list)) effects[key] = list = new();
                list.Add((function, value));
            }
        }
        foreach (var (key, values) in effects)
        {
            int index = blocks.FindIndex(p => p.Id == key.Block);
            if (index < 0) { blocks.Add(new Performance { Id = key.Block, Nums = new(), Strs = new() }); index = blocks.Count - 1; }
            var original = baseline.FirstOrDefault(p => p.Id == key.Block);
            float value = original.Nums?.GetValueOrDefault(key.Field) ?? 0;
            foreach (var effect in values)
                value = effect.Function switch {
                    "ratio" => value * (float)effect.Value,
                    "set" => (float)effect.Value,
                    "decr" => value - (float)effect.Value,
                    _ => value + (float)effect.Value };
            blocks[index].Nums[key.Field] = value;
        }
        // The server grants nutrition immediately. Translate the original cooking
        // expression bonus into that recovery amount, once, from the raw baseline.
        int foodIndex = blocks.FindIndex(p => p.Id == "food");
        if (foodIndex >= 0 && effects.ContainsKey(("food", "energy_expression")))
        {
            var raw = baseline.FirstOrDefault(p => p.Id == "food");
            float bonus = Math.Max(0, blocks[foodIndex].Nums.GetValueOrDefault("energy_expression") -
                (raw.Nums?.GetValueOrDefault("energy_expression") ?? 0));
            blocks[foodIndex].Nums["energy_potential"] =
                (raw.Nums?.GetValueOrDefault("energy_potential") ?? 0) * (1 + bonus);
            if (blocks[foodIndex].Strs.GetValueOrDefault("effect_on") == "raw_food")
                blocks[foodIndex].Strs["effect_on"] = raw.Strs?.GetValueOrDefault("effect_on") ?? "";
        }
        item.Performance = blocks.ToArray();
    }

    private static bool Reform(ref Item item, string id, int? requested, out string error)
    {
        error = null;
        var slots = item.ReformSlots?.ToArray() ?? Array.Empty<ReformSlot>();
        int index = requested ?? Array.FindIndex(slots, s => string.IsNullOrEmpty(s.RecipeId));
        if (index < 0 || index >= slots.Length || slots[index].Index != index || !string.IsNullOrEmpty(slots[index].RecipeId))
        { error = "Escolha um espaço de melhoria vazio neste equipamento."; return false; }
        if (_reforms[id]?["tags"] is not JObject effects)
        { error = "Esta melhoria não possui efeitos definidos."; return false; }
        // Stable estimate/result using the base level within the original effect ranges.
        int baseLevel = item.Level;
        var additions = effects.Properties().Select(p => new Tag { Id = p.Name, Level = Math.Clamp(
            (int)Math.Ceiling(baseLevel * (int)p.Value["max_level"] / 60d),
            (int)p.Value["min_level"], (int)p.Value["max_level"]) }).ToArray();
        slots[index] = new ReformSlot { Index = index, RecipeId = id, Tags = additions };
        item.ReformSlots = slots;
        foreach (Tag tag in additions) SetTag(ref item, tag.Id,
            (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id == tag.Id).Select(t => t.Level).DefaultIfEmpty(0).Max() + tag.Level);
        return true;
    }

    private static bool Dye(ref Item item, string id, CraftRecipeData recipe, Dictionary<string, Item[]> materials, out string error)
    {
        error = null;
        if (recipe.add_color == null) { error = "Esta tintura não possui uma cor definida."; return false; }
        foreach (var pair in recipe.add_color)
        {
            string target = pair.Value;
            if (target == "dye.color_r")
            {
                if (materials?.GetValueOrDefault("dye")?.FirstOrDefault() is not Item dye || string.IsNullOrEmpty(dye.ColorR))
                { error = "Adicione a tintura primeiro."; return false; }
                target = dye.ColorR;
            }
            string old = pair.Key == "0" ? item.ColorR : pair.Key == "1" ? item.ColorG : item.ColorB;
            string blended = Blend(old, target, recipe.add_color_rate);
            if (pair.Key == "0") item.ColorR = blended; else if (pair.Key == "1") item.ColorG = blended; else item.ColorB = blended;
        }
        return true;
    }
    private static string Blend(string old, string target, float rate)
    {
        if (!int.TryParse(old?.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out int a) ||
            !int.TryParse(target?.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out int b)) return old;
        rate = Math.Clamp(rate, 0, 1);
        int Channel(int shift) => (int)Math.Round(((a >> shift) & 255) * (1 - rate) + ((b >> shift) & 255) * rate);
        return $"{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}";
    }
}
