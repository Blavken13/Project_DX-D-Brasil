using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GameplayBugsCheck
{
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); Console.WriteLine("[bugs-check] OK " + name); }
    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-bugs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            string terrain = RegionCatalog.All.First(r => RegionCatalog.GetTemplate(r.TemplateId) is { Role: Role.Risky, Level: >= 40 }).Id;
            var state = new WorldContext { TerrainId = terrain };
            state.Initialize(Path.Combine(root, "test.world"));
            var world = new World(state);
            var context = new PlayerContext { RegionId = terrain, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "bugs-tester", PlayerName = "Tester", PlayerLevel = 60 } };
            context.Initialize(Path.Combine(root, "test.player"));
            context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
            var tile = new Point2(world.EntryPoint.x + 8, world.EntryPoint.y + 8);
            context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
                Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
            using var link = new EconomyProtocolCheck.Link(context, world, null, true);
            context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
                Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };

            GatheredPropertiesCheck.Run(Check);
            WorkbenchCraftCheck.Run(link, context, world, tile);
            foreach (string recipeId in new[] { "extend_rope", "extend_stick", "s02_extend_stick" })
            {
                var recipe = CraftRecipeStore.Get(recipeId);
                Check(CraftRecipeStore.CraftableIds().Contains(recipeId), recipeId + " aparece no catalogo");
                var materials = new Dictionary<string, string[]>();
                var consumed = new List<Item>();
                foreach (var slot in recipe.slots)
                {
                    var prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k =>
                        PrototypeYaml.GetItemPrototype(k).Tags?.Keys.Any(t => slot.required_tags.ContainsKey(t)) == true);
                    var item = Cheats.MakeItem(prototype, 40).Value;
                    item.ModifiableCount = 3;
                    context.InventoryItems.Add(item); consumed.Add(item);
                    materials[slot.slot_id] = new[] { item.Id };
                }
                var timer = link.Request<Craft, Messages.Timer>(new Craft { RecipeId = recipeId, Materials = materials });
                Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + timer.Duration + 1);
                link.PumpUntil(() => context.InventoryItems.Any(i => i.Id == consumed[0].Id));
                var product = context.InventoryItems.Single(i => i.Id == consumed[0].Id);
                Check(product.Level == consumed[0].Level && product.Tags.Any(t => t.Id == CraftRecipeStore.ExtendedShape(recipeId))
                    && !product.Tags.Any(t => t.Id == (recipeId == "extend_rope" ? "string_short" : "stick_short") || t.Id == (recipeId == "extend_rope" ? "string_normal" : "stick_normal"))
                    && product.ModifiableCount == 2 && product.ModifiedCount == consumed[0].ModifiedCount + 1,
                    recipeId + " transforma base preservando nivel e desconta modificacao");
                Check(consumed.Skip(1).All(i => context.InventoryItems.All(p => p.Id != i.Id)), recipeId + " consome materiais adicionais");
            }

            var dropped = context.InventoryItems.First();
            link.Send(new DumpItems { ItemIds = new[] { dropped.Id }, Tile = tile });
            link.PumpUntil(() => state.GroundPackages.Count == 1);
            string packageId = state.GroundPackages.Keys.Single();
            Check(context.InventoryItems.All(i => i.Id != dropped.Id), "descarte transfere item para o mundo");
            var touched = link.Request<Touch, Touched>(new Touch { EntityId = packageId, EntityType = 8000, Tile = tile });
            Check(touched.Collectible.Generators.Single().Id == dropped.Id, "pacote oferece o item original para coleta");
            world.Save(); SafeSave.FlushPending();
            var reload = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(state.Path));
            reload.Initialize(state.Path);
            Check(reload.GroundPackages[packageId].Single().Id == dropped.Id && reload.Artifacts.ContainsKey(packageId), "item e pacote persistem apos salvar e recarregar");
            int replies = link.Messages.OfType<Collected>().Count();
            link.Send(new Collect { EntityId = packageId, GeneratorId = dropped.Id, Tile = tile });
            link.PumpUntil(() => link.Messages.OfType<Collected>().Count() > replies);
            Check(context.InventoryItems.Count(i => i.Id == dropped.Id) == 1 && state.GroundPackages.Count == 0,
                "coleta recupera item original e remove pacote vazio");
            int aborts = link.Messages.OfType<Abort>().Count();
            link.Send(new Collect { EntityId = packageId, GeneratorId = dropped.Id, Tile = tile });
            link.PumpUntil(() => link.Messages.OfType<Abort>().Count() > aborts);
            Check(context.InventoryItems.Count(i => i.Id == dropped.Id) == 1, "coleta repetida nao duplica item");
            aborts = link.Messages.OfType<Abort>().Count();
            link.Send(new DumpItems { ItemIds = new[] { dropped.Id }, Tile = new Point2(world.NumTilesX + 1, 0) });
            link.PumpUntil(() => link.Messages.OfType<Abort>().Count() > aborts);
            Check(context.InventoryItems.Any(i => i.Id == dropped.Id), "posicao invalida nao perde item");
            link.Send(new DumpItems { ItemIds = new[] { dropped.Id } });
            link.PumpUntil(() => context.InventoryItems.All(i => i.Id != dropped.Id));
            Check(state.GroundPackages.Count == 0, "exclusao explicita continua removendo item");

            ushort tableType = (ushort)BlueprintStore.GetAllBlueprints().First(b => b.Id == "fur_table").EntityType;
            foreach (ushort type in new ushort[] { 7000, tableType })
            {
                var artifact = Cheats.MakeAppearArtifact(new[] { "prop", type.ToString() }, out _).Value;
                artifact.Tile = tile;
                Check((bool)Call(link.Player, "CanUseArtifactInCurrentSettlement", artifact, "another-player", Shared.Estate.AccessRights.UseFacility),
                    type + " permite usar instalacao compartilhada na ilha selvagem");
                Check(!(bool)Call(link.Player, "CanUseArtifactInCurrentSettlement", artifact, "another-player", Shared.Estate.AccessRights.Take),
                    type + " preserva permissao de armazenamento");
                artifact.States.BuildingState = Shared.Building.BuildingState.Completed;
                world.ConstructArtifact(artifact, null, "another-player");
                var menu = link.Request<Touch, Touched>(new Touch { EntityId = artifact.EntityId, EntityType = type, Tile = tile });
                Check(menu.Interactions.Contains((int)Shared.System.Interaction.Craft), type + " oferece craft pelo TCP");
                var benchRecipe = new CraftRecipeData { workbench_tags = WorkbenchTags.Of(type).ToDictionary(t => t.Id, t => t.Level) };
                var benchArgs = new object[] { benchRecipe, new PropKey { EntityId = artifact.EntityId, Tile = tile }, null };
                Check((bool)Call(link.Player, "CheckWorkbench", benchArgs), type + " aceita receita que exige esta bancada");
                world.DestructArtifact(artifact.EntityId);
            }

            var blueprint = BlueprintStore.GetBlueprint(tableType);
            var table = Cheats.MakeAppearArtifact(new[] { "prop", tableType.ToString() }, out _).Value;
            table.Tile = tile; table.States.BuildingState = Shared.Building.BuildingState.Occupied;
            table.States.Level = 1;
            world.ConstructArtifact(table, null, context.EntityId);
            var material = Cheats.MakeItem("axe_onehand_loose_stone", 40).Value;
            foreach (var slot in blueprint.Slots)
            {
                int count = (int)typeof(Durango.Online.Player).GetMethod("SlotCapacity", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { slot, table, blueprint });
                world.ArtifactManager.AddBuildMaterials(table.EntityId, slot.slot_id, Enumerable.Range(0, count).Select(_ => material));
            }
            var buildTool = Cheats.MakeItem("axe_onehand_loose_stone", 40).Value;
            buildTool.Tags = blueprint.ToolTags.Select(t => new Tag { Id = t.Key, Level = Math.Max(40, t.Value) }).ToArray();
            context.InventoryItems.Add(buildTool);
            link.Request<BuildArtifact, Messages.Timer>(new BuildArtifact { EntityId = table.EntityId, ToolItemId = buildTool.Id });
            Check(world.ArtifactManager.Get(table.EntityId).Value.States.BuildingState == Shared.Building.BuildingState.Occupied,
                "construção aguarda o tempo ativo antes de mudar de estado");
            Call(link.Player, "UpdatePendingBuildReplies", Gauge.CurrentTime + 121);
            Check(world.ArtifactManager.Get(table.EntityId).Value.States.Level == Math.Clamp(40, Math.Max(1, blueprint.MinLevel), blueprint.MaxLevel),
                "mesa construida recebe nivel dos materiais em vez de nivel 1");

            var animal = world.AnimalManager.All.First();
            animal.Life = animal.LifeMax; animal.IsAlive = true;
            Call(world, "OnAnimalRespawned", animal);
            Call(link.Player, "SyncAnimalVisibility");
            link.PumpUntil(() => link.Messages.OfType<Survival>().Any(m => m.EntityId == animal.EntityId));
            Check(link.Messages.OfType<Survival>().Last(m => m.EntityId == animal.EntityId).Life.Get() == animal.LifeMax,
                "respawn envia vida restaurada ao cliente");
            Console.WriteLine("[bugs-check] PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }
}
