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
    private static Item Material(CraftRecipeSlotData slot)
    {
        var prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.FirstOrDefault(k =>
            PrototypeYaml.GetItemPrototype(k) is { Tags: { } tags } &&
            Matches(tags, slot.required_tags) && Matches(tags, slot.required_materials));
        // Processed materials have dynamic tags and do not have a separate prototype.
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

        foreach (string id in new[] { "trim", "process", "board", "extend_sheet", "dry", "tan", "smelt", "refine", "dye_color_r", "reform_pocket", "roast_01", "fry" })
        {
            var recipe = catalog[id];
            var inputs = recipe.slots.ToDictionary(s => s.slot_id,
                s => Enumerable.Range(0, s.count_min).Select(_ => Material(s)).ToArray());
            if (recipe.category == "cook") inputs["base"] = new[] { Cheats.MakeItem("meat", 1).Value };
            var baseItem = inputs["base"][0];
            foreach (Item input in inputs.Values.SelectMany(v => v)) context.InventoryItems.Add(input);
            var toolPrototype = SingletonDict<string, List<Prototype>>.Instance.Keys.FirstOrDefault(k =>
                PrototypeYaml.GetItemPrototype(k).Tags?.Keys.Any(t => recipe.tool_tags.ContainsKey(t)) == true);
            Item? tool = toolPrototype == null ? null : Cheats.MakeItem(toolPrototype, 60);
            if (tool.HasValue) context.InventoryItems.Add(tool.Value);
            var blueprint = BlueprintStore.GetAllBlueprints().First(b => b.Components?.Contains("Workbench") == true && WorkbenchTags.Of(b.EntityType)?.Any(t =>
                recipe.workbench_tags.TryGetValue(t.Id, out int level) && t.Level >= level) == true);
            var bench = Cheats.MakeAppearArtifact(new[] { "prop", blueprint.EntityType.ToString() }, out _).Value;
            bench.Tile = tile; bench.States.BuildingState = Shared.Building.BuildingState.Completed;
            world.ConstructArtifact(bench, null, context.EntityId);
            var materialIds = inputs.ToDictionary(p => p.Key, p => p.Value.Select(i => i.Id).ToArray());
            var estimate = link.Request<EstimateCraft, CraftEstimationInfo>(new EstimateCraft {
                RecipeId = id, Materials = materialIds, ReformSlotIndex = recipe.type == CraftType.Reform ? 0 : null });
            Check(context.InventoryItems.Single(i => i.Id == baseItem.Id).ModifiableCount == baseItem.ModifiableCount,
                id + " estimativa nao consome processamento");
            var timer = link.Request<Craft, Messages.Timer>(new Craft { RecipeId = id, Materials = materialIds,
                ToolItemId = tool?.Id, Workbench = new PropKey { EntityId = bench.EntityId, Tile = tile },
                ReformSlotIndex = recipe.type == CraftType.Reform ? 0 : null });
            Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + timer.Duration + 1);
            link.PumpUntil(() => context.InventoryItems.Any(i => i.Id == baseItem.Id));
            Item result = context.InventoryItems.Single(i => i.Id == baseItem.Id);
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
