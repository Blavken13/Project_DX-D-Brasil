using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Shared.System;
using Yaml;
using Yaml.Util;
using Json = Durango.Utils.Json;

namespace DurangoServerNx;

internal static class WorldInteractionCheck
{
    private static int _passed;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[world-check] OK " + message); }
    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static void Place(PlayerContext context, Point2 tile) => context.AppearPlayer.Move.Movements = new[]
    { new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[]
        { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
    private static PlayerContext Player(string root, string name)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = name, PlayerName = name, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, name + ".player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        return context;
    }
    private static WorldContext Context(string root, string terrain)
    {
        var context = new WorldContext { TerrainId = terrain };
        context.Initialize(Path.Combine(root, terrain + ".world"));
        return context;
    }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-world-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            EstateAccessCheck.Run(root, Check);
            MapTravelMountCheck.Run(root, Check);
            var flowerRecipe = Json.ReadFromFile<Newtonsoft.Json.Linq.JObject>("item/recipes")["capture_tool_02"];
            Check(flowerRecipe["slots"].Any(s => (int?)s["required_materials"]?["flower"] == 20 &&
                s["source_info"].Any(source => (string)source["collectible_id"] == "bush_lilac" &&
                    (string)source["generator_id"] == "flower_lilac")), "ferramenta intermediaria exige flores nivel 20 de lilas nos dados nativos");
            var flowerGenerators = new System.Collections.Generic.Dictionary<ushort, string> {
                [11008] = "dogrose_flower", [11020] = "flower_lilac", [11091] = "lavender_flower",
                [14063] = "lavender_flower", [14064] = "wiregrass_flower" };
            foreach (var pair in flowerGenerators)
            {
                var spec = CollectibleTable.FindGenerator(pair.Key, pair.Value);
                Check(spec?.PrototypeId == "flower" && CollectibleTable.Build("flower-source", pair.Key).Generators.Any(g => g.Id == pair.Value),
                    "flor generica e id original de coleta em " + pair.Key);
            }
            Check(!CollectibleTable.AllSpecs(11002).Any(s => s.PrototypeId == "flower"), "arbusto sem flores nao recebe loot floral");

            // Save antigo com garden congelado, recurso alterado e coleta persistida.
            var legacyFlowers = Context(root, "ri35te");
            legacyFlowers.Garden = TerrainLoader.Load("ri35te").Garden;
            legacyFlowers.AddedNatural.Add(new NaturalInfo { X = 20, Y = 20, EntityType = 11002 });
            legacyFlowers.RemovedNatural.Add(new Point2(12, 12));
            legacyFlowers.NaturalHarvests["20,20"] = new System.Collections.Generic.List<string> { "leaf_small" };
            var legacyFlowerWorld = new World(legacyFlowers);
            Check(legacyFlowers.AddedNatural.Count(n => n.EntityType == 11020) == 8 &&
                legacyFlowerWorld.NaturalTypeAt(new Point2(20, 20)) == 11002 &&
                legacyFlowers.RemovedNatural.Contains(new Point2(12, 12)) && legacyFlowers.NaturalHarvests["20,20"].Count == 1,
                "save antigo recebe lilas sem apagar garden, alteracoes ou coletas");
            SafeSave.FlushPending();
            var savedFlowers = JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(legacyFlowers.Path));
            savedFlowers.Initialize(legacyFlowers.Path);
            var savedFlowerWorld = new World(savedFlowers);
            Check(savedFlowers.FlowerEcologyVersion == 1 && savedFlowers.AddedNatural.Count == legacyFlowers.AddedNatural.Count &&
                savedFlowers.NaturalHarvests["20,20"].Count == 1, "reinicio preserva flores e nao repete migracao");
            var craftContext = Player(root, "flower-crafter");
            using (var craftLink = new EconomyProtocolCheck.Link(craftContext, savedFlowerWorld, null, true))
            {
                ((SkillCategorySave)Call(craftLink.Player, "CategoryState", (int)Shared.Skill.Category.Gathering)).Level = 35;
                var lilac = savedFlowers.AddedNatural.First(n => n.EntityType == 11020);
                var lilacTile = new Point2(lilac.X, lilac.Y);
                Place(craftContext, lilacTile);
                var flowerMenu = craftLink.Request<Touch, Touched>(new Touch { EntityId = "native-lilac", EntityType = 11020, Tile = lilacTile });
                Check(flowerMenu.Collectible.Generators.Single(g => g.Id == "flower_lilac").Level == 35,
                    "spot restaurado oferece flores no nivel da ilha");
                var flowerTimer = craftLink.Request<Collect, Messages.Timer>(new Collect { EntityId = "native-lilac", Tile = lilacTile, GeneratorId = "flower_lilac" });
                Call(craftLink.Player, "UpdatePendingCollects", Gauge.CurrentTime + flowerTimer.Duration + .1);
                craftLink.PumpUntil(() => craftContext.InventoryItems.Any(i => i.Prototype == "flower"));
                var gatheredFlower = craftContext.InventoryItems.First(i => i.Prototype == "flower");
                var flowerSlot = flowerRecipe["slots"].First(s => (int?)s["required_materials"]?["flower"] == 20).ToObject<CraftRecipeSlotData>();
                Check(gatheredFlower.Level == 35 && (bool)typeof(Durango.Online.Player).GetMethod("MatchesSlot", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { gatheredFlower, flowerSlot }), "flor coletada e aceita pelo filtro real da ferramenta de domagem");
            }
            var wc = Context(root, "grass_company_safehouse_01");
            var world = new World(wc);
            var terrain = TerrainLoader.Load(wc.TerrainId);
            Check(wc.SafehouseEcologyVersion == 1 && wc.AddedNatural.Count >= 180, "safehouse recebe 180 novos spots");
            Check(wc.AddedNatural.Select(n => n.EntityType).Distinct().Count() >= 15, "recursos variados do template nativo");
            Check(wc.AddedNatural.All(n => world.CanPlaceSystemContent(new Point2(n.X, n.Y))), "spots preservam edificios e entrada");
            Check(world.AnimalManager.All.Count() == WorldTuning.SafehouseAnimalCount && world.AnimalManager.All.All(a => a.DefensiveOnly &&
                AnimalTypes.Get(a.EntityType).BaseScale <= 1 && a.CombatLevel <= 5), "quarenta dinos pequenos de baixo nivel no refugio");
            Check(world.AnimalManager.All.All(a => !(a.HomeTile.x is >= 88 and <= 145 && a.HomeTile.y is >= 70 and <= 138)),
                "fauna distribuida fora da area central da safehouse");
            var context = Player(root, "tester");
            Place(context, world.EntryPoint);
            using var link = new EconomyProtocolCheck.Link(context, world, null, true);
            link.Player.ContextChanged += () => context.Save();
            foreach (var animal in world.AnimalManager.All)
            {
                Place(context, animal.Tile);
                Call(link.Player, "AnimalTurn", animal, Gauge.CurrentTime + 1);
            }
            Check(world.AnimalManager.All.All(a => a.AggroTargetId == null), "dinos nao atacam sem provocacao");

