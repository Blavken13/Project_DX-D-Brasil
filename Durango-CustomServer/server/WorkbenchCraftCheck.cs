using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Item;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class WorkbenchCraftCheck
{
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); Console.WriteLine("[bench-check] OK " + text); }
    private static object Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static bool Matches(Dictionary<string, string> actual, Dictionary<string, int> filter) =>
        filter == null || filter.Count == 0 || filter.Any(p => actual.ContainsKey(p.Key));
    private static bool Suitable(Item item, CraftRecipeSlotData slot) => (bool)typeof(Player)
        .GetMethod("MatchesSlot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { item, slot });

    private static void CheckAllRecipeMaterialFilters(Dictionary<string, CraftRecipeData> catalog)
    {
        int cases = 0, slots = 0;
        static Dictionary<string, int> Filter(Dictionary<string, int> tags) =>
            tags == null || (tags.Count == 1 && tags.ContainsKey("bare_hands")) ? new() : tags;
        void Assert(bool condition, string id, string slot, string scenario)
        {
            if (!condition) throw new InvalidOperationException(id + "/" + slot + ": " + scenario);
            cases++;
        }
        foreach (var (id, recipe) in catalog)
        foreach (var slot in recipe.slots ?? Array.Empty<CraftRecipeSlotData>())
        {
            slots++;
            var groups = new[] { Filter(slot.required_tags), Filter(slot.required_materials) };
            var requirements = new Dictionary<string, int>();
            foreach (var group in groups)
                foreach (var pair in group) requirements[pair.Key] = Math.Max(requirements.GetValueOrDefault(pair.Key), pair.Value);
            Item Material(int extra) => new() { Level = 60,
                Tags = requirements.Select(p => new Tag { Id = p.Key, Level = p.Value + extra }).ToArray() };
            Assert(Suitable(Material(0), slot), id, slot.slot_id, "atributos no mínimo devem ser aceitos");
            Assert(Suitable(Material(5), slot), id, slot.slot_id, "atributos acima do mínimo devem ser aceitos");
            foreach (var group in groups.Where(g => g.Count > 0))
            {
                Item missing = Material(0);
                missing.Tags = missing.Tags.Where(t => !group.ContainsKey(t.Id)).ToArray();
                Assert(!Suitable(missing, slot), id, slot.slot_id, "grupo obrigatório ausente deve ser recusado");
                Item low = Material(0);
                low.Tags = low.Tags.Select(t => group.TryGetValue(t.Id, out int required)
                    ? new Tag { Id = t.Id, Level = required - 1 } : t).ToArray();
                Assert(!Suitable(low, slot), id, slot.slot_id, "nível nominal 60 não substitui propriedade abaixo do mínimo");
                foreach (var alternative in group)
                {
                    Item oneAlternative = Material(0);
                    oneAlternative.Tags = oneAlternative.Tags.Where(t => !group.ContainsKey(t.Id) || t.Id == alternative.Key).ToArray();
                    // Se os grupos compartilham tags, mantém os requisitos do outro
                    // grupo sem introduzir outra alternativa deste grupo.
                    bool otherSatisfied = groups.All(g => g.Count == 0 || g.Any(p =>
                        oneAlternative.Tags.Any(t => t.Id == p.Key && t.Level >= p.Value)));
                    if (otherSatisfied) Assert(Suitable(oneAlternative, slot), id, slot.slot_id, "alternativa OR válida deve ser aceita");
                }
            }
        }
        Check(slots == catalog.Values.Sum(r => r.slots?.Length ?? 0), "auditoria cobre todos os slots das receitas");
        Console.WriteLine($"[bench-check] Filtros: {catalog.Count} receitas, {slots} slots, {cases} verificações aprovadas.");
    }

    private static void CheckPurityMaterialLevels(Dictionary<string, CraftRecipeData> catalog)
    {
        var bowSlot = catalog["bowstick_metal_01"].slots.Single(s => s.slot_id == "base");
        Check(bowSlot.required_materials["purity_high"] == 55, "regressão usa a receita real do limbo de arco nível 55");
        foreach (int level in new[] { 54, 55, 60 })
        {
            Item metal = Cheats.MakeItem("ore_iron", level).Value;
            string id = metal.Id;
            Check(ItemCraftModifications.Apply(ref metal, "smelt", catalog["smelt"], new(), null, out _), "funde metal nível " + level);
            Check(ItemCraftModifications.Apply(ref metal, "refine", catalog["refine"], new(), null, out _), "refina metal nível " + level);
            Check(metal.Tags.Single(t => t.Id == "purity_high").Level == level &&
                metal.TagModifications.Single(t => t.Id == "purity_high").Level == level,
                "pureza acompanha nível do metal em atributos e modificações: " + level);
            Check(Suitable(metal, bowSlot) == (level >= 55), "receita distingue material abaixo, igual e acima do mínimo: " + level);
            Check(metal.Id == id && metal.Level == level, "refinar preserva identidade e nível: " + level);
            int hardnessBefore = metal.Tags.Where(t => t.Id == "hardness_hard").Select(t => t.Level).DefaultIfEmpty(0).Max();
            Check(ItemCraftModifications.Apply(ref metal, "refine_02", catalog["refine_02"], new(), null, out _), "tratamento de dureza continua disponível");
            int hardness = metal.Tags.Single(t => t.Id == "hardness_hard").Level;
            Check(hardness == hardnessBefore + 1, "propriedade minor conserva incremento de intensidade");
            metal.ModifiedCount = 2;
            metal.ModifiableCount = 1;
            // Reproduz um save afetado: nível nominal correto, pureza gravada como 1.
            metal.Tags = metal.Tags.Select(t => t.Id == "purity_high" ? new Tag { Id = t.Id, Level = 1 } : t).ToArray();
            metal.TagModifications = metal.TagModifications.Select(t => t.Id == "purity_high" ? new Tag { Id = t.Id, Level = 1 } : t).ToArray();
            var inventory = new List<Item> { Json.Read<Item>(Json.Write(metal)) };
            ItemExtRepair.Normalize(inventory, "Teste de pureza");
            var repaired = inventory.Single();
            Check(repaired.Tags.Single(t => t.Id == "purity_high").Level == level && Suitable(repaired, bowSlot) == (level >= 55),
                "carregamento de save repara material antigo sem aceitar nível insuficiente: " + level);
            Check(repaired.Id == id && repaired.ModifiedCount == 2 && repaired.ModifiableCount == 1 &&
                repaired.Tags.Single(t => t.Id == "hardness_hard").Level == hardness,
                "reparo preserva usos, identidade e propriedades minor");
            string before = Json.Write(repaired);
            Check(!ItemCraftModifications.Initialize(ref repaired) && Json.Write(repaired) == before, "reparo é idempotente");
        }
        var ordinary = Cheats.MakeItem("ore_iron", 60).Value;
        Check(!Suitable(ordinary, bowSlot) && !ordinary.Tags.Any(t => t.Id == "purity_high"), "minério sem refinamento permanece inelegível");
        var untreated = Cheats.MakeItem("metal_iron", 60).Value;
        ItemCraftModifications.Initialize(ref untreated);
        Check(!Suitable(untreated, bowSlot), "metal não refinado não ganha pureza por normalização");
    }
    private static Item Material(CraftRecipeSlotData slot)
    {
        var prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.FirstOrDefault(k =>
            PrototypeYaml.GetItemPrototype(k) is { Tags: { } tags } &&
            Matches(tags, slot.required_tags) && Matches(tags, slot.required_materials));
        // Some processed materials require dynamic tags absent from native prototypes.
        Item item = Cheats.MakeItem(prototype ?? "wood_log", 60).Value;
        var tags = (item.Tags ?? Array.Empty<Tag>()).ToList();
        foreach (var filter in new[] { slot.required_tags, slot.required_materials })
            if (filter?.Count > 0 && !filter.Any(p => tags.Any(t => t.Id == p.Key)))
                tags.Add(new Tag { Id = filter.Keys.First(), Level = 60 });
        item.Tags = tags.ToArray(); ItemCraftModifications.Initialize(ref item);
        return item;
    }

    internal static void Run(EconomyProtocolCheck.Link link, PlayerContext context, World world, Point2 tile)
    {
        var catalog = Json.ReadFromFile<Dictionary<string, CraftRecipeData>>("item/recipes");
        CheckPurityMaterialLevels(catalog);
        CheckAllRecipeMaterialFilters(catalog);
        Check(catalog.Keys.All(CraftRecipeStore.CraftableIds().Contains), "catalogo inclui criacao, processamento e melhorias");
        foreach (var (id, recipe) in catalog.Where(p => p.Value.workbench_tags?.Count > 0))
            Check(BlueprintStore.GetAllBlueprints().Any(b => b.Components?.Contains("Workbench") == true &&
                WorkbenchTags.Of(b.EntityType)?.Any(t => recipe.workbench_tags.TryGetValue(t.Id, out int level) && t.Level >= level) == true),
                id + " tem uma bancada utilizavel com nivel suficiente");
        foreach (var (id, recipe) in catalog.Where(p => p.Value.type != CraftType.Craft))
        {
            var slots = recipe.slots.ToDictionary(s => s.slot_id,
                s => Enumerable.Range(0, Math.Max(1, s.count_min)).Select(_ => Material(s)).ToArray());
            Item original = slots["base"][0], result = original;
            string before = Json.Write(original);
            Check(ItemCraftModifications.Apply(ref result, id, recipe, slots, recipe.type == CraftType.Reform ? 0 : null, out string error),
                id + " possui resultado executavel: " + error);
            Check(Json.Write(original) == before && result.Id == original.Id && result.Level == original.Level,
                id + " nao altera original durante estimativa e preserva identidade/nivel");
            var tags = Json.ReadFromFile<Newtonsoft.Json.Linq.JObject>("tags");
            Check((result.TagModifications ?? Array.Empty<Tag>()).All(t => tags[t.Id] != null), id + " usa atributos existentes no cliente");
            var major = (result.TagModifications ?? Array.Empty<Tag>())
                .Where(t => (string)tags[t.Id]?["type"] == "major").ToArray();
            if (major.Length > 0)
            {
                Check(major.All(t => t.Level == Math.Clamp(result.Level, 1, (int?)tags[t.Id]?["max_level"] ?? 100)),
                    id + " gera propriedades major no nível do material");
                var legacy = Json.Read<Item>(Json.Write(result));
                var ids = major.Select(t => t.Id).ToHashSet();
                legacy.ModifiedCount = Math.Max(1, legacy.ModifiedCount);
                legacy.TagModifications = legacy.TagModifications.Select(t => ids.Contains(t.Id) ? new Tag { Id = t.Id, Level = 1 } : t).ToArray();
                legacy.Tags = legacy.Tags.Select(t => ids.Contains(t.Id) ? new Tag { Id = t.Id, Level = 1 } : t).ToArray();
                ItemCraftModifications.Initialize(ref legacy);
                Check(major.All(t => legacy.Tags.Any(a => a.Id == t.Id && a.Level == t.Level) &&
                    legacy.TagModifications.Any(a => a.Id == t.Id && a.Level == t.Level)),
                    id + " repara propriedades processadas em saves antigos");
                Check(result.TagModifications.Where(t => !ids.Contains(t.Id)).All(t =>
                    legacy.TagModifications.Any(a => a.Id == t.Id && a.Level == t.Level)),
                    id + " reparo não altera atributos de intensidade");
            }
        }
        var skillSave = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
        foreach (var unlock in CraftingAbilityRecipes.All)
        {
            Check(CraftingAbilityRecipes.Unlocked(ability => ability == unlock.Ability ? unlock.RequiredValue : 0).Contains(unlock.RecipeId),
                unlock.RecipeId + " libera no valor original da habilidade");
            // Some recipes occur in more than one threshold; test against the lowest for this ability.
            float first = CraftingAbilityRecipes.All.Where(u => u.RecipeId == unlock.RecipeId && u.Ability == unlock.Ability).Min(u => u.RequiredValue);
            Check(!CraftingAbilityRecipes.Unlocked(ability => ability == unlock.Ability ? first - 0.01f : 0).Contains(unlock.RecipeId),
                unlock.RecipeId + " fica bloqueada antes do requisito");
        }
        var savedSkills = skillSave.Learned.ToDictionary(p => p.Key, p => new Dictionary<string, int>(p.Value));
        var categoryLevels = skillSave.Categories.ToDictionary(p => p.Key, p => p.Value.Level);
        var initial = link.Request<GetRecipes, Recipes>(default);
        Check(initial.Ids.All(link.Player.UnlockedRecipeIds().Contains), "receitas iniciais seguem habilidades aprendidas");
        foreach (var category in SkillDataStore.Skills.Values)
            foreach (var skill in category)
                skillSave.Learned[skill.Key] = skill.Value.ToDictionary(sub => sub.Key, sub => sub.Value.Length);
        foreach (var state in skillSave.Categories.Values) state.Level = 60;
        var unlocked = link.Player.UnlockedRecipeIds();
        var available = link.Request<GetRecipes, Recipes>(default);
        Check(available.Ids.ToHashSet().SetEquals(unlocked.Where(catalog.ContainsKey)),
            "todas as receitas das skills aprendidas sao publicadas, inclusive as de bancada");
        Check(available.Ids.Except(initial.Ids).Any(id => catalog[id].type != CraftType.Craft),
            "processamento e melhorias entram somente apos aprender suas skills");
        var bonusUnlock = CraftingAbilityRecipes.All.First(u => !available.Ids.Contains(u.RecipeId));
        string bonusKey = SkillDataStore.DerivedOfModifier.First(p => p.Value == bonusUnlock.Ability).Key;
        Item gear = Cheats.MakeItem("gloves_weaponcrafting", 60).Value;
        float beforeBonus = (float)Call(link.Player, "CraftAbilityValue", bonusUnlock.Ability);
        gear.Performance = new[] { new Performance { Id = "modifiers", Nums = new() {
            [bonusKey] = bonusUnlock.RequiredValue - beforeBonus + 1 } } };
        context.InventoryItems.Add(gear);
        int recipeUpdates = link.Messages.OfType<Recipes>().Count();
        link.Request<Equip, Equipments>(new Equip { Action = "equip", ItemId = gear.Id, SlotName = "gloves" });
        link.PumpUntil(() => link.Messages.OfType<Recipes>().Count() > recipeUpdates);
        Check(link.Messages.OfType<Recipes>().Last().Ids.Contains(bonusUnlock.RecipeId) &&
            link.Messages.OfType<Statistics>().Last().DerivedsAbilities[bonusUnlock.Ability] >= bonusUnlock.RequiredValue,
            "equipar bonus atualiza capacidade mostrada e libera receita avancada sem relogar");
        recipeUpdates = link.Messages.OfType<Recipes>().Count();
        link.Request<Equip, Equipments>(new Equip { Action = "unequip", SlotName = "gloves" });
        link.PumpUntil(() => link.Messages.OfType<Recipes>().Count() > recipeUpdates);
        Check(!link.Messages.OfType<Recipes>().Last().Ids.Contains(bonusUnlock.RecipeId),
            "retirar bonus reavalia requisitos de receita avancada");
        skillSave.Learned = savedSkills;
        foreach (var level in categoryLevels) skillSave.Categories[level.Key].Level = level.Value;

        var cases = new[] { "trim", "process", "extend_sheet", "dry", "tan", "smelt", "refine", "dye_color_r", "reform_pocket", "roast_01", "fry" }
            .Select(id => (Id: id, Material: (string)null))
            .Concat(new[] { "board", "s02_board", "board_02", "board_03" }
                .SelectMany(id => (id == "board" ? new[] { "wood", "stone", "bone", "metal" } :
                    id == "board_02" ? new[] { "wood", "stone" } : new[] { "stone" })
                    .Select(material => (Id: id, Material: material))));
        foreach (var testCase in cases)
        {
            string id = testCase.Id;
            var recipe = catalog[id];
            var inputs = recipe.slots.ToDictionary(s => s.slot_id,
                s => Enumerable.Range(0, s.count_min).Select(_ => Material(s)).ToArray());
            if (testCase.Material != null)
            {
                Item material = inputs["base"][0];
                if (id == "board")
                {
                    var slot = recipe.slots.Single(s => s.slot_id == "base");
                    string nativeSource = SingletonDict<string, List<Prototype>>.Instance.Keys.FirstOrDefault(k =>
                        PrototypeYaml.GetItemPrototype(k) is { Tags: { } tags } && tags.ContainsKey(testCase.Material) &&
                        Matches(tags, slot.required_tags) && Matches(tags, slot.required_materials));
                    if (nativeSource != null) material = Cheats.MakeItem(nativeSource, 60).Value;
                }
                material.Tags = material.Tags.Where(t => !new[] { "wood", "stone", "bone", "metal" }.Contains(t.Id))
                    .Append(new Tag { Id = testCase.Material, Level = material.Level }).ToArray();
                if (id == "board_02")
                    material.Tags = material.Tags.Where(t => t.Id is not "trim_wood" and not "trim_stone")
                        .Append(new Tag { Id = "trim_" + testCase.Material, Level = material.Level }).ToArray();
                inputs["base"] = new[] { material };
            }
            if (recipe.category == "cook") inputs["base"] = new[] { Cheats.MakeItem("meat", 1).Value };
            var baseItem = inputs["base"][0];
            string originalJson = Json.Write(baseItem);
            foreach (Item input in inputs.Values.SelectMany(v => v)) context.InventoryItems.Add(input);
            var toolPrototype = SingletonDict<string, List<Prototype>>.Instance.Keys.FirstOrDefault(k =>
                PrototypeYaml.GetItemPrototype(k).Tags?.Keys.Any(t => recipe.tool_tags.ContainsKey(t)) == true);
            Item? tool = toolPrototype == null ? null : Cheats.MakeItem(toolPrototype, 60);
            if (tool.HasValue) context.InventoryItems.Add(tool.Value);
            var blueprint = BlueprintStore.GetAllBlueprints().First(b => b.Components?.Contains("Workbench") == true &&
                (recipe.workbench_tags == null || recipe.workbench_tags.Count == 0 || WorkbenchTags.Of(b.EntityType)?.Any(t =>
                    recipe.workbench_tags.TryGetValue(t.Id, out int level) && t.Level >= level) == true));
            var bench = Cheats.MakeAppearArtifact(new[] { "prop", blueprint.EntityType.ToString() }, out _).Value;
            bench.Tile = tile; bench.States.BuildingState = Shared.Building.BuildingState.Completed;
            world.ConstructArtifact(bench, null, context.EntityId);
            var materialIds = inputs.ToDictionary(p => p.Key, p => p.Value.Select(i => i.Id).ToArray());
            var estimate = link.Request<EstimateCraft, CraftEstimationInfo>(new EstimateCraft {
                RecipeId = id, Materials = materialIds, ReformSlotIndex = recipe.type == CraftType.Reform ? 0 : null });
            Check(context.InventoryItems.Single(i => i.Id == baseItem.Id).ModifiableCount == baseItem.ModifiableCount,
                id + " estimativa nao consome processamento");
            Check(Json.Write(context.InventoryItems.Single(i => i.Id == baseItem.Id)) == originalJson,
                id + " estimativa preserva material original completo");
            if (testCase.Material != null)
            {
                string expectedPrototype = "board_" + testCase.Material;
                Check(estimate.CraftEstimation.Value.PrototypeId == expectedPrototype &&
                    estimate.CraftEstimation.Value.Name == (string)PrototypeYaml.GetItemPrototype(expectedPrototype).Name,
                    id + " previa mostra tabua de " + testCase.Material + " em vez do pilar");
            }
            var timer = link.Request<Craft, Messages.Timer>(new Craft { RecipeId = id, Materials = materialIds,
                ToolItemId = tool?.Id, Workbench = new PropKey { EntityId = bench.EntityId, Tile = tile },
                ReformSlotIndex = recipe.type == CraftType.Reform ? 0 : null });
            Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + timer.Duration + 1);
            link.PumpUntil(() => context.InventoryItems.Any(i => i.Id == baseItem.Id));
            Item result = context.InventoryItems.Single(i => i.Id == baseItem.Id);
            if (id == "refine")
                Check(result.Tags.Single(t => t.Id == "purity_high").Level == result.Level &&
                    Suitable(result, catalog["bowstick_metal_01"].slots.Single(s => s.slot_id == "base")),
                    "refinamento pelo TCP gera metal elegível para limbo de arco nível 55");
            if (testCase.Material != null)
            {
                var prototype = PrototypeYaml.GetItemPrototype("board_" + testCase.Material);
                Check(result.Prototype == "board_" + testCase.Material && result.Name == (string)prototype.Name &&
                    result.Icon == prototype.Icon && result.Description == (string)prototype.Description && result.Size == prototype.Size &&
                    !result.Tags.Any(t => t.Id is "pillar_thin" or "pillar_normal" or "pillar_thick"),
                    id + " entrega tabua de " + testCase.Material + " com identidade visual e forma corretas");
                var persisted = Json.Read<Item>(Json.Write(result));
                Check(persisted.Prototype == result.Prototype && persisted.Name == result.Name && persisted.Level == baseItem.Level,
                    id + " conserva tabua e nivel apos salvar");
            }
            Check(result.Tags.All(t => estimate.CraftEstimation.Value.Tags.TryGetValue(t.Id, out int level) && t.Level == level)
                && result.ModifiableCount == estimate.CraftEstimation.Value.ModifiableCount,
                id + " craft TCP conclui como previsto pela estimativa");
            Check(inputs.Where(p => p.Key != "base").SelectMany(p => p.Value).All(i => context.InventoryItems.All(p => p.Id != i.Id)),
                id + " consome somente os materiais adicionais e conserva base");
            if (recipe.type == CraftType.Reform)
            {
                Check(result.ReformSlots[0].RecipeId == id && result.ReformSlots[1].RecipeId == null,
                    "melhoria preenche apenas o espaco escolhido");
                Check(result.Performance.Single(p => p.Id == "armor").Nums["bag_size"] >
                    (baseItem.Performance.FirstOrDefault(p => p.Id == "armor").Nums?.GetValueOrDefault("bag_size") ?? 0),
                    "bolso de melhoria aumenta capacidade numerica do equipamento");
                string before = Json.Write(result);
                Check(!ItemCraftModifications.Apply(ref result, id, recipe, inputs, 0, out _) && Json.Write(result) == before,
                    "espaco ocupado rejeitado sem modificar equipamento");
                var persisted = Json.Read<Item>(Json.Write(result));
                Check(persisted.ReformSlots[0].RecipeId == id && persisted.Tags.Any(t => t.Id == "reform_pocket"),
                    "melhoria e seus atributos sobrevivem ao save");
            }
            else if (recipe.deduct_modifiable_count)
                Check(result.ModifiableCount == baseItem.ModifiableCount - 1, id + " desconta um uso de processamento");
            if (recipe.category == "cook")
            {
                float rawEnergy = baseItem.Performance.Single(p => p.Id == "food").Nums["energy_potential"];
                float cookedEnergy = result.Performance.Single(p => p.Id == "food").Nums["energy_potential"];
                Check(cookedEnergy > rawEnergy && result.Tags.All(t => t.Id != "raw_food"),
                    id + " melhora nutricao e remove atributo cru");
                var persisted = Json.Read<Item>(Json.Write(result));
                Check(persisted.Performance.Single(p => p.Id == "food").Nums["energy_potential"] == cookedEnergy,
                    id + " conserva nutricao preparada apos salvar");
                Item repeated = result;
                ItemCraftModifications.Apply(ref repeated, id, recipe, inputs, null, out _);
                Check(repeated.Performance.Single(p => p.Id == "food").Nums["energy_potential"] == cookedEnergy,
                    id + " nao acumula bonus ao recalcular o mesmo preparo");
                var survival = (SurvivalState)typeof(Player).GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
                // Leave enough headroom: a capped gauge could conceal consumption
                // incorrectly recovering the raw prototype instead of cooked food.
                survival.SetMaxBonus(SurvivalState.KeyEnergy, 1000, Gauge.CurrentTime);
                survival.Set(SurvivalState.KeyEnergy, 1);
                Call(link.Player, "FlushSurvival");
                link.Request<UseItem, OK>(new UseItem { ItemId = result.Id, Accept = true });
                float expected = 1 + cookedEnergy;
                float actual = survival.ValueAt(SurvivalState.KeyEnergy, Gauge.CurrentTime);
                Check(Math.Abs(actual - expected) < .5,
                    id + $" consumo TCP recupera os valores preparados do item ({actual} / {expected})");
            }
            world.DestructArtifact(bench.EntityId);
        }
        var exhausted = Cheats.MakeItem("wood_log", 60).Value;
        exhausted.ModifiableCount = 0; exhausted.ModifiedCount = 3;
        ItemCraftModifications.Initialize(ref exhausted);
        Check(exhausted.ModifiableCount == 0, "normalizacao nao restaura usos de material ja processado");
    }
}
