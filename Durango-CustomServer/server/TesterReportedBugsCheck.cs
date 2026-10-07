using System.Reflection;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Yaml.Util;

namespace DurangoServerNx;

internal static class TesterReportedBugsCheck
{
    private static int _checks;
    private static void Check(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException(text);
        _checks++;
        Console.WriteLine("[tester-check] OK " + text);
    }
    private static object Call(object obj, string method, params object[] args) => obj.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args);
    private static void Place(PlayerContext context, Point2 tile) => context.AppearPlayer.Move.Movements = new[]
    { new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[]
        { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };

    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-tester-fixes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            foreach (string terrain in new[] { "op60te_alpha", "ua60de_alpha", "pe10gr_1" })
                CheckIsland(root, terrain);
            foreach (var region in RegionCatalog.All)
            {
                var terrain = TerrainLoader.Load(region.TerrainId);
                var missing = NaturalInfo.FromBytes(terrain.Garden ?? Array.Empty<byte>()).Select(n => n.EntityType).Distinct()
                    .Where(t => DataHelper.GetBiomeSpriteInfo(t)?.CollectibleId is { Length: > 0 } id &&
                        id is not ("warphole" or "barrier") && CollectibleTable.GeneratorCount(t) == 0)
                    .Select(t => t + ":" + DataHelper.GetBiomeSpriteInfo(t).CollectibleId).ToArray();
                Check(missing.Length == 0, region.Id + ": auditoria de recursos do terreno; faltantes=" + string.Join(',', missing));
            }
            var advices = Json.ReadFromFile<JObject>("advices");
            Check(advices.Properties().All(p => p.Value["hints"] is JArray { Count: > 0 }),
                "todas as 58 carreiras têm dicas para o fallback textual do cliente");
            Check(SafeSave.FlushPending(), "gravações temporárias concluídas");
            Console.WriteLine($"[tester-check] PASS {_checks} verificações");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }

    private static void CheckIsland(string root, string terrain)
    {
        var state = new WorldContext { TerrainId = terrain };
        state.Initialize(Path.Combine(root, terrain + ".world"));
        var world = new World(state);
        var context = new PlayerContext { RegionId = terrain, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = terrain + "-tester", PlayerName = "Tester", PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, terrain + ".player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        var tile = new Point2(world.EntryPoint.x + 8, world.EntryPoint.y + 8);
        Place(context, tile);
        var skill = (SkillCategorySave)Call(link.Player, "CategoryState", (int)Shared.Skill.Category.Gathering);
        skill.Level = 60;
        int expectedLevel = RegionCatalog.GetTemplate(world.TerrainInfo.region_template).Level;
        world.AddNatural(tile, 11002); // junco de rio
        var touched = link.Request<Touch, Touched>(new Touch { Tile = tile, EntityType = 11002, EntityId = "" });
        Check(touched.Level == expectedLevel, terrain + ": toque mostra nível da ilha");
        string stemId = CollectibleTable.AllSpecs(11002).First(s => s.PrototypeId == "stem").Id;
        Check(touched.Collectible.Generators.Any(g => g.Id == stemId) &&
            touched.Collectible.Generators.All(g => g.Level == expectedLevel), terrain + ": junco tem caule no nível correto");
        foreach (var type in new ushort[] { 11002, 11032, 11127, 11128, 16003 })
            Check(CollectibleTable.AllSpecs(type).Any(s => s.PrototypeId is "stem" or "stem_tough"),
                terrain + ": caule disponível na variante " + type);

        string key = $"{tile.x},{tile.y}";
        var stem = touched.Collectible.Generators.First(g => g.Id == stemId);
        for (int i = 0; i < stem.Amount; i++) world.MarkGeneratorHarvested(key, stem.Id);
        Check(state.NaturalRegrow.Any(e => e.X == tile.x && e.Y == tile.y),
            terrain + ": coleta parcial agenda renovação sem esgotar folhas");
        state.Save(); Check(SafeSave.FlushPending(), terrain + ": save de mundo temporário gravado");
        var reloadState = WorldContext.Load(state.Path);
        var reloaded = new World(reloadState);
        Check(reloadState.NaturalRegrow.Any(e => e.X == tile.x && e.Y == tile.y), terrain + ": fila persiste no restart");
        Call(reloaded, "ProcessRegrow", Gauge.CurrentTime + 1000);
        Check(reloaded.NaturalTypeAt(tile) == 11002 && reloaded.HarvestedGenerators(key).Count == 0,
            terrain + ": renovação restaura todas as partes e o tipo original");
        // Migração de saves anteriores, sem fila para um spot parcialmente coletado.
        reloadState.NaturalHarvests[key] = Enumerable.Repeat(stem.Id, stem.Amount).ToList();
        reloadState.NaturalRegrow.Clear();
        var migrated = new World(reloadState);
        Check(reloadState.NaturalRegrow.Any(e => e.X == tile.x && e.Y == tile.y),
            terrain + ": save antigo com caule esgotado recebe fila de renovação");
        var data = TerrainLoader.Load(terrain);
        var types = NaturalInfo.FromBytes(data.Garden ?? Array.Empty<byte>()).Select(n => n.EntityType).Distinct().ToArray();
        var empty = types.Where(t => !string.IsNullOrEmpty(DataHelper.GetBiomeSpriteInfo(t)?.CollectibleId) &&
            DataHelper.GetBiomeSpriteInfo(t).CollectibleId != "warphole" &&
            CollectibleTable.GeneratorCount(t) == 0).ToArray();
        Check(empty.Length == 0, terrain + ": todos os tipos coletáveis do terreno têm recursos (faltantes: " + string.Join(',', empty) + ")");
        if (expectedLevel == 60) CheckCooking(link, context);
        CheckDiscovery(link, context, world, tile);
    }

    private static void CheckCooking(EconomyProtocolCheck.Link link, PlayerContext context)
    {
        foreach (string[] input in new[] { new[] { "meat", "meat" }, new[] { "fish", "fish" }, new[] { "meat", "fish" } })
        {
            var materials = input.Select(p => Cheats.MakeItem(p, 20).Value).ToArray();
            context.InventoryItems.AddRange(materials);
            var mortar = Cheats.MakeItem("mortar_01", 60).Value;
            context.InventoryItems.Add(mortar);
            int before = link.Messages.OfType<Crafted>().Count();
            var timer = link.Request<Craft, Messages.Timer>(new Craft { RecipeId = "meatball_01", ToolItemId = mortar.Id,
                Materials = new() { ["base"] = materials.Select(i => i.Id).ToArray() } });
            Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + timer.Duration + .1);
            link.PumpUntil(() => link.Messages.OfType<Crafted>().Count() > before);
            var products = link.Messages.OfType<Crafted>().Last().Items;
            Check(products.Length == 2 && products.All(p => p.Prototype == "meatball_01") &&
                materials.All(m => context.InventoryItems.All(i => i.Id != m.Id)), "receita aceita " + string.Join('+', input) + " e consome ingredientes");
            var saved = Json.Write(context.InventoryItems);
            var skillSave = (SkillSave)link.Player.GetType().GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
            string beforeSkills = Json.Write(skillSave);
            foreach (var invalid in new[] { products.Select(i => i.Id).ToArray(), new[] { products[0].Id, products[0].Id } })
            {
                link.Request<Craft, Abort>(new Craft { RecipeId = "meatball_01", ToolItemId = mortar.Id, Materials = new() { ["base"] = invalid } });
                Check(saved == Json.Write(context.InventoryItems) && beforeSkills == Json.Write(skillSave),
                    "bolinho/IDs repetidos rejeitados sem consumir itens nem conceder XP");
            }
            object[] args = { CraftRecipeStore.Get("roast_01"), new Dictionary<string, string[]> { ["base"] = new[] { products[0].Id } }, null, null };
            Check((bool)Call(link.Player, "ResolveMaterials", args), "bolinho continua disponível para receitas de preparo de alimento");
        }
    }

    private static void CheckDiscovery(EconomyProtocolCheck.Link link, PlayerContext context, World world, Point2 tile)
    {
        string templateId = world.TerrainInfo.region_template;
        var initial = link.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = templateId });
        if (initial.AnimalTypes.Length == 0) return;
        ushort type = initial.AnimalTypes[0].Item1;
        var animal = world.AnimalManager.SpawnAt(type, 1, tile);
        animal.Position = new WorldPosition(tile.x * 200, tile.y * 200);
        link.Request<DiscoverAnimal, OK>(new DiscoverAnimal { EntityId = animal.EntityId });
        link.Request<DiscoverAnimal, OK>(new DiscoverAnimal { EntityId = animal.EntityId });
        var info = link.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = templateId });
        Check(info.AnimalTypes.Single(p => p.Item1 == type).Item2 && context.DiscoveredAnimalTypes[templateId].Count == 1,
            "descoberta de espécie aparece no mapa e repetição não duplica progresso");
        var rates = link.Request<GetDiscoveryRates, DiscoveryRates>(new GetDiscoveryRates { TemplateIds = new[] { templateId } });
        Check(Math.Abs(rates.Rates[0].Item2 - 1f / initial.AnimalTypes.Length) < .0001f, "taxa reflete espécies descobertas");
        var unknown = link.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = "invalid-template" });
        Check(unknown.AnimalTypes.Length == 0, "template desconhecido não herda fauna de outra ilha");
        link.Request<DiscoverAnimal, Abort>(new DiscoverAnimal { EntityId = "invalid-animal" });
        Place(context, new Point2(tile.x + 10, tile.y));
        link.Request<DiscoverAnimal, Abort>(new DiscoverAnimal { EntityId = animal.EntityId });
        Place(context, tile);
        context.Save(); Check(SafeSave.FlushPending(), "save de personagem gravado");
        var persisted = PlayerContext.Load(context.Path);
        using var reconnect = new EconomyProtocolCheck.Link(persisted, world, null);
        Place(persisted, tile);
        var restored = reconnect.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = templateId });
        Check(restored.AnimalTypes.Single(p => p.Item1 == type).Item2, "reconexão preserva descoberta e retrato no mapa");
        foreach (var species in initial.AnimalTypes.Skip(1))
        {
            var next = world.AnimalManager.SpawnAt(species.Item1, 1, tile);
            next.Position = new WorldPosition(tile.x * 200, tile.y * 200);
            reconnect.Request<DiscoverAnimal, OK>(new DiscoverAnimal { EntityId = next.EntityId });
        }
        persisted.Save(); Check(SafeSave.FlushPending(), "descobertas completas gravadas");
        var completed = PlayerContext.Load(persisted.Path);
        using var completeLink = new EconomyProtocolCheck.Link(completed, world, null);
        var completeInfo = completeLink.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = templateId });
        Check(completeInfo.AnimalTypes.All(p => p.Item2) && completeLink.Request<GetDiscoveryRates, DiscoveryRates>(
            new GetDiscoveryRates { TemplateIds = new[] { templateId } }).Rates[0].Item2 == 1f,
            "todas as espécies e 100% de descoberta persistem após nova reconexão");
        var other = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = "other-" + templateId, PlayerName = "Other", PlayerLevel = 60 } };
        other.Initialize(Path.Combine(Path.GetDirectoryName(context.Path), "other-" + templateId + ".player"));
        using var otherLink = new EconomyProtocolCheck.Link(other, world, null);
        Check(otherLink.Request<GetDiscoveryInfo, DiscoveryInfo>(new GetDiscoveryInfo { TemplateId = templateId }).AnimalTypes.All(p => !p.Item2),
            "outro personagem conserva descobertas independentes");
    }
}
