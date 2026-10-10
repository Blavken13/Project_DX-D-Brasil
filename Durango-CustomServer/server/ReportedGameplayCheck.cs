using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Ability;
using Shared.Animal;
using Shared.Item;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class ReportedGameplayCheck
{
    private static int _passed;
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        _passed++;
        Console.WriteLine("[reported-gameplay] OK " + text);
    }
    private static void Recalc(Player.PetStore.Entry entry) => typeof(Player)
        .GetMethod("RecalcPetStats", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { entry });
    private static void Position(PlayerContext player, Point2 tile) => player.AppearPlayer.Move.Movements = new[]
    {
        new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(tile.x * 200 + 50, tile.y * 200 + 50), Time = Gauge.CurrentTime } } }
    };

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-reported-gameplay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var naturals = Json.ReadFromFile<JObject>("entity_types/natural");
            var cacti = naturals.Properties().Where(p => ((string)p.Value["collectible_id"])?.StartsWith("cactus_") == true).ToArray();
            Check(cacti.Length > 0, "catalogo contém os cactos nativos");
            foreach (var cactus in cacti)
            {
                var specs = CollectibleTable.AllSpecs(ushort.Parse(cactus.Name)).ToArray();
                Check(specs.Any(s => s.PrototypeId == "cactus") && !specs.Any(s => s.PrototypeId == "leaf"),
                    "cacto " + cactus.Name + " oferece polpa em vez de folha");
            }
            foreach (var rock in naturals.Properties().Where(p =>
                ((string)p.Value["collectible_id"])?.StartsWith("basalt") == true ||
                ((string)p.Value["collectible_id"])?.StartsWith("granite") == true))
            {
                string expected = ((string)rock.Value["collectible_id"]).StartsWith("basalt") ? "basalt" : "granite";
                var specs = CollectibleTable.AllSpecs(ushort.Parse(rock.Name)).ToArray();
                Check(specs.Any(s => s.PrototypeId == expected) && !specs.Any(s => s.PrototypeId == "stone"),
                    "rocha " + rock.Name + " oferece " + expected + " sem seixo genérico");
            }
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "reported-owner", PlayerName = "reported-owner", PlayerLevel = 60 } };
            context.Initialize(Path.Combine(root, "owner.player"));
            context.AppearPlayer.IsAlive = true;
            ushort petType = ushort.Parse(Json.ReadFromFile<JObject>("pet/pets_for_client").Properties().First().Name);
            var pet = Player.PetFactory.Build(petType, PetRank.S, 60, context.EntityId).Value;
            context.Pets.Add(new PetSaveData { Pet = pet, LifeMax = pet.Stat.Life.Max(), HungryMax = pet.Stat.Hungry.Max() });
            var state = new WorldContext { TerrainId = "pe10gr_1" };
            state.Initialize(Path.Combine(root, "island.world"));
            var world = new World(state);
            Position(context, new Point2(20, 20));
            using var link = new EconomyProtocolCheck.Link(context, world, null, true);
            Position(context, new Point2(20, 20));

            AppearArtifact appeared = default;
            world.ArtifactAppeared += a => appeared = a;
            var recipes = Json.ReadFromFile<Dictionary<string, CraftRecipeData>>("item/recipes");
            foreach (ushort type in new ushort[] { 7000, 7053, 7111, 9104 })
            {
                var fire = Cheats.MakeAppearArtifact(new[] { "prop", type.ToString() }, out _).Value;
                fire.Tile = new Point2(20, 20); fire.Tags = default;
                world.ConstructArtifact(fire, null, context.EntityId);
                Check(appeared.Tags.EntityId == fire.EntityId && appeared.Tags._Tags.Any(t => t.Id == "cook" && t.Level >= 1),
                    "fogueira " + type + " anuncia cook no primeiro envio");
                var args = new object[] { recipes["skewer"], new PropKey { EntityId = fire.EntityId, Tile = fire.Tile }, null };
                Check((bool)typeof(Player).GetMethod("CheckWorkbench", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(link.Player, args), "espeto aceito na fogueira " + type + " da ilha selvagem");
            }

            var candidates = link.Request<GetMilestoneCandidate, MilestoneCandidates>(new GetMilestoneCandidate { PetId = pet.EntityId });
            Check(candidates.Result.Length == Player.PetTables.MilestoneTags.Count &&
                Math.Abs(candidates.Result.Sum(c => c.Item2) - 1) < .0001,
                "roleta mostra todo o conjunto sorteável com probabilidades somando 100%");
            MilestoneResult rolled = default;
            for (int i = 0; i < 30; i++)
            {
                rolled = i == 0 ? link.Request<PickMilestone, MilestoneResult>(new PickMilestone { PetId = pet.EntityId }) :
                    link.Request<PickMilestoneAgain, MilestoneResult>(new PickMilestoneAgain { PetId = pet.EntityId });
                Check(candidates.Result.Any(c => c.Item1 == rolled.SelectedTagId), "resultado pertence à roda, giro " + i);
            }
            var entry = Player.PetStore.Find(context.EntityId, pet.EntityId);
            Check(context.Pets.Single().PendingMilestoneTag == rolled.SelectedTagId, "resultado pendente persistido antes da confirmação");
            link.Request<AcceptMilestone, MilestoneResult>(new AcceptMilestone { PetId = pet.EntityId });
            Check(entry.Pet.Stat.Tags.ContainsKey(rolled.SelectedTagId) && entry.Pet.Statistics.MilestonesInformation[0].Acquired &&
                context.Pets.Single().PendingMilestoneSlot == -1, "confirmação concede e persiste o atributo uma vez");
            link.Request<AcceptMilestone, Abort>(new AcceptMilestone { PetId = pet.EntityId });

            var baseStats = Player.PetFactory.DerivedOf(petType, 60, null);
            var lowPerf = Player.PetTables.PerfOf(petType, 10);
            var highPerf = Player.PetTables.PerfOf(petType, 60);
            Check(!ReferenceEquals(lowPerf, highPerf) &&
                lowPerf.HungryMax == Player.PetTables.PerfOf(petType, 10).HungryMax,
                "cálculo por nível não altera o desempenho de outros animais no cache");
            foreach (var tag in Player.PetTables.MilestoneTags)
            {
                var modified = Player.PetFactory.DerivedOf(petType, 60, new() { [tag.Id] = 1 });
                Check(tag.Target != Derived.Invalid && modified[tag.Target] != baseStats[tag.Target],
                    "atributo " + tag.Id + " altera o parâmetro correspondente");
            }
            var bonuses = new Dictionary<string, int> { ["hp_max_plus_10"] = 2, ["hp_max_amplifier_10"] = 1,
                ["hungry_max_plus_10"] = 2, ["hungry_velocity_amplifier_10"] = 1, ["hp_regen_plus_10"] = 2,
                ["life_span_plus_5"] = 1 };
            var forward = Player.PetFactory.DerivedOf(petType, 60, bonuses);
            var reverse = Player.PetFactory.DerivedOf(petType, 60, bonuses.Reverse().ToDictionary(p => p.Key, p => p.Value));
            Check(forward.All(p => p.Value == reverse[p.Key]), "efeitos independem da ordem dos atributos no save");
            entry.Pet.Stat.Tags = bonuses;
            double now = Gauge.CurrentTime;
            entry.Pet.Stat.Life = new Gauge(entry.LifeMax, 0, new[] { new GaugeNode(now, 50) });
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, 40) });
            Recalc(entry);
            Check(entry.LifeMax == forward[Derived.LifeMax] && entry.Pet.Stat.Life.Max() == entry.LifeMax &&
                entry.Pet.Stat.Life.Get(now + 10) > 50, "vida máxima e regeneração aplicadas à barra real");
            Check(entry.Pet.Stat.Hungry.Max() == forward[Derived.HungryMax] && entry.HungryVelocity == forward[Derived.HungryVelocity],
                "saciedade máxima e consumo aplicados à barra real");
            Check(entry.Pet.Stat.AgingUntil == entry.Pet.Stat.AgingSince + forward[Derived.LifeSpan], "longevidade altera o prazo real de envelhecimento");
            var saved = Json.Read<PetSaveData>(Json.Write(new PetSaveData { Pet = entry.Pet, LifeMax = 100, HungryMax = 100 }));
            var restored = (Player.PetStore.Entry)typeof(Player).GetMethod("FromSave", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { saved });
            Check(restored.Pet.Stat.Life.Max() == forward[Derived.LifeMax] && restored.HungryVelocity == forward[Derived.HungryVelocity],
                "reconexão repara os parâmetros antigos usando os atributos salvos");

            Point2 FindWater(bool ocean)
            {
                for (int x = 0; x < world.NumTilesX; x++)
                for (int y = 0; y < world.NumTilesY; y++)
                {
                    var tile = new Point2(x, y);
                    var biome = WorldStatusRules.UnmaskBiome(world.BiomeAt(tile));
                    if (WorldStatusRules.IsWaterBiome(biome) && (biome is Shared.Region.Biome.ColdOcean or Shared.Region.Biome.WarmOcean) == ocean)
                        return tile;
                }
                throw new InvalidOperationException("Ilha sem água do tipo solicitado");
            }
            var pot = Cheats.MakeItem("pot_01", 20).Value;
            context.InventoryItems.Add(pot);
            foreach (bool ocean in new[] { false, true })
            {
                Position(context, FindWater(ocean));
                string prototype = ocean ? "salt_water" : "water";
                int before = context.InventoryItems.Count(i => i.Prototype == prototype);
                var timer = link.Request<DrawWater, Messages.Timer>(new DrawWater { ToolItemId = pot.Id });
                Check(timer.Duration == 4 && context.InventoryItems.Count(i => i.Prototype == prototype) == before,
                    "água aguarda o tempo de coleta antes da entrega");
                link.PumpUntil(() => context.InventoryItems.Count(i => i.Prototype == prototype) > before);
                Check(context.InventoryItems.Count(i => i.Prototype == prototype) == before + 2 &&
                    context.InventoryItems.Any(i => i.Id == pot.Id), "panela entrega duas unidades de " + prototype + " e permanece na mochila");
            }
            Position(context, FindWater(false));
            link.Request<DrawWater, Messages.Timer>(new DrawWater { ToolItemId = pot.Id });
            link.Request<DrawWater, Abort>(new DrawWater { ToolItemId = pot.Id });
            int singleBefore = context.InventoryItems.Count(i => i.Prototype == "water");
            link.PumpUntil(() => context.InventoryItems.Count(i => i.Prototype == "water") > singleBefore);
            Check(context.InventoryItems.Count(i => i.Prototype == "water") == singleBefore + 2,
                "pedidos simultâneos não duplicam a coleta de água");
            var leaf = Cheats.MakeItem("leaf", 20).Value;
            context.InventoryItems.Add(leaf);
            link.Request<DrawWater, Abort>(new DrawWater { ToolItemId = leaf.Id });
            link.Request<DrawWater, Messages.Timer>(new DrawWater { ToolItemId = pot.Id });
            int count = context.InventoryItems.Count(i => i.Prototype == "water");
            int aborts = link.Messages.OfType<Abort>().Count();
            Position(context, new Point2(20, 20));
            link.PumpUntil(() => link.Messages.OfType<Abort>().Count() > aborts);
            Check(context.InventoryItems.Count(i => i.Prototype == "water") == count, "coleta interrompida não entrega água");
            CheckMovement(link, context, recipes);
            CheckOtherMovementActions(link, context, world, state);
            Console.WriteLine("[reported-gameplay] PASS " + _passed + " verificações");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }

    private static object Call(Player player, string method, params object[] args) => typeof(Player)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(player, args);

    private static void Move(EconomyProtocolCheck.Link link, PlayerContext context, float distance)
    {
        var position = context.AppearPlayer.Move.Movements[0].Path[0].Position;
        link.Send(new Messages.Move { EntityId = context.EntityId, Movements = new[]
        { new Movement { MotionName = distance == 0 ? "Craft" : "Walk", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(position.x + distance, position.y), Time = Gauge.CurrentTime } } } } });
        link.Request<GetSkills, Skills>(default); // Barreira TCP após processar o movimento.
    }

    private static void CheckMovement(EconomyProtocolCheck.Link link, PlayerContext context,
        Dictionary<string, CraftRecipeData> recipes)
    {
        var material = Cheats.MakeItem("wood_bough", 20).Value;
        var product = Cheats.MakeItem("leaf", 20).Value;
        context.InventoryItems.Add(material);
        string original = Json.Write(material);
        int crafted = link.Messages.OfType<Crafted>().Count();
        Call(link.Player, "BeginPendingCraft", recipes["skewer"], "skewer", null,
            new List<Item> { material }, new[] { product }, new Crafted { Result = Result.Success, Items = new[] { product } }, 300u);
        Check(!context.InventoryItems.Any(i => i.Id == material.Id), "fabricação reserva o material durante o tempo ativo");
        Move(link, context, 0);
        Check(!context.InventoryItems.Any(i => i.Id == material.Id), "troca de animação sem deslocamento preserva fabricação");
        Move(link, context, 20);
        Check(context.InventoryItems.Any(i => i.Id == material.Id && Json.Write(i) == original),
            "andar devolve o material completo com identidade e propriedades");
        Check(link.Messages.OfType<Abort>().Any(a => a.Text.Contains("Fabricação interrompida")) &&
            link.Messages.OfType<InventoryUpdated>().Any(u => u.Items?.Any(i => i.Id == material.Id) == true),
            "cliente recebe cancelamento da sequência e devolução à mochila");
        Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + 121);
        Check(!context.InventoryItems.Any(i => i.Id == product.Id) && link.Messages.OfType<Crafted>().Count() == crafted,
            "craft cancelado não entrega produto nem dispara a próxima fabricação");
        Call(link.Player, "BeginPendingCraft", recipes["skewer"], "skewer", null,
            new List<Item> { material }, new[] { product }, new Crafted { Result = Result.Success, Items = new[] { product } }, 301u);
        Call(link.Player, "UpdatePendingCrafts", Gauge.CurrentTime + 121);
        Check(context.InventoryItems.Any(i => i.Id == product.Id), "fabricação pode ser reiniciada e concluída após cancelar");
    }

    private static void CheckOtherMovementActions(EconomyProtocolCheck.Link link, PlayerContext context,
        World world, WorldContext state)
    {
        var tile = new Point2(30, 30);
        Position(context, tile);
        var blueprint = BlueprintStore.GetAllBlueprints().First(b => b.Id == "fur_table");
        var table = Cheats.MakeAppearArtifact(new[] { "prop", blueprint.EntityType.ToString() }, out _).Value;
        table.Tile = tile; table.States.BuildingState = Shared.Building.BuildingState.Occupied;
        table.States.Level = 1;
        world.ConstructArtifact(table, null, context.EntityId);
        var material = Cheats.MakeItem("axe_onehand_loose_stone", 40).Value;
        foreach (var slot in blueprint.Slots)
        {
            int capacity = (int)typeof(Player).GetMethod("SlotCapacity", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { slot, table, blueprint });
            world.ArtifactManager.AddBuildMaterials(table.EntityId, slot.slot_id, Enumerable.Range(0, capacity).Select(_ => material));
        }
        var tool = Cheats.MakeItem("axe_onehand_loose_stone", 40).Value;
        tool.Tags = blueprint.ToolTags.Select(t => new Tag { Id = t.Key, Level = Math.Max(40, t.Value) }).ToArray();
        context.InventoryItems.Add(tool);
        var build = new BuildArtifact { EntityId = table.EntityId, Tile = tile, ToolItemId = tool.Id };
        string materials = Json.Write(world.ArtifactManager.GetBuildMaterials(table.EntityId));
        link.Request<BuildArtifact, Messages.Timer>(build);
        Move(link, context, 20);
        Call(link.Player, "UpdatePendingBuildReplies", Gauge.CurrentTime + 121);
        Check(world.ArtifactManager.Get(table.EntityId).Value.States.BuildingState == Shared.Building.BuildingState.Occupied &&
            Json.Write(world.ArtifactManager.GetBuildMaterials(table.EntityId)) == materials,
            "andar cancela a construção e preserva os materiais do canteiro");
        link.Request<BuildArtifact, Messages.Timer>(build);
        Call(link.Player, "UpdatePendingBuildReplies", Gauge.CurrentTime + 121);
        Check(world.ArtifactManager.Get(table.EntityId).Value.States.BuildingState == Shared.Building.BuildingState.Built,
            "construção pode ser retomada depois da interrupção");

        var plot = Cheats.MakeAppearArtifact(new[] { "prop", "6254" }, out _).Value;
        plot.Tile = tile; world.ConstructArtifact(plot, null, context.EntityId);
        world.ArtifactManager.SeedPlant(plot.EntityId, "corn_seed", 1, Shared.Region.Biome.Grassland);
        plot = state.Artifacts[plot.EntityId];
        var farming = plot.States.Farming.Value; farming.GrowsUntil = Gauge.CurrentTime - 1;
        plot.States.Farming = farming; state.Artifacts[plot.EntityId] = plot;
        var harvest = new Collect { EntityId = plot.EntityId, Tile = tile, GeneratorId = FarmHarvest.GeneratorId(CropYaml.Get("corn_seed")) };
        int inventoryCount = context.InventoryItems.Count;
        link.Request<Collect, Messages.Timer>(harvest);
        link.Request<Collect, Abort>(harvest);
        Move(link, context, 20);
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + 121);
        Check(context.InventoryItems.Count == inventoryCount && world.ArtifactManager.Get(plot.EntityId).Value.States.Farming.HasValue,
            "andar cancela a colheita sem perder a planta nem entregar itens");
        link.Request<Collect, Messages.Timer>(harvest);
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + 121);
        Check(context.InventoryItems.Count > inventoryCount && !world.ArtifactManager.Get(plot.EntityId).Value.States.Farming.HasValue,
            "colheita retomada entrega produtos uma vez e libera o canteiro");

        int teleports = link.Messages.OfType<Teleported>().Count();
        Call(link.Player, "BeginWarp", new Point2(32, 32), 310u, "teste de retorno");
        Move(link, context, 20);
        var moved = context.AppearPlayer.Move.Movements[0].Path[0].Position;
        Call(link.Player, "UpdateReturnWarps", Gauge.CurrentTime + 121);
        link.Request<GetSkills, Skills>(default);
        var after = context.AppearPlayer.Move.Movements[0].Path[0].Position;
        Check(after.x == moved.x && after.y == moved.y && link.Messages.OfType<Teleported>().Count() == teleports,
            "andar cancela o teleporte e impede deslocamento após o prazo");
        Call(link.Player, "BeginWarp", new Point2(32, 32), 311u, "teste de retorno");
        Move(link, context, 0);
        Call(link.Player, "UpdateReturnWarps", Gauge.CurrentTime + 121);
        Check(context.AppearPlayer.Move.Movements[0].Path[0].Position.x == 32 * 200,
            "teleporte retomado funciona e troca de animação não o cancela");
        var statuses = (System.Collections.IDictionary)typeof(Player).GetField("_timedStatusEffects",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(link.Player);
        link.Request<DrinkWater, Messages.Timer>(default);
        Check(!statuses.Contains("drink_water"), "beber água aguarda o tempo da ação para conceder o efeito");
        Move(link, context, 20);
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + 121);
        Check(!statuses.Contains("drink_water"), "andar cancela beber água e o efeito pendente");
        link.Request<DrinkWater, Messages.Timer>(default);
        Move(link, context, 0);
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + 121);
        Check(statuses.Contains("drink_water"), "beber água retomado concede o efeito ao concluir");
        link.Request<WashBody, Messages.Timer>(default);
        Move(link, context, 20);
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + 121);
        Check(!statuses.Contains("clean"), "andar cancela o banho sem conceder limpeza");
    }
}