            // Percorrer os terrenos reais, incluindo POIs originalmente em whole.garden.
            int freshwater = 0, converted = 0;
            foreach (string path in Directory.GetFiles(TerrainLoader.TerrainDir, "*.zip"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                var data = TerrainLoader.Load(id);
                Check(RegionCatalog.TryGet(id, out var region) && !string.IsNullOrWhiteSpace(region.Name), "titulo de " + id);
                var selected = link.Request<GetRegion, Messages.Region>(new GetRegion { RegionId = id });
                Check(selected.Id == id && selected.TerrainId == id && selected.Name == region.Name, "selecao retorna titulo e terreno de " + id);
                Check(region.CreatedAt > Times.UnixTimeNow() - 60 && data.Biomes.Length == data.Width * data.Height,
                    "dimensoes e validade de " + id);
                var map = link.Request<GetRegionMapInfo, RegionMapInfo>(new GetRegionMapInfo { RegionId = id });
                Check(map.TerrainId == id && map.TileCount.x == data.Width && map.TileCount.y == data.Height &&
                    map.DefoggedChunks.Chunks.Length < data.Width / 16 * (data.Height / 16), "preview preserva nevoa de " + id);
                var tc = new WorldContext { TerrainId = id };
                tc.Initialize(Path.Combine(root, id + "-catalog.world"));
                var tw = new World(tc);
                Check(tc.FlowerEcologyVersion == (id == "tropical_event_ancora_01" ? 0 : 1), "migracao floral em " + id);
                var newFlowers = tc.AddedNatural.Where(n => flowerGenerators.ContainsKey(n.EntityType)).ToArray();
                Check(newFlowers.All(n => DataHelper.GetBiomeSpriteInfo(n.EntityType).Survivability.Contains(
                    TerrainEcology.BiomeAt(data, new Point2(n.X, n.Y))) && tw.CanPlaceSystemContent(new Point2(n.X, n.Y)) &&
                    !NaturalInfo.FromBytes(data.Garden).Any(original => original.X == n.X && original.Y == n.Y)),
                    "flores preservam spots existentes, construcoes e biomas em " + id);
                if (id is "pe10gr_1" or "ri35te" or "ri30td01" or "ri55tu")
                    Check(newFlowers.Any(n => n.EntityType == 11020), "lilas restaurado para craft em " + id);
                if (id == "ri15sv01") Check(newFlowers.Any(n => n.EntityType is 11091 or 14063), "lavanda restaurada com variante do bioma real");
                if (id == "ri18tp01") Check(newFlowers.Any(n => n.EntityType == 11008), "roseira tropical restaurada");
                var template = RegionCatalog.GetTemplate(data.Info.region_template);
                if (id.EndsWith("_alpha", StringComparison.Ordinal))
                    Check(tw.RegionLevel == template.Level && (template.Role != Shared.Region.Role.Risky ||
                        tw.AnimalManager.All.All(a => a.CombatLevel == Math.Clamp(Math.Max(1, template.Level - 2),
                            AnimalTypes.Get(a.EntityType).MinCombatLevel, AnimalTypes.Get(a.EntityType).MaxCombatLevel))),
                        "nova ilha preserva nivel e fauna do template: " + id);
                if (template.Role is not (Shared.Region.Role.Personal or Shared.Region.Role.Tutorial))
                {
                    Check(data.Pois.Warpholes.Count >= 2 && data.Pois.Craters.Count >= 1 &&
                        tw.ArtifactManager.Enumerable(a => a.States.Crack.HasValue).Any(), "portais e crateras em " + id);
                    foreach (var nativeCrater in tw.ArtifactManager.Enumerable(a => a.States.Crack.HasValue).ToArray())
                        Check(tw.ActivateCrater(nativeCrater.EntityId, Gauge.CurrentTime) &&
                            tc.CraterResources[nativeCrater.EntityId].Count > 0, "inducao efetiva em " + id + "/" + nativeCrater.EntityId);
                    foreach (var nativePortal in tw.ArtifactManager.Enumerable(a => a.EntityType == 9450))
                        Check(tw.CanPlaceSystemContent(tw.PortalLanding(nativePortal.Tile), 0), $"chegada segura em {id} [{nativePortal.Tile.x},{nativePortal.Tile.y}]");
                }
                foreach (var n in NaturalInfo.FromBytes(data.Garden ?? Array.Empty<byte>()))
                {
                    var tile = new Point2(n.X, n.Y);
                    if (n.EntityType is 15001 or 15002 or 15004 or 15006)
                    {
                        Check(tw.NaturalTypeAt(tile) == 0 && tw.ArtifactManager.Enumerable(a =>
                            a.Tile.x == tile.x && a.Tile.y == tile.y).Any(), "POI natural convertido em " + id);
                        converted++;
                    }
                    string collectible = DataHelper.GetBiomeSpriteInfo(n.EntityType)?.CollectibleId;
                    if (!TerrainEcology.IsFishing(collectible) || collectible.Contains("ocean")) continue;
                    Check(tw.NaturalTypeAt(tile) == n.EntityType, "pesca de rio/lago preservada em " + id);
                    freshwater++;
                }
            }
            Check(freshwater > 0, "suite cobre pesca nativa fora do oceano");
            Console.WriteLine($"[world-check] {converted} POIs convertidos encontrados nos gardens");
            var levelField = typeof(Durango.Online.Player).GetField("_skillLevel", BindingFlags.Instance | BindingFlags.NonPublic);
            object originalLevel = levelField.GetValue(link.Player);
            levelField.SetValue(link.Player, 60);
            var routes = link.Request<GetRoutes, Routes>(default);
            Check(routes._Routes.Values.SelectMany(byTemplate => byTemplate.Values).SelectMany(r => r)
                .Select(r => r.RegionId).Distinct().Count() == RegionCatalog.All.Count, "navegacao inclui todas as ilhas publicas para nivel 60");
            levelField.SetValue(link.Player, originalLevel);

            // Pesca efetiva com arpao nativo, do Touch ate o inventario.
            ushort fishingType = (ushort)Enumerable.Range(10000, 6000).First(t =>
                TerrainEcology.IsFishing(DataHelper.GetBiomeSpriteInfo((ushort)t)?.CollectibleId) &&
                !DataHelper.GetBiomeSpriteInfo((ushort)t).CollectibleId.Contains("ocean"));
            var fishTile = new Point2(60, 60);
            world.AddNatural(fishTile, fishingType);
            Place(context, fishTile);
            var menu = link.Request<Touch, Touched>(new Touch { EntityId = "fish-check", EntityType = fishingType, Tile = fishTile });
            var generators = menu.Collectible.Generators;
            Check(generators.Length > 0 && generators.All(g => g.ToolRequirements.ContainsKey("harpoon")), "pesca oferece generators que exigem arpao");
            Check(generators.Any(g => g.Enabled), "pesca nao exige habilidade ausente da arvore nativa");
            var generator = generators.First(g => g.Enabled);
            var collect = new Collect { EntityId = "fish-check", Tile = fishTile, GeneratorId = generator.Id };
            int beforeFishing = context.InventoryItems.Count;
            link.Request<Collect, ToolNeeded>(collect);
            Check(context.InventoryItems.Count == beforeFishing, "mao livre nao produz peixe");
            string toolId = SingletonDict<string, System.Collections.Generic.List<Prototype>>.Instance.Keys.First(id =>
                PrototypeYaml.GetItemPrototype(id).Tags?.ContainsKey("harpoon") == true);
            var harpoon = Cheats.MakeItem(toolId, 60).Value;
            context.InventoryItems.Add(harpoon);
            collect.ToolItemId = harpoon.Id;
            var timer = link.Request<Collect, Messages.Timer>(collect);
            Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
            link.PumpUntil(() => link.Messages.OfType<Collected>().Any());
            Check(context.InventoryItems.Count > beforeFishing + 1 && link.Messages.OfType<Collected>().Any(), "arpao coleta peixe no protocolo real");

            // Coleta real: manter ids de protocolo, mas entregar flower no inventario.
            int flowerIndex = 0;
            foreach (var pair in flowerGenerators)
            {
                var flowerTile = new Point2(64 + flowerIndex++ * 2, 64);
                world.AddNatural(flowerTile, pair.Key);
                Place(context, flowerTile);
                string id = "flower-check-" + pair.Key;
                var floralMenu = link.Request<Touch, Touched>(new Touch { EntityId = id, EntityType = pair.Key, Tile = flowerTile });
                Check(floralMenu.Collectible.Generators.Any(g => g.Id == pair.Value && g.Enabled), "TCP oferece flor coletavel em " + pair.Key);
                int before = context.InventoryItems.Count;
                var floralTimer = link.Request<Collect, Messages.Timer>(new Collect { EntityId = id, Tile = flowerTile, GeneratorId = pair.Value });
                Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + floralTimer.Duration + .1);
                link.PumpUntil(() => context.InventoryItems.Count > before);
                Check(context.InventoryItems.Last().Prototype == "flower", "TCP entrega item flower em " + pair.Key);
            }

