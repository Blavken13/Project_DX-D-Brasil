using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Economy;
using Shared.Estate;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GameplayPolishCheck
{
    private static int _passed;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[polish-check] OK " + message); }
    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static PlayerContext Player(string root, string id, string region)
    {
        var context = new PlayerContext { RegionId = region, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        return context;
    }
    private static WorldContext Context(string root, string id, string terrain)
    {
        var context = new WorldContext { TerrainId = terrain };
        context.Initialize(Path.Combine(root, id + ".world"));
        return context;
    }
    private static void Place(PlayerContext context, Point2 tile) => context.AppearPlayer.Move.Movements = new[]
    { new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[]
        { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };

    private static void CheckStarterResources(string root, World world, int expectedLevel, string name)
    {
        var context = Player(root, name, world.TerrainId);
        Place(context, world.EntryPoint);
        using var link = new EconomyProtocolCheck.Link(context, world, null, true);
        var template = RegionCatalog.GetTemplate(world.TerrainInfo.region_template);
        var types = template.CollectibleLevels.Keys
            .Concat(Durango.Terrain.NaturalInfo.FromBytes(TerrainLoader.Load(world.TerrainId).Garden)
                .Select(n => n.EntityType))
            .Append((ushort)13014).Distinct().Where(t => CollectibleTable.GeneratorCount(t) > 0).ToArray();
        Check(world.RegionLevel == expectedLevel && types.Length > 0, name + " possui recursos e nivel de ilha esperado");
        foreach (ushort type in types)
        {
            int level = (int)Call(link.Player, "CurrentGatheringLevel", type);
            Check(level == expectedLevel, name + " recurso " + type + " acompanha nivel " + expectedLevel);
            var collectible = link.Player.BuildCollectibleFor("", type, world.EntryPoint);
            Check(collectible.Generators.All(g => g.Level == CollectibleTable.ClampLevel(
                CollectibleTable.FindGenerator(type, g.Id), expectedLevel)), name + " menu " + type + " projeta nivel da ilha");
        }

        // Coleta pelo TCP: o nivel enviado pelo cliente nao pode alterar o item recebido.
        var tile = new Point2(world.EntryPoint.x + 6, world.EntryPoint.y + 6);
        world.AddNatural(tile, 13014);
        Place(context, tile);
        var touched = link.Request<Touch, Touched>(new Touch { EntityId = "", EntityType = 13014, Tile = tile });
        var generator = touched.Collectible.Generators.First();
        int before = context.InventoryItems.Count;
        var timer = link.Request<Collect, Messages.Timer>(new Collect
            { EntityId = "", Tile = tile, GeneratorId = generator.Id, Level = 1 });
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.OfType<Collected>().Any());
        var gathered = context.InventoryItems.Skip(before).ToArray();
        Check(gathered.Length > 0 && gathered.All(i => i.Level ==
            Math.Min(expectedLevel, PrototypeYaml.GetItemPrototype(i.Prototype).MaxLevel)), name + " entrega itens no nivel da ilha pelo TCP");

        var carcass = world.AnimalManager.All.FirstOrDefault();
        if (carcass != null)
        {
            Check(world.AnimalManager.All.All(a => a.CombatLevel == Math.Max(1, expectedLevel - 2)),
                name + " dinos permanecem dois niveis abaixo da ilha");
            carcass.Life = 0;
            carcass.IsAlive = false;
            var collectible = link.Player.BuildCollectibleFor(carcass.EntityId, carcass.EntityType, carcass.Tile);
            Check(collectible.Generators.Length > 0 && collectible.Generators.All(g => g.Level ==
                CollectibleTable.ClampLevel(CollectibleTable.FindGenerator(carcass.EntityType, g.Id), carcass.CombatLevel)),
                name + " coleta de carcaca usa nivel do dino");
        }
    }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-polish-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            string highId = RegionCatalog.All.First(r => RegionCatalog.GetTemplate(r.TemplateId) is { Role: Role.Risky, Level: >= 40 }).Id;
            var highContext = Context(root, "high", highId);
            var high = new World(highContext);
            var lowContext = Context(root, "low", "pe10gr_1");
            var low = new World(lowContext);
            CheckStarterResources(root, new World(Context(root, "safehouse", "grass_company_safehouse_01")), 5, "safehouse");
            CheckStarterResources(root, low, 10, "ilha-domada");
            var tutorial = new World(Context(root, "tutorial", "tropical_event_ancora_01"));
            using (var tutorialLink = new EconomyProtocolCheck.Link(Player(root, "tutorial-tester", tutorial.TerrainId), tutorial, null, true))
                Check((int)Call(tutorialLink.Player, "CurrentGatheringLevel", (ushort)11009) == 1, "tutorial conserva recursos nivel 1");
            var store = new EconomyStore(Path.Combine(root, "economy.json"), new ShopCatalog());
            var context = Player(root, "tester", highId);
            Place(context, high.EntryPoint);
            using var link = new EconomyProtocolCheck.Link(context, high, store, true);
            link.Player.ContextChanged += () => context.Save();

            // Coleta real: pacotes mistos, sementes, roupas e armas precisam oferecer generators.
            foreach (ushort type in new ushort[] { 13014, 13015, 13031, 13032, 13033, 13034, 13035, 13036, 13037 })
                Check(CollectibleTable.GeneratorCount(type) > 0, "pacote " + type + " oferece itens existentes");
            Point2 parcelTile = new Point2(high.EntryPoint.x + 6, high.EntryPoint.y + 6);
            high.AddNatural(parcelTile, 13014);
            Place(context, parcelTile);
            var touched = link.Request<Touch, Touched>(new Touch { EntityId = "parcel", EntityType = 13014, Tile = parcelTile });
            Check(touched.Collectible.Generators.Length > 0, "pacote perdido permite interagir pelo TCP");
            var generator = touched.Collectible.Generators.First();
            int originalCount = context.InventoryItems.Count;
            var collect = new Collect { EntityId = "parcel", Tile = parcelTile, GeneratorId = generator.Id };
            var timer = link.Request<Collect, Messages.Timer>(collect);
            Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
            link.PumpUntil(() => link.Messages.OfType<Collected>().Any());
            var gathered = context.InventoryItems.Skip(originalCount).ToArray();
            Check(gathered.Length > 0 && gathered.All(i => i.Level == Math.Min(high.RegionLevel, PrototypeYaml.GetItemPrototype(i.Prototype).MaxLevel)),
                "itens dos pacotes acompanham o nivel da ilha");
            int highLevel = (int)Call(link.Player, "CurrentGatheringLevel", (ushort)11002);
            Check(highLevel == high.RegionLevel, "recurso com override legado 1 usa nivel da ilha alta");
            var highSpecs = CollectibleTable.Build("high-resource", 11002, resourceLevel: highLevel).Generators;
            var lowSpecs = CollectibleTable.Build("low-resource", 11002, resourceLevel: 10).Generators;
            Check(highSpecs.Length > 0 && highSpecs.All(g => g.Level == highLevel), "generators de farm projetam nivel alto");
            Check(lowSpecs.Length > 0 && lowSpecs.All(g => g.Level == 10), "cache nao vaza nivel da ilha alta para ilha baixa");

            // Capacidade, migração, desgaste e reparo na mesma unidade.
            var tool = Cheats.MakeItem("axe_onehand_loose_stone", 10).Value;
            Check(tool.Durability.Max() > 1 && tool.Durability.Get() == tool.Durability.Max(), "ferramenta nova tem durabilidade real e cheia");
            var metal = Cheats.MakeItem("axe_onehand_loose_metal", 40).Value;
            Check(metal.Durability.Max() > tool.Durability.Max(), "material e nivel melhoram durabilidade");
            tool.Durability = new Gauge(1, 0, new[] { new GaugeNode(0, .5f) });
            ItemDurability.Normalize(ref tool);
            Check(Math.Abs(tool.Durability.Get() / tool.Durability.Max() - .5f) < .001, "migracao conserva percentual de desgaste");
            context.InventoryItems.Add(tool);
            float before = tool.Durability.Get();
            Call(link.Player, "WearTool", tool.Id, "collect");
            Check(Math.Abs(context.InventoryItems.Single(i => i.Id == tool.Id).Durability.Get() - (before - ItemDurability.Delta("collect"))) < .001,
                "coleta usa delta nativa sem voltar ao maximo 1");
            var breaking = context.InventoryItems.Single(i => i.Id == tool.Id);
            breaking.Durability = new Gauge(breaking.Durability.Max(), 0, new[] { new GaugeNode(0, .1f) });
            context.InventoryItems[context.InventoryItems.FindIndex(i => i.Id == tool.Id)] = breaking;
            Call(link.Player, "WearTool", tool.Id, "collect");
            Check(context.InventoryItems.Single(i => i.Id == tool.Id).Durability.Get() == 0, "ferramenta quebrada permanece para reparo");
            string kitId = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k => PrototypeYaml.GetItemPrototype(k).Tags?.ContainsKey("tool_repair_kit") == true);
            var kit = Cheats.MakeItem(kitId, 60).Value;
            context.InventoryItems.Add(kit);
            link.Request<RepairItem, Messages.Timer>(new RepairItem { ItemId = tool.Id, KitItemIds = new[] { kit.Id } });
            var repaired = context.InventoryItems.Single(i => i.Id == tool.Id);
            Check(repaired.Durability.Max() > 1 && repaired.Durability.Get() == repaired.Durability.Max(), "reparo conserva capacidade real");
            Check(context.InventoryItems.All(i => i.Id != kit.Id), "reparo consome o kit");

            // Free expansion on every island, capped at eight without truncating old estates.
            var cell = new Point2(10, 10);
            var highEstate = high.DeclareEstate(context.EntityId, OwnerType.Player, cell, highId).Value;
            long expectedCost = EstateExpansionCost.For(OwnerType.Player, 1, high.RegionLevel);
            context.TStone = 0;
            var expanded = link.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(11, 10) });
            Check(expanded.Size == 2 && context.TStone == 0 && expectedCost == 0, "ilha alta expande gratuitamente com saldo zero");
            link.Request<ExpandEstate, Abort>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(11, 10) });
            Check(context.TStone == 0, "repeticao de expansao nao altera carteira");
            link.Request<ShrinkEstate, EstateLicense>(new ShrinkEstate { EstateId = highEstate.EstateId, Cell = new Point2(11, 10) });
            expanded = link.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(11, 10) });
            Check(expanded.Size == 2 && context.TStone == 0, "reexpansao continua gratuita como indicado no cliente");
            var allowances = link.Request<GetEstateLicenses, EstateLicenses>(default);
            Check(allowances.LargestUrbanEstateSize == 8 && allowances.LargestPersonalEstateSize == 8,
                "PC e Android recebem oito lotes gratuitos pelo mesmo protocolo");
            for (int x = 12; x <= 17; x++)
                expanded = link.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(x, 10) });
            link.Request<ExpandEstate, Abort>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(18, 10) });
            Check(expanded.Size == 8 && high.GetEstate(highEstate.EstateId).Cells.Count == 8 && context.TStone == 0,
                "oitavo lote permitido e nono recusado sem cobrar");
            // Simulate a save produced before the new cap, including a recorded maximum above eight.
            var legacy = high.GetEstate(highEstate.EstateId);
            legacy.Cells.AddRange(new[] { "18,10", "19,10", "20,10", "21,10" });
            foreach (string key in legacy.Cells) highContext.EstateCells[key] = highEstate.EstateId;
            legacy.Size = legacy.LargestSize = 12;
            link.Request<ExpandEstate, Abort>(new ExpandEstate { EstateId = highEstate.EstateId, Cell = new Point2(22, 10) });
            allowances = link.Request<GetEstateLicenses, EstateLicenses>(default);
            Check(allowances.UrbanEstate.Value.Size == 12 && legacy.Cells.Count == 12 && allowances.LargestUrbanEstateSize == 12,
                "dominio legado acima do teto conserva todos os lotes e sua licenca");
            Check(high.TryGetEstateIdAtCell(new Point2(21, 10), out string legacyId) && legacyId == highEstate.EstateId,
                "lote excedente continua pertencendo ao dominio");
            var beginner = Player(root, "beginner", low.TerrainId);
            beginner.TStone = 0;
            using var beginnerLink = new EconomyProtocolCheck.Link(beginner, low, store, true);
            var lowEstate = low.DeclareEstate(beginner.EntityId, OwnerType.Player, cell, low.TerrainId).Value;
            expanded = beginnerLink.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = lowEstate.EstateId, Cell = new Point2(11, 10) });
            Check(expanded.Size == 2 && beginner.TStone == 0, "ilha nivel 10 expande gratuitamente com saldo zero");
            Check(EstateExpansionCost.For(OwnerType.PersonalPlayer, 1, 60) == 0, "ilha particular tambem expande sem custo");

            // Expiração precisa sobreviver a reinícios e limpar conteúdo associado.
            var artifact = high.ArtifactManager.Enumerable(a => a.States.BuildingState == Shared.Building.BuildingState.Completed).First();
            artifact.EntityId = "temporary-player-building";
            artifact.States.EntityId = artifact.EntityId; artifact.Display.EntityId = artifact.EntityId;
            high.ConstructArtifact(artifact, null, context.EntityId);
            Durango.Online.Player.WarehouseStore.Items(artifact.EntityId, "", true).Add(repaired);
            double expiry = highContext.WildStructureExpirations[artifact.EntityId];
            Check(expiry >= Gauge.CurrentTime + 86390, "construcao recebe 24 horas a partir da criacao");
            high.ProcessWildStructureExpirations(expiry - 1);
            Check(high.ArtifactManager.Get(artifact.EntityId).HasValue, "construcao permanece antes do prazo");
            high.Save(); SafeSave.FlushPending();
            var reload = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(highContext.Path));
            reload.Initialize(highContext.Path);
            var restored = new World(reload);
            Check(restored.GetEstate(highEstate.EstateId).Size == 12 &&
                restored.TryGetEstateIdAtCell(new Point2(21, 10), out legacyId) && legacyId == highEstate.EstateId,
                "reiniciar conserva tamanho e celulas do dominio legado sem limitar save");
            Check(reload.WildStructureExpirations[artifact.EntityId] == expiry, "reiniciar nao renova prazo da construcao");
            int systemCount = restored.ArtifactManager.Enumerable(a => string.IsNullOrEmpty(restored.ArtifactManager.OwnerOf(a.EntityId))).Count();
            restored.ProcessWildStructureExpirations(expiry + 1);
            Check(!restored.ArtifactManager.Get(artifact.EntityId).HasValue && Durango.Online.Player.WarehouseStore.Items(artifact.EntityId, "", false) == null,
                "expiracao remove construcao e armazenamento");
            Check(restored.ArtifactManager.Enumerable(a => string.IsNullOrEmpty(restored.ArtifactManager.OwnerOf(a.EntityId))).Count() == systemCount,
                "portos crateras e portais do sistema preservados");
            var personalContext = Context(root, "protected", highId);
            personalContext.IsSettlementIsland = true;
            var personal = new World(personalContext);
            personal.ConstructArtifact(artifact, null, context.EntityId);
            personalContext.WildStructureExpirations[artifact.EntityId] = 1;
            personal.ProcessWildStructureExpirations(expiry + 1);
            Check(personal.ArtifactManager.Get(artifact.EntityId).HasValue, "ilha particular nunca aplica limpeza selvagem");
            personal.Save(); SafeSave.FlushPending();
            var protectedReload = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(personalContext.Path));
            protectedReload.Initialize(personalContext.Path);
            var protectedWorld = new World(protectedReload);
            Check(protectedReload.IsSettlementIsland && protectedWorld.ArtifactManager.Get(artifact.EntityId).HasValue,
                "assentamento protegido sobre terreno selvagem permanece protegido ao reiniciar");

            Place(context, new Point2(high.EntryPoint.x + 20, high.EntryPoint.y + 20));
            int teleports = link.Messages.OfType<Teleported>().Count();
            link.Request<WarpToPort, Messages.Timer>(default);
            link.PumpUntil(() => link.Messages.OfType<Teleported>().Count() > teleports);
            Check(link.Messages.OfType<Teleported>().Last().Tile == high.EntryPoint ||
                TerrainLoader.Load(highId).Pois.PortPoints.Contains(link.Messages.OfType<Teleported>().Last().Tile),
                "retorno a jangada teleporta ao porto pelo protocolo real");
            Console.WriteLine($"[polish-check] PASS {_passed} verificacoes. Saves temporarios: {root}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[polish-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }
}
