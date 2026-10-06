using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Item;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class CraftEquipmentLevelCheck
{
    private static object Call(Player player, string method, params object[] args) => typeof(Player)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, args);

    private static bool Matches(Dictionary<string, string> tags, Dictionary<string, int> filter) =>
        filter == null || filter.Count == 0 || filter.ContainsKey("bare_hands") || filter.Keys.Any(tags.ContainsKey);

    internal static void Run(string root, World world, Action<bool, string> check)
    {
        var context = new PlayerContext { RegionId = world.TerrainId, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
            PlayerEntityId = "equipment-level-tester", PlayerName = "Craft tester", PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, "equipment-level.player"));
        context.AppearPlayer.IsAlive = true;
        context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(world.EntryPoint.x * 200, world.EntryPoint.y * 200), Time = Gauge.CurrentTime } } } };
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        var player = link.Player;
        var skills = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        skills.Learned.Clear();
        foreach (Category category in Enum.GetValues<Category>())
            if (category != Category.Invalid) ((SkillCategorySave)Call(player, "CategoryState", (int)category)).Level = 60;
        var recipes = CraftRecipeStore.All().Where(r => r.type == CraftType.Craft &&
            (r.category.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) || r.category.StartsWith("tool", StringComparison.OrdinalIgnoreCase))).ToArray();
        check(recipes.All(r => r.max_level == 60 && (int)Call(player, "ProductLevel", r,
            new List<Item> { new() { Level = 60 } }) == 60), "todas as receitas de armas/ferramentas permitem resultado 60 com habilidade e materiais 60");
        check(recipes.SelectMany(r => new[] { r.prototype_id }.Concat(r.prototypes?.Select(p => p.prototype_id) ?? Array.Empty<string>()))
            .Where(id => !string.IsNullOrEmpty(id)).All(id => Enumerable.Range(1, 60).All(level => PrototypeYaml.GetItemPrototype(id, level) != null)),
            "protótipos recebidos por PC/Android cobrem todos os níveis calculados");

        var bench = Cheats.MakeAppearArtifact(new[] { "prop", BlueprintStore.GetBlueprint("fur_table_03").EntityType.ToString() }, out _).Value;
        bench.Tile = world.EntryPoint; bench.FounderEntityId = context.EntityId;
        world.ConstructArtifact(bench, null, context.EntityId);
        void Craft(string id, int proficiency, int[] materialLevels, int expected, bool great = false)
        {
            context.InventoryItems.Clear(); context.EquippedItems.Clear();
            var state = (SkillCategorySave)Call(player, "CategoryState", (int)Category.Weaponcrafting);
            state.Level = proficiency; state.Exp = 0; state.ResearchStart = state.ResearchEnd = 0;
            skills.Learned.Clear();
            player.CraftRoll = () => great ? 0 : 1;
            var recipe = CraftRecipeStore.Get(id);
            var sent = new Dictionary<string, string[]>();
            var picked = new List<Item>(); int cursor = 0;
            foreach (var slot in recipe.slots)
            {
                string prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k =>
                    PrototypeYaml.GetItemPrototype(k)?.Tags is { } tags && Matches(tags, slot.required_tags) && Matches(tags, slot.required_materials));
                var items = Enumerable.Range(0, slot.count_min).Select(_ => Cheats.MakeItem(prototype,
                    materialLevels[cursor++ % materialLevels.Length]).Value).ToArray();
                context.InventoryItems.AddRange(items); picked.AddRange(items); sent[slot.slot_id] = items.Select(i => i.Id).ToArray();
            }
            Item? tool = null;
            if (recipe.tool_tags?.Count > 0 && !recipe.tool_tags.ContainsKey("bare_hands"))
            {
                string prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k =>
                    PrototypeYaml.GetItemPrototype(k)?.Tags is { } tags && recipe.tool_tags.Keys.Any(tags.ContainsKey));
                tool = Cheats.MakeItem(prototype, 60); context.InventoryItems.Add(tool.Value);
            }
            int ordinary = Math.Min(proficiency, (int)Math.Round(picked.Average(i => (double)i.Level)));
            var estimate = link.Request<EstimateCraft, CraftEstimationInfo>(new EstimateCraft { RecipeId = id, Materials = sent });
            check(estimate.CraftEstimation?.Level == ordinary && estimate.CraftLevel == ordinary,
                id + $": prévia usa habilidade {proficiency} e média dos materiais {picked.Average(i => i.Level)}");
            int replies = link.Messages.OfType<Crafted>().Count();
            var timer = link.Request<Craft, Messages.Timer>(new Craft { RecipeId = id, Materials = sent, ToolItemId = tool?.Id,
                Workbench = recipe.workbench_tags?.Count > 0 ? new PropKey { EntityId = bench.EntityId, Tile = bench.Tile } : null });
            Call(player, "UpdatePendingCrafts", Gauge.CurrentTime + timer.Duration + 1);
            link.PumpUntil(() => link.Messages.OfType<Crafted>().Count() > replies);
            var result = link.Messages.OfType<Crafted>().Last();
            check(result.Items.Length > 0 && result.Items.All(i => i.Level == expected && i.Tags.All(t => t.Level == expected)) &&
                result.Result == (great ? Result.GreatSuccess : Result.Success), id + $": produto TCP nível {expected}, sem piso/teto antigo");
            check(picked.All(i => context.InventoryItems.All(p => p.Id != i.Id)) && result.Items.All(i => context.InventoryItems.Any(p => p.Id == i.Id)),
                id + ": consome materiais e entrega o produto anunciado");
            check(result.Items.All(i => i.Durability.Max() > 1 && i.Performance.Any(p => p.Id == "weapon" && p.Nums?.Count > 0)),
                id + ": durabilidade e atributos reais calculados no nível do produto");
            context.Save(); check(SafeSave.FlushPending(), "produto salvo após fabricação");
            var loaded = PlayerContext.Load(context.Path);
            check(result.Items.All(i => loaded.InventoryItems.Single(p => p.Id == i.Id).Level == expected), "nível fabricado preservado ao reconectar");
        }
        Craft("harpoon_wooden_01", 10, new[] { 60 }, 10);
        Craft("harpoon_wooden_01", 40, new[] { 60 }, 40);
        Craft("harpoon_wooden_01", 60, new[] { 20 }, 20);
        Craft("harpoon_wooden_01", 60, new[] { 60 }, 60);
        Craft("assembled_sword_one_01", 40, new[] { 60 }, 40);
        Craft("assembled_sword_one_01", 50, new[] { 20, 40, 60 }, 40);
        Craft("assembled_sword_one_01", 50, new[] { 20, 40, 60 }, 50, great: true);
        Craft("harpoon_metal_01", 40, new[] { 60 }, 40);
        Craft("harpoon_metal_01", 60, new[] { 60 }, 60);
    }
}