            var portals = world.ArtifactManager.Enumerable(a => BlueprintStore.GetBlueprint(a.EntityType)?.Components?.Contains("Warphole") == true).Take(2).ToArray();
            foreach (var portal in portals)
            {
                Place(context, portal.Tile);
                var touched = link.Request<Touch, Touched>(new Touch { EntityId = portal.EntityId, EntityType = portal.EntityType, Tile = portal.Tile });
                Check(touched.Interactions.Contains((int)Interaction.Warp), "portal oferece acao de viagem");
                link.Request<IsWarpholeAvailable, OK>(new IsWarpholeAvailable { EntityId = portal.EntityId, Tile = portal.Tile });
            }
            var camp = world.ArtifactManager.Enumerable(a => a.EntityType == 9101).First();
            Place(context, camp.Tile);
            link.Request<IsWarpholeAvailable, OK>(new IsWarpholeAvailable { EntityId = camp.EntityId, Tile = camp.Tile });
            Check(context.ExploredPOIs.Values.Any(p => p.EntityType == 9101), "portal do acampamento tambem funciona");
            var costs = link.Request<GetWarpCosts, WarpCosts>(default);
            Check(costs.Costs.Length >= 3, "mapa de viagem inclui portais descobertos");
            Check(context.ExploredPOIs.Values.Count(p => p.Type == (int)Shared.System.PointOfInterest.CargoWarphole) >= 2, "descoberta de portais persistida");
            var destination = portals[0];
            timer = link.Request<Warp, Messages.Timer>(new Warp { Tile = destination.Tile });
            link.Request<Warp, Abort>(new Warp { Tile = destination.Tile });
            link.Request<ReturnToHome, Abort>(default);
            Call(link.Player, "UpdateTravelWarp", Gauge.CurrentTime + timer.Duration + .1);
            link.PumpUntil(() => link.Messages.OfType<Teleported>().Any());
            var landing = link.Messages.OfType<Teleported>().Last().Tile;
            Check(world.CanPlaceSystemContent(landing, 0) && world.NaturalTypeAt(landing) == 0, "viagem termina fora da estrutura do portal");

