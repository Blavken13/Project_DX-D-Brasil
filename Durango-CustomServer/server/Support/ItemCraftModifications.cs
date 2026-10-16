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
    private static Dictionary<string, string[]> _modifiedFields;
    private static void Load()
    {
        _recipes ??= Json.ReadFromFile<Dictionary<string, CraftRecipeData>>("item/recipes");
        _reforms ??= Json.ReadFromFile<JObject>("item/tech_support");
        _tags ??= Json.ReadFromFile<JObject>("tags");
        _modifiedFields ??= _tags.Properties().Where(p => !string.IsNullOrEmpty((string)p.Value["required_performance"]) &&
            p.Value["modifiers"] is JObject).GroupBy(p => (string)p.Value["required_performance"])
            .ToDictionary(g => g.Key, g => g.SelectMany(p => ((JObject)p.Value["modifiers"]).Properties().Select(m => m.Name)).Distinct().ToArray());
    }

    private static bool Has(Item item, string id) => item.Tags?.Any(t => t.Id == id) == true;
    private static bool Matches(Item item, Dictionary<string, int> filter) => filter == null || filter.Count == 0 ||
        filter.Any(f => item.Tags?.Any(t => t.Id == f.Key && t.Level >= f.Value) == true);

    public static bool Initialize(ref Item item)
    {
        Load(); bool changed = false; Item current = item;
        if (item.TagModifications?.Any(t => t.Id == "pocket") != true && item.Tags?.Any(t => t.Id == "pocket") == true)
        {
            int pocket = BagPocketLevels.Of(item.Prototype, item.Level);
            // Legacy items may legitimately retain a higher saved intensity.
            // Repair undersized pockets without reducing existing storage.
            if (item.Tags.Any(t => t.Id == "pocket" && t.Level == current.Level && t.Level < pocket))
            {
                item.Tags = item.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = pocket } : t).ToArray();
                changed = true;
            }
        }
        // Atributos major acompanham o nível do material. Repara somente atributos
        // presentes com evidência de processamento; nunca concede propriedades novas.
        foreach (var modification in item.TagModifications ?? Array.Empty<Tag>())
        {
            int expected = TagLevel(item, modification.Id, modification.Level);
            if (item.Level <= 0 || item.ModifiedCount <= 0 || !IsMajorTag(modification.Id) ||
                modification.Level <= 0 || modification.Level >= expected ||
                item.Tags?.Any(t => t.Id == modification.Id && t.Level > 0 && t.Level < expected) != true) continue;
            SetTag(ref item, modification.Id, item.Level);
            changed = true;
        }
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
        var tag = new Tag { Id = id, Level = TagLevel(item, id, level) };
        item.Tags = (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id != id && !remove.Contains(t.Id)).Append(tag).ToArray();
        item.TagModifications = (item.TagModifications ?? Array.Empty<Tag>())
            .Where(t => t.Id != id && !remove.Contains(t.Id)).Append(tag).ToArray();
    }
    private static bool IsMajorTag(string id) => (string)_tags[id]?["type"] == "major";
    private static int TagLevel(Item item, string id, int intensity) =>
        Math.Clamp(IsMajorTag(id) ? item.Level : intensity, 1, (int?)_tags[id]?["max_level"] ?? 100);
    private static void Increase(ref Item item, string id, int amount = 1) =>
        SetTag(ref item, id, (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id == id).Select(t => t.Level).DefaultIfEmpty(0).Max() + amount);

    public static bool Apply(ref Item item, string id, CraftRecipeData recipe,
        Dictionary<string, Item[]> materials, int? reformIndex, out string error)
    {
        Load(); error = null;
        if (recipe.type == CraftType.Reform)
        {
            if (!Reform(ref item, id, recipe, materials, reformIndex, out error)) return false;
            ApplyPerformance(ref item); return true;
        }
        if (id.StartsWith("dye_color_") || id.StartsWith("bleach_color_"))
            return Dye(ref item, id, recipe, materials, out error);
        if (recipe.category?.StartsWith("cook", StringComparison.OrdinalIgnoreCase) == true)
        {
            Cook(ref item, id, recipe);
            ApplyCookAppearance(ref item, id, recipe);
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
        else if (id == "combine_metal")
        {
            // A receita consome o segundo fragmento e transforma a forma do fragmento-base.
            // Sem trocar o protótipo e as tags, o resultado não atende às receitas que
            // exigem um fragmento grande, mesmo depois de aumentar a dureza.
            Prototype prototype = PrototypeYaml.GetItemPrototype("metal_chunk_big");
            if (prototype == null) { error = "O fragmento grande de metal não está disponível."; return false; }
            // Os campos localizados desse protótipo estão incompletos nos dados incluídos;
            // preserve a apresentação do material e atualize sua identidade/tags.
            item.Prototype = "metal_chunk_big";
            item.Name = "Fragmento grande de metal";
            item.Description = "Fragmento grande de metal obtido pelo processamento de dois fragmentos menores.";
            SetTag(ref item, "chunk_big", item.Level, "chunk_normal", "chunk_small");
            Increase(ref item, "hardness_hard");
        }
        else if (id.StartsWith("refine"))
        {
            string property = id switch { "refine" => "purity_high", "refine_02" => "hardness_hard",
                "refine_03" or "refine_03_t2" => "structure_compact", "refine_snowfield" => "cold_resistant",
                "refine_04" => "surface_dense_volcanic", _ => "purity_high" };
            Increase(ref item, property);
        }
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

    private static void ApplyCookAppearance(ref Item item, string id, CraftRecipeData recipe)
    {
        if (!string.IsNullOrEmpty(recipe.icon) && (recipe.icon.StartsWith("cook_") || recipe.icon.StartsWith("recipe_")))
            item.Icon = recipe.icon;
        string method = id switch {
            "boil" => "Cozido", "steam" => "Ao vapor", "fry" => "Frito", "fry_stir" => "Refogado",
            "ice_drink" => "Bebida gelada", "steak_set_meal" => "Jantar de bife",
            "medicine_01" => "Pílula improvisada", "medicine_02" => "Pílula normal", "medicine_03" => "Pílula de qualidade",
            _ => id.StartsWith("smoke_food") ? "Defumado" : id.StartsWith("dry_food") ? "Seco" : null };
        if (method != null)
        {
            var prototype = PrototypeYaml.GetItemPrototype(item.Prototype, item.Level);
            item.Name = method + " · " + (prototype?.Name?.ToString() ?? item.Name);
        }
    }

    private static void Cook(ref Item item, string id, CraftRecipeData recipe)
    {
        int tier = id.Contains("03") ? 3 : id.Contains("02") ? 2 : 1;
        int stationLevel = recipe.workbench_tags?.Values.DefaultIfEmpty(1).Max() ?? 1;
        int heatLevel = stationLevel >= 40 ? 3 : stationLevel >= 15 ? 2 : 1;
        bool isColdDrink = id == "ice_drink";
        string method = id.StartsWith("medicine_") ? "medicine_high" :
            id == "fry" ? "fried" : id == "fry_stir" ? "fried_stir" :
            id.StartsWith("smoke_food") ? "smoked" : id.StartsWith("dry_food") ||
            id.StartsWith("salt_food") || id.StartsWith("can_food")
                ? "preserve_energy" : id is "s02_skewer" or "s02_cooking" ? "s02_energy_fire" : "energy_fire";
        bool isPreserving = id.StartsWith("dry_food") || id.StartsWith("smoke_food") ||
            id.StartsWith("salt_food") || id.StartsWith("can_food");
        bool isMedicine = id.StartsWith("medicine_");

        SetTag(ref item, isColdDrink ? "cold" : method,
            isColdDrink ? 3 : isPreserving || isMedicine ? tier : heatLevel,
            "raw_food", "hot", "cold", "energy_fire", "s02_energy_fire", "fried", "fried_stir",
            "smoked", "preserve_energy", "preserve_power", "dried", "medicine_high",
            "attack_incr_food", "defense_incr_food");
        if (!isPreserving && !isMedicine && !isColdDrink)
            SetTag(ref item, "hot", heatLevel);
        if (id.StartsWith("dry_food")) SetTag(ref item, "dried", tier);
        // O atributo smoked já aplica ×1,65 pela fórmula nativa; não acumule
        // com preserve_power (×1,5). Os outros métodos de conservação usam esse bônus.
        if (id.StartsWith("dry_food") || id.StartsWith("salt_food") || id.StartsWith("can_food"))
            SetTag(ref item, "preserve_power", tier);
        if (id.EndsWith("seasoning") || id.EndsWith("food_02")) SetTag(ref item, "savory_plus", tier);
        if (id is "boil" or "steam" or "soup_01" or "herb_tea_01") SetTag(ref item, "digestive_easy", 2);
        if (isMedicine)
        {
            if (id == "medicine_03") SetTag(ref item, "sweet_plus_02", 1);
        }
        if (id == "steak_set_meal")
        {
            SetTag(ref item, "attack_incr_food", 1);
            SetTag(ref item, "defense_incr_food", 1);
        }
    }

    public static void Recalculate(ref Item item) { Load(); ApplyPerformance(ref item); }

    private static void ApplyPerformance(ref Item item)
    {
        // Recompute modified numeric fields from the prototype baseline, avoiding
        // repeated addition when processing/reforming an item more than once.
        var baseline = ItemPerformance.Of(item.Prototype, item.Level);
        var blocks = (item.Performance ?? Array.Empty<Performance>()).Select(p => new Performance {
            Id = p.Id, Strs = p.Strs == null ? new() : new(p.Strs),
            Nums = p.Nums == null ? new Dictionary<string, float>() : new(p.Nums) }).ToList();
        foreach (var block in blocks.Where(b => b.Id == "food"))
            foreach (string field in _modifiedFields.GetValueOrDefault(block.Id) ?? Array.Empty<string>())
            { block.Nums.Remove(field); block.Strs.Remove(field); }
        foreach (var original in baseline)
        {
            int index = blocks.FindIndex(p => p.Id == original.Id);
            if (index < 0) { blocks.Add(new Performance { Id = original.Id, Nums = new(), Strs = new() }); index = blocks.Count - 1; }
            foreach (var field in original.Nums ?? new())
                if (original.Id == "food") blocks[index].Nums[field.Key] = field.Value;
                else blocks[index].Nums.TryAdd(field.Key, field.Value);
            foreach (var field in original.Strs ?? new())
                if (original.Id == "food") blocks[index].Strs[field.Key] = field.Value;
                else blocks[index].Strs.TryAdd(field.Key, field.Value);
        }
        var effects = new Dictionary<(string Block, string Field), List<(string Function, double Value)>>();
        foreach (var tag in item.Tags ?? Array.Empty<Tag>())
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
        AddCraftedMaterialEffects(item, baseline, effects);
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
        // Cooking changes energy_expression, while the client displays satiety.
        // Keep that displayed amount in sync with the energy restored on use.
        bool preparedFood = item.Tags?.Any(tag => tag.Id is "fried" or "fried_stir" or
            "energy_fire" or "s02_energy_fire" or "smoked" or "preserve_energy" or
            "medicine_high" or "cold") == true;
        var food = blocks.FirstOrDefault(block => block.Id == "food");
        if (preparedFood && food.Nums?.TryGetValue("satiety", out float satiety) == true &&
            food.Nums.TryGetValue("energy_potential", out float potential))
            food.Nums["satiety"] = Math.Max(satiety,
                potential * food.Nums.GetValueOrDefault("energy_expression", 1));
        item.Performance = blocks.ToArray();
    }

    private static void AddCraftedMaterialEffects(Item item, List<Performance> baseline,
        Dictionary<(string Block, string Field), List<(string Function, double Value)>> effects)
    {
        bool Has(string id) => item.Tags?.Any(t => t.Id == id) == true;
        int Level(string id) => item.Tags?.Where(t => t.Id == id).Select(t => t.Level).DefaultIfEmpty(0).Max() ?? 0;
        bool IsEquipment(string id) => Has(id);

        // The prototype supplies the item's native performance block. Add only
        // fields that exist for that prototype, so unrelated item types are not
        // given synthetic client stats.
        void Add(string block, string[] fields, string tag, double perLevel)
        {
            int level = Level(tag);
            if (level <= 0) return;
            var native = baseline.FirstOrDefault(p => p.Id == block);
            string field = fields.FirstOrDefault(name => native.Nums?.ContainsKey(name) == true);
            if (field == null) return;
            var key = (block, field);
            if (!effects.TryGetValue(key, out var values)) effects[key] = values = new();
            values.Add((perLevel < 0 ? "decr" : "incr", Math.Abs(perLevel) * level));
        }

        void AddUsingNativeModifier(string block, string sourceTag, string modifierTag, bool reverse = false)
        {
            int level = Level(sourceTag);
            if (level <= 0 || _tags[modifierTag] is not JObject definition ||
                (string)definition["required_performance"] != block ||
                definition["modifiers"] is not JObject modifiers) return;
            foreach (var modifier in modifiers.Properties())
            {
                string formula = (string)modifier.Value["formula"];
                string function = (string)modifier.Value["function"];
                if (!StatFormula.TryEval(formula, "level", level, out double value)) continue;
                if (reverse) function = function switch { "incr" => "decr", "decr" => "incr", _ => function };
                var key = (block, modifier.Name);
                if (!effects.TryGetValue(key, out var values)) effects[key] = values = new();
                values.Add((function, value));
            }
        }

        if (IsEquipment("weapon"))
        {
            // Where the old guide confirms an effect but not its coefficient,
            // reuse the corresponding native modifier formula from tags.json.
            AddUsingNativeModifier("weapon", "hardness_hard", "attack_incr");
            AddUsingNativeModifier("weapon", "hardness_soft", "attack_decr");
            AddUsingNativeModifier("weapon", "weight_light", "accuracy_incr");
            AddUsingNativeModifier("weapon", "weight_heavy", "accuracy_decr");
            // The surviving game notes give the compact-structure coefficient
            // exactly: +0.67 percentage points of critical chance per level.
            Add("weapon", new[] { "critical", "critical_chance" }, "structure_compact", 0.0067);
            Add("weapon", new[] { "critical", "critical_chance" }, "structure_sloppy", -0.0067);
        }

        if (IsEquipment("armor"))
        {
            // Density modifies the actual durability gauge in ItemDurability;
            // dodge and defense stay in the equipment performance block.
            AddUsingNativeModifier("armor", "fiber_tough", "defense_incr");
            AddUsingNativeModifier("armor", "weight_light", "dodge_incr");
            AddUsingNativeModifier("armor", "weight_heavy", "dodge_decr");
        }

        bool IsTool = IsEquipment("tool_gathering") || IsEquipment("tool_working");
        if (IsTool)
        {
            AddUsingNativeModifier("tool", "section_sharp", "gathering_incr");
            AddUsingNativeModifier("tool", "section_round", "gathering_incr", reverse: true);
            AddUsingNativeModifier("tool", "shape_pointed", "weaponcraft_incr");
            AddUsingNativeModifier("tool", "shape_blunt", "weaponcraft_incr", reverse: true);
            AddUsingNativeModifier("tool", "inside_full", "construction_incr");
            AddUsingNativeModifier("tool", "inside_empty", "construction_incr", reverse: true);
        }
    }

    private static bool Reform(ref Item item, string id, CraftRecipeData recipe,
        Dictionary<string, Item[]> materials, int? requested, out string error)
    {
        error = null;
        var slots = item.ReformSlots?.ToArray() ?? Array.Empty<ReformSlot>();
        int index = requested ?? Array.FindIndex(slots, s => string.IsNullOrEmpty(s.RecipeId));
        if (index < 0 || index >= slots.Length || slots[index].Index != index || !string.IsNullOrEmpty(slots[index].RecipeId))
        { error = "Escolha um espaço de melhoria vazio neste equipamento."; return false; }
        if (_reforms[id]?["tags"] is not JObject effects)
        { error = "Esta melhoria não possui efeitos definidos."; return false; }
        // Pocket quality follows the item/material levels and the advertised
        // recipe cap; the old 10/20 effect intensity caps are not item levels.
        bool isPocket = id is "reform_pocket" or "reform_pocket_t2";
        int baseLevel = item.Level;
        int pocketLimit = recipe.max_level > 0 ? recipe.max_level : 60;
        if (isPocket)
        {
            var additionsMaterials = materials?.Where(p => p.Key != "base").SelectMany(p => p.Value ?? Array.Empty<Item>()).ToArray();
            if (additionsMaterials?.Length > 0)
                baseLevel = Math.Min(baseLevel, (int)Math.Round(additionsMaterials.Average(m => (double)m.Level)));
            baseLevel = Math.Clamp(baseLevel, 1, pocketLimit);
        }
        var additions = effects.Properties().Select(p => new Tag { Id = p.Name, Level = Math.Clamp(
            isPocket ? baseLevel : (int)Math.Ceiling(baseLevel * (int)p.Value["max_level"] / 60d),
            (int)p.Value["min_level"], isPocket ? pocketLimit : (int)p.Value["max_level"]) }).ToArray();
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
