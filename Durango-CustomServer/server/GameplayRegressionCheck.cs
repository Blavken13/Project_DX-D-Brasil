using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GameplayRegressionCheck
{
    private static int _passed;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[gameplay-check] OK " + message); }

    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-gameplay-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var worldContext = new WorldContext { TerrainId = "pe10gr_1" };
            worldContext.Initialize(Path.Combine(root, "regular.world"));
            var world = new World(worldContext);
            var context = Context(root, "tester");
            using var link = new EconomyProtocolCheck.Link(context, world, null, true);
            var player = link.Player;
            var survival = Field<SurvivalState>(player, "_survival");
            var tile = world.EntryPoint;
            Place(context, tile);

            var animalRow = Json.ReadFromFile<JObject>("entity_types/animal").Properties().First(p =>
                ushort.TryParse(p.Name, out var type) && AnimalTypes.Get(type)?.Tamable == true &&
                PrototypeYaml.GetItemPrototype(AnimalTypes.Get(type).TamingResult) != null &&
                AnimalMotions.Of(type)?.PickAttack(null) != null);
            var animal = world.AnimalManager.SpawnAt(ushort.Parse(animalRow.Name), 1, tile);
            animal.Position = new WorldPosition(tile.x * 200, tile.y * 200);
            animal.AggroTargetId = context.EntityId; animal.Attack = 100;
            double now = Gauge.CurrentTime;
            float life = survival.ValueAt(SurvivalState.KeyLife, now);
            int damages = link.Messages.OfType<Damaged>().Count();
            int motions = link.Messages.OfType<Move>().Count(m => m.EntityId == animal.EntityId);
            Call(player, "AnimalTurn", animal, now);
            Check(animal.AttackAt == now + Player.AnimalAttackWarningSeconds, "ataque agendado dois segundos depois do aviso");
            link.PumpUntil(() => link.Messages.OfType<CombatInteraction>().Any(m => m.Details.ContainsKey("notice_attack")));
            var warning = link.Messages.OfType<CombatInteraction>().Last(m => m.Details.ContainsKey("notice_attack"));
            Check(Math.Abs(warning.Details["notice_attack"] / 1000d - animal.AttackAt) < .002,
                "aviso usa timestamp esperado pelo cliente original");
            player.ResolveAnimalAttack(animal, now + 1);
            Check(survival.ValueAt(SurvivalState.KeyLife, now) == life && link.Messages.OfType<Damaged>().Count() == damages,
                "aviso nao aplica dano antes do ataque");
            player.ResolveAnimalAttack(animal, now + 2);
            Check(animal.AttackHitAt > now + 2 && survival.ValueAt(SurvivalState.KeyLife, now) == life,
                "animacao precede o impacto");
            link.PumpUntil(() => link.Messages.OfType<Move>().Count(m => m.EntityId == animal.EntityId) > motions);
            Check(link.Messages.OfType<Move>().Last(m => m.EntityId == animal.EntityId).Movements[0].MotionName != null,
                "ataque envia clip nativo para o cliente");
            player.ResolveAnimalAttack(animal, now + 2.4);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) < life, "impacto aplica dano uma vez");
            float afterHit = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
            player.ResolveAnimalAttack(animal, now + 3);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) >= afterHit, "mesmo impacto nao repete dano");
            survival.Set(SurvivalState.KeyLife, survival.MaxOf(SurvivalState.KeyLife, Gauge.CurrentTime));
            survival.Flush(Gauge.CurrentTime);
            animal.NextAttackAt = animal.StandAt = 0;
            Call(player, "AnimalTurn", animal, now + 4);
            Place(context, new Point2(tile.x + 3, tile.y));
            player.ResolveAnimalAttack(animal, now + 6);
            float beforeDodgeHit = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
            player.ResolveAnimalAttack(animal, now + 6.4);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) >= beforeDodgeHit,
                "sair do alcance durante aviso evita o dano");
            Place(context, tile);
            animal.NextAttackAt = animal.StandAt = 0;
            Call(player, "AnimalTurn", animal, now + 8);
            animal.IsAlive = false;
            player.ResolveAnimalAttack(animal, now + 11);
            Check(animal.AttackAt == 0 && animal.AttackHitAt == 0, "morte do dino cancela ataque pendente");
            animal.IsAlive = true;

            var bonfire = Cheats.MakeAppearArtifact(new[] { "prop", "7000" }, out _).Value;
            bonfire.Tile = tile;
            world.ArtifactManager.AddArtifact(bonfire);
            survival.Set(SurvivalState.KeyFatigue, 300); survival.Flush(Gauge.CurrentTime);
            link.Request<RestOn, OK>(new RestOn { EntityId = bonfire.EntityId, Tile = tile });
            Check(survival.IsResting && survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime + 1) < 300,
                "primeiro RestOn ativa descanso e reduz fadiga");
            Call(player, "HandleMoveMsg", new object[] { MovementAt(tile, "Barehand_Sit_A", Gauge.CurrentTime, 100) });
            Check(survival.IsResting, "ajuste de posicao ao sentar preserva descanso");
            Check(survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime + 1) < 300,
                "fadiga continua caindo depois do movimento de sentar");
            Call(player, "HandleMoveMsg", new object[] { MovementAt(tile, "Walk", Gauge.CurrentTime + 1, 300) });
            Check(!survival.IsResting, "caminhar de verdade encerra descanso");
            link.Request<RestOn, Abort>(new RestOn { EntityId = "invalid", Tile = tile });
            Check(!survival.IsResting, "abrigo inexistente nao concede descanso");
            Place(context, tile);

            string toolPrototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(id =>
                PrototypeYaml.GetItemPrototype(id).Tags?.ContainsKey("capturable") == true);
            var tool = Cheats.MakeItem(toolPrototype, 60).Value;
            context.InventoryItems.Add(tool);
            animal.Life = animal.LifeMax * .001f;
            int inventoryCount = context.InventoryItems.Count;
            int deaths = link.Messages.OfType<EntityDied>().Count();
            link.Request<UseTamingAction, Messages.Timer>(new UseTamingAction { EntityId = animal.EntityId, ToolItemId = tool.Id });
            Check(animal.IsAlive && !animal.Captured && context.InventoryItems.Count == inventoryCount,
                "captura aguarda o tempo da ferramenta sem remover animal instantaneamente");
            Call(player, "UpdateTaming", Gauge.CurrentTime + TamingTuning.TamingTime + .1);
            // A chance nativa é probabilística. Repetir uma falha legítima não altera o RNG de produção.
            for (int attempt = 0; !animal.Captured && attempt < 24; attempt++)
            {
                player.GetType().GetField("_lastTamingAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0d);
                link.Request<UseTamingAction, Messages.Timer>(new UseTamingAction { EntityId = animal.EntityId, ToolItemId = tool.Id });
                Call(player, "UpdateTaming", Gauge.CurrentTime + TamingTuning.TamingTime + .1);
            }
            Check(animal.Captured && !animal.IsAlive && animal.Butchered, "captura remove animal e bloqueia carcaca");
            var rein = context.InventoryItems.Last(i => i.Prototype == AnimalTypes.Get(animal.EntityType).TamingResult);
            Check(rein.Ext is Reins { Domesticated: false }, "captura entrega animal nao domesticado para o cercado");
            link.PumpUntil(() => link.Messages.OfType<Rewarded>().Any(m => m.Effect is TamingCompletedEffect));
            Check(link.Messages.OfType<EntityDied>().Count() == deaths && link.Messages.OfType<DisappearEntity>().Any(m => m.EntityId == animal.EntityId),
                "cliente recebe desaparecimento, sem mensagem de morte");
            var collectible = link.Request<GetCollectible, Collectible>(new GetCollectible { EntityId = animal.EntityId, Tile = tile });
            Check(collectible.Generators.Length == 0, "animal capturado nao oferece recursos");
            link.Request<Collect, Abort>(new Collect { EntityId = animal.EntityId, Tile = tile, GeneratorId = "meat" });
            Check(context.InventoryItems.Count == inventoryCount + 1, "tentativa de farmar captura nao gera itens");
            Call(player, "UpdateTaming", Gauge.CurrentTime + 10);
            Check(context.InventoryItems.Count == inventoryCount + 1, "captura nao entrega animal duas vezes");
            world.AnimalManager.Process(animal.DiedAt + AnimalManager.CorpseDisposeDelay + 1, _ => { });
            Check(animal.IsAlive && !animal.Captured && !animal.Butchered && animal.CaptureOwnerId == null,
                "fauna repoe animal capturado sem manter estado de carcaca");

            void BeginCapture()
            {
                player.GetType().GetField("_lastTamingAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0d);
                animal.Life = animal.LifeMax * .001f;
                Place(context, animal.Tile);
                link.Request<UseTamingAction, Messages.Timer>(new UseTamingAction { EntityId = animal.EntityId, ToolItemId = tool.Id });
            }
            BeginCapture();
            link.Request<UseTamingAction, Abort>(new UseTamingAction { EntityId = animal.EntityId, ToolItemId = tool.Id });
            Check(animal.CaptureOwnerId == context.EntityId, "pedido duplicado nao substitui captura em andamento");
            context.InventoryItems.RemoveAll(i => i.Id == tool.Id);
            Call(player, "UpdateTaming", Gauge.CurrentTime + TamingTuning.TamingTime + .1);
            Check(animal.IsAlive && !animal.Captured && animal.CaptureOwnerId == null && context.InventoryItems.Count == inventoryCount,
                "perder ferramenta interrompe captura sem remover animal ou entregar outro");
            context.InventoryItems.Add(tool);
            BeginCapture();
            Place(context, new Point2(animal.Tile.x + 20, animal.Tile.y));
            Call(player, "UpdateTaming", Gauge.CurrentTime + TamingTuning.TamingTime + .1);
            Check(animal.IsAlive && !animal.Captured && animal.CaptureOwnerId == null,
                "sair do alcance interrompe captura e libera alvo");
            Place(context, tile);

            var tutorialContext = new WorldContext { TerrainId = "tropical_event_ancora_01" };
            tutorialContext.Initialize(Path.Combine(root, "tutorial.world"));
            var tutorial = new World(tutorialContext);
            Check(tutorial.IsTutorialIsland, "terrain Ancora reconhecido como tutorial");
            var obstructionTile = new Point2(49, 48);
            Check(tutorial.NaturalTypeAt(obstructionTile) == 0,
                "acacia que encobre o resgate nao e enviada no garden de Ancora");
            Check(tutorial.NaturalTypeAt(new Point2(49, 57)) == 14024,
                "outras acacias decorativas de Ancora permanecem no mapa");
            var originalTutorial = TerrainLoader.Load("tropical_event_ancora_01");
            var originalNaturals = Durango.Terrain.NaturalInfo.FromBytes(originalTutorial.Garden);
            Check(originalNaturals.Where(n => n.X != 49 || n.Y != 48 || n.EntityType != 14024)
                    .All(n => tutorial.NaturalTypeAt(new Point2(n.X, n.Y)) == n.EntityType),
                "remocao pontual preserva todos os demais recursos naturais originais de Ancora");
            var originalLandmarks = Durango.Terrain.LandmarkInfo.FromBytes(originalTutorial.Landmarks);
            Check(originalLandmarks.GroupBy(n => Durango.Terrain.Util.TilePositionToChunkCoords(new Point2(n.X, n.Y)))
                    .All(group => Durango.Terrain.LandmarkInfo.ToBytes(group.ToList())
                        .SequenceEqual(tutorial.GetChunkLandmark(group.Key))),
                "landmarks de rochas, espinheiros, dinossauros e NPCs sao enviados sem alteracoes");
            tutorial.AddNatural(obstructionTile, 14024);
            Check(tutorial.NaturalTypeAt(obstructionTile) == 0,
                "renovacao nao recria a arvore na frente da cena");
            var legacyTutorialContext = new WorldContext { TerrainId = "tropical_event_ancora_01" };
            legacyTutorialContext.Initialize(Path.Combine(root, "legacy-tutorial.world"));
            legacyTutorialContext.Garden = TerrainLoader.Load("tropical_event_ancora_01").Garden;
            legacyTutorialContext.AddedNatural.Add(new Durango.Terrain.NaturalInfo
                { X = 49, Y = 48, EntityType = 14024 });
            var legacyTutorial = new World(legacyTutorialContext);
            Check(legacyTutorial.NaturalTypeAt(obstructionTile) == 0,
                "garden persistido e naturais adicionados antigos tambem filtram a arvore");
            world.AddNatural(obstructionTile, 14024);
            Check(world.NaturalTypeAt(obstructionTile) == 14024,
                "mesmas coordenadas e especie seguem permitidas em outros mapas");
            Point2 spot = tutorial.EntryPoint;
            tutorial.AddNatural(spot, 11002);
            string key = $"{spot.x},{spot.y}";
            tutorial.MarkGeneratorHarvested(key, "leaf_small");
            var refresh = tutorialContext.NaturalRegrow.Single(e => e.X == spot.x && e.Y == spot.y);
            double due = refresh.DueAt;
            Check(Math.Abs(due - Gauge.CurrentTime - WorldTuning.TutorialNaturalRegrowSeconds) < 1,
                "primeira coleta parcial agenda renovacao em 120 segundos");
            tutorial.MarkGeneratorHarvested(key, "leaf_small");
            Check(refresh.DueAt == due && tutorialContext.NaturalRegrow.Count(e => e.X == spot.x && e.Y == spot.y) == 1,
                "novas coletas nao adiam renovacao nem duplicam fila");
            Call(tutorial, "ProcessRegrow", due - .01);
            Check(tutorial.HarvestedGenerators(key).Count == 2, "spot nao renova antes do prazo");
            Call(tutorial, "ProcessRegrow", due + .01);
            Check(tutorial.NaturalTypeAt(spot) == 11002 && tutorial.HarvestedGenerators(key).Count == 0,
                "wipe parcial restaura tipo original e todos os recursos");
            Check(tutorial.NaturalGeneration(spot) == 1, "renovacao invalida operacoes da geracao anterior");
            tutorial.MarkGeneratorHarvested(key, "leaf_small");
            double firstDue = tutorialContext.NaturalRegrow.Single(e => e.X == spot.x && e.Y == spot.y).DueAt;
            tutorial.DestroyNatural(spot);
            Check(tutorialContext.NaturalRegrow.Single(e => e.X == spot.x && e.Y == spot.y).DueAt == firstDue,
                "esvaziar spot nao posterga prazo iniciado na primeira coleta");
            Call(tutorial, "ProcessRegrow", firstDue + 1);
            Check(tutorial.NaturalTypeAt(spot) == 11002, "spot totalmente vazio tambem reaparece");

            var tutorialPlayerContext = Context(root, "tutorial-tester");
            using var tutorialLink = new EconomyProtocolCheck.Link(tutorialPlayerContext, tutorial, null, true);
            Place(tutorialPlayerContext, spot);
            var fakeItem = Cheats.MakeItem(toolPrototype, 1).Value;
            tutorial.MarkGeneratorHarvested(key, "leaf_small");
            int itemCount = tutorialPlayerContext.InventoryItems.Count;
            Call(tutorialLink.Player, "ScheduleCollectFinish", default(Collected), new List<Item> { fakeItem }, 50u,
                2f, key, spot, "spot-test", false, true, null, "leaf_small");
            due = tutorialContext.NaturalRegrow.Single(e => e.X == spot.x && e.Y == spot.y).DueAt;
            Call(tutorial, "ProcessRegrow", due + 1);
            Call(tutorialLink.Player, "UpdatePendingCollects", Gauge.CurrentTime + 3);
            Check(tutorialPlayerContext.InventoryItems.Count == itemCount && tutorial.NaturalTypeAt(spot) == 11002,
                "coleta anterior ao wipe nao duplica itens nem remove recurso novo");
            tutorial.MarkGeneratorHarvested(key, "leaf_small");
            Call(tutorialLink.Player, "ScheduleCollectFinish", default(Collected), new List<Item> { fakeItem }, 51u,
                2f, key, spot, "spot-test", false, false, null, "leaf_small");
            Call(tutorialLink.Player, "UpdatePendingCollects", Gauge.CurrentTime + 1);
            Check(tutorialPlayerContext.InventoryItems.Count == itemCount, "coleta normal nao entrega antes do tempo");
            Call(tutorialLink.Player, "UpdatePendingCollects", Gauge.CurrentTime + 3);
            Check(tutorialPlayerContext.InventoryItems.Exists(i => i.Id == fakeItem.Id), "coleta normal continua entregando item ao concluir");

            var clientSpot = new Point2(spot.x + 4, spot.y + 4);
            // Reproduzir um spot que só aparece no Touch do cliente, sem whole.garden.
            tutorial.ObserveTutorialNatural(clientSpot, 11002);
            tutorial.MarkGeneratorHarvested($"{clientSpot.x},{clientSpot.y}", "leaf_small");
            Check(tutorialContext.NaturalRegrow.Any(e => e.X == clientSpot.x && e.Y == clientSpot.y),
                "recursos conhecidos pelo cliente tambem entram na fila de renovacao");
            var regularSpot = new Point2(world.EntryPoint.x + 2, world.EntryPoint.y + 2);
            world.AddNatural(regularSpot, 11002);
            world.MarkGeneratorHarvested($"{regularSpot.x},{regularSpot.y}", "leaf_small");
            Check(worldContext.NaturalRegrow.All(e => e.X != regularSpot.x || e.Y != regularSpot.y),
                "renovacao parcial restrita ao tutorial");
            SafeSave.FlushPending();
            Check(File.Exists(tutorialContext.Path), "fila de renovacao e coleta persistidas no save do mundo");
            var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(tutorialContext.Path));
            reloaded.Initialize(tutorialContext.Path);
            var restoredWorld = new World(reloaded);
            Check(reloaded.NaturalRegrow.Any(e => e.X == clientSpot.x && e.Y == clientSpot.y) &&
                restoredWorld.HarvestedGenerators(key).Count == 1,
                "reiniciar preserva fila e estado de coleta parcial");
            reloaded.NaturalRegrow.Clear();
            var legacyWorld = new World(reloaded);
            Check(reloaded.NaturalRegrow.Any(e => e.X == spot.x && e.Y == spot.y),
                "save antigo com coleta parcial recebe renovacao ao carregar");
            Console.WriteLine($"[gameplay-check] PASS {_passed} verificacoes. Saves temporarios: {root}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[gameplay-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }

    private static PlayerContext Context(string root, string id)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        return context;
    }
    private static Movement[] MovementAt(Point2 tile, string motion, double now, int offset = 0) => new[]
    { new Movement { MotionName = motion, PlaybackRate = 1, Path = new[]
        { new Location { Position = new WorldPosition(tile.x * 200 + offset, tile.y * 200), Time = now } } } };
    private static void Place(PlayerContext context, Point2 tile) => context.AppearPlayer.Move.Movements = MovementAt(tile, "Stand", Gauge.CurrentTime);
}