            var crater = world.ArtifactManager.Enumerable(a => a.States.Crack.HasValue).First();
            Place(context, crater.Tile);
            var cm = link.Request<Touch, Touched>(new Touch { EntityId = crater.EntityId, EntityType = crater.EntityType, Tile = crater.Tile });
            Check(cm.Interactions.Contains((int)Interaction.Invest), "cratera oferece inducao");
            int cost = crater.States.Crack.Value.RequiredInvestment;
            Check(cost == Math.Max(1, (int)(RegionCatalog.GetTemplate(world.TerrainInfo.region_template).Level * .2)), "custo original da cratera");
            var invest = new InvestToCrack { EntityId = crater.EntityId, Tile = crater.Tile, Amount = cost };
            link.Request<InvestToCrack, Abort>(invest);
            Check(link.Player.InductionStones == 0 && crater.States.Crack.Value.ActivatedUntil == null, "sem pedras nao ativa cratera");
            link.Player.AddInductionStones(10);
            Check(link.Player.InductionStones == 10, "pedras de inducao recebidas na carteira");
            invest.Amount = cost + 1;
            link.Request<InvestToCrack, Abort>(invest);
            invest.Amount = cost;
            Check((bool)Call(link.Player, "CanInvestInCrater", world.ArtifactManager.Get(crater.EntityId), cost, Gauge.CurrentTime),
                $"inducao autorizada: vivo={context.AppearPlayer.IsAlive}, perto={Call(link.Player, "IsWithinTiles", crater.Tile, 10)}, estado={world.ArtifactManager.Get(crater.EntityId).Value.States.Crack}");
            timer = link.Request<InvestToCrack, Messages.Timer>(invest);
            Check(link.Player.InductionStones == 10 && world.ArtifactManager.Get(crater.EntityId).Value.States.Crack.Value.ActivatedUntil == null,
                "inducao espera quatro segundos antes de cobrar");
            link.Request<InvestToCrack, Abort>(invest);
            using (var other = new EconomyProtocolCheck.Link(Player(root, "other"), world, null, true))
            {
                var oc = (PlayerContext)typeof(Durango.Online.Player).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(other.Player);
                Place(oc, crater.Tile); other.Player.AddInductionStones(10);
                other.Request<InvestToCrack, Abort>(invest);
                Check(other.Player.InductionStones == 10, "outro jogador nao paga cratera reservada");
            }
            Call(link.Player, "UpdateCraterInvestment", Gauge.CurrentTime + timer.Duration + .1);
            Check(link.Player.InductionStones == 10 - cost && wc.CraterResources[crater.EntityId].Count > 0, "inducao cobra uma vez e gera recursos");
            Check(world.ArtifactManager.Get(crater.EntityId).Value.States.Crack.Value.ActivatedUntil.HasValue, "estado de ativacao publicado");
            Call(link.Player, "UpdateCraterInvestment", Gauge.CurrentTime + timer.Duration + 1);
            Check(link.Player.InductionStones == 10 - cost, "reprocessar nao cobra novamente");
            link.Request<InvestToCrack, Abort>(invest);
            SafeSave.FlushPending();
            var restored = JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(wc.Path));
            restored.Initialize(wc.Path);
            var restoredWorld = new World(restored);
            Check(restored.AddedNatural.Count == wc.AddedNatural.Count && restored.Artifacts.Count == wc.Artifacts.Count,
                "reiniciar preserva safehouse e cratera sem duplicar");
            var playerReload = JsonConvert.DeserializeObject<PlayerContext>(File.ReadAllText(context.Path));
            Check(playerReload.Vouchers[CrackTuning.VoucherId] == 10 - cost, "saldo de pedras persistido");
            double until = restored.Artifacts[crater.EntityId].States.Crack.Value.ActivatedUntil.Value;
            var spots = restored.CraterResources[crater.EntityId].ToArray();
            var depleted = new Point2(spots[0].X, spots[0].Y);
            restoredWorld.DestroyNatural(depleted);
            Check(restored.NaturalRegrow.Any(n => n.X == depleted.x && n.Y == depleted.y), "recurso temporario coletado possui fila antes de expirar");
            Call(restoredWorld, "ProcessCraterStates", until + .1);
            Check(!restored.Artifacts[crater.EntityId].States.Crack.Value.ActivatedUntil.HasValue &&
                !restored.CraterResources.ContainsKey(crater.EntityId), "cratera fecha apos dez minutos");
            Check(spots.All(n => restoredWorld.NaturalTypeAt(new Point2(n.X, n.Y)) == 0) &&
                restored.NaturalRegrow.All(r => !spots.Any(n => n.X == r.X && n.Y == r.Y)), "expiracao remove recursos temporarios e filas de regrow");
            Check(!restored.RemovedNatural.Any(p => p.x == depleted.x && p.y == depleted.y) &&
                restoredWorld.ActivateCrater(crater.EntityId, until + 1), "cratera pode gerar novos recursos depois de coletar e expirar");
            // Cancelamento por distancia e desconexao preserva saldo e libera a reserva.
            Call(world, "ProcessCraterStates", until + .1);
            Place(context, crater.Tile);
            timer = link.Request<InvestToCrack, Messages.Timer>(invest);
            Place(context, world.EntryPoint);
            Call(link.Player, "UpdateCraterInvestment", Gauge.CurrentTime + timer.Duration + .1);
            Check(link.Player.InductionStones == 10 - cost && !wc.Artifacts[crater.EntityId].States.Crack.Value.ActivatedUntil.HasValue,
                "sair do alcance cancela inducao sem cobrar");
            using (var cancel = new EconomyProtocolCheck.Link(Player(root, "cancel"), world, null, true))
            {
                var cc = (PlayerContext)typeof(Durango.Online.Player).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cancel.Player);
                Place(cc, crater.Tile);
                cancel.Request<Cheat, Abort>(new Cheat { _Cheat = "voucher " + CrackTuning.VoucherId + " 10" });
                Check(cancel.Player.InductionStones == 0, "jogador comum nao pode gerar pedras");
                Durango.Online.Player.Admins.Add(context.EntityId);
                link.Request<Cheat, OK>(new Cheat { _Cheat = "voucher " + CrackTuning.VoucherId + " 10 " + cc.EntityId });
                Durango.Online.Player.Admins.Remove(context.EntityId);
                Check(cancel.Player.InductionStones == 10, "admin pode abastecer testador sem dar permissao administrativa");
                cancel.Request<InvestToCrack, Messages.Timer>(invest);
                cancel.Server.Close();
                Call(cancel.Player, "UpdateCraterInvestment", Gauge.CurrentTime + 10);
                Check(cancel.Player.InductionStones == 10 && world.ReserveCrater(crater.EntityId), "desconexao preserva pedras e libera cratera");
                world.ReleaseCrater(crater.EntityId);
            }
            Console.WriteLine($"[world-check] PASS {_passed} verificacoes. Saves temporarios: {root}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[world-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }
}
