using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Animal;
using Shared.System;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class PetFeedingCheck
{
    private static int _passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _passed++;
        Console.WriteLine("[pet-feeding-check] OK " + label);
    }
    // Reproduce the client's menu contract: pet_food plus at least one eatable tag.
    private static bool InMenu(Messages.Pet pet, Item item, bool taming = false)
    {
        var food = item.Performance?.FirstOrDefault(p => p.Id == "pet_food");
        if (food?.Id != "pet_food") return false;
        if (pet.Stat.EatableTags.Length > 0 && !item.Tags.Any(t => pet.Stat.EatableTags.Contains(t.Id))) return false;
        return !taming || food.Value.Nums.Any(p => p.Value > 0 && p.Key is
            "decrease_domesticate_time" or "decrease_domesticate_time_ratio" or "increase_domesticate_success_rate");
    }
    private static float Vigor(Item item) => item.Performance.Single(p => p.Id == "pet_food").Nums["vigor"];

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-pet-feeding-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var pets = Json.ReadFromFile<JObject>("pet/pets_for_client");
            ushort herbType = ushort.Parse(pets.Properties().First(p =>
                (int)p.Value["vehicle_entity_type"] == 2027).Name);
            ushort carniType = ushort.Parse(pets.Properties().First(p =>
                (string)p.Value["type"] == "Carnivore").Name);
            Item leaves = Cheats.MakeItem("leaf_small", 20).Value, flower = Cheats.MakeItem("flower", 20).Value, meat = Cheats.MakeItem("meat", 20).Value;
            var herb = Player.PetFactory.Build(herbType, PetRank.D, 20, "food-owner").Value;
            var carni = Player.PetFactory.Build(carniType, PetRank.D, 20, "food-owner").Value;
            Check(InMenu(herb, leaves) && InMenu(herb, flower, true), "zebraceratops com preferencia soft mostra folhas e flores apropriadas");
            Check(!InMenu(herb, meat) && InMenu(carni, meat, true) && !InMenu(carni, leaves), "menus respeitam dieta herbivora e carnivora");
            Check(!InMenu(herb, leaves, true), "folhas sem bonus nativo nao sao oferecidas durante a doma");
            Check(InMenu(herb, Cheats.MakeItem("cactus", 20).Value) && InMenu(herb, Cheats.MakeItem("sugarcane", 20).Value),
                "vegetais nativos com tags especificas aparecem no menu herbivoro");
            foreach (var p in pets.Properties())
            {
                ushort type = ushort.Parse(p.Name);
                var pet = Player.PetFactory.Build(type, PetRank.D, 20, "food-owner").Value;
                string kind = (string)p.Value["type"];
                Check(kind == "Herbivore" ? InMenu(pet, leaves) && InMenu(pet, flower, true) :
                    kind == "Carnivore" && InMenu(pet, meat, true), "cardapio disponivel para especie " + type);
            }
            var nativeFoods = Json.ReadFromFile<JObject>("performance")["pet_food"];
            string ration = nativeFoods.Children<JProperty>().First(p => p.Value.Children<JProperty>().Count() > 1 &&
                PrototypeYaml.GetItemPrototype(p.Name) != null).Name;
            Item lowRation = Cheats.MakeItem(ration, 20).Value, highRation = Cheats.MakeItem(ration, 60).Value;
            Check(Vigor(lowRation) == Player.PetTables.FoodVigor(ration, 20) &&
                Vigor(highRation) == Player.PetTables.FoodVigor(ration, 60) && Vigor(lowRation) != Vigor(highRation),
                "energia da racao corresponde a faixa de nivel nativa");
            Item oldFlower = flower;
            oldFlower.Performance = new[] { new Performance { Id = "food", Nums = new() { ["energy"] = 987 } },
                new Performance { Id = "pet_food", Nums = new() { ["vigor"] = 1 } } };
            var oldItems = new List<Item> { oldFlower };
            Item missingBlock = flower;
            missingBlock.Performance = null;
            oldItems.Add(missingBlock);
            ItemExtRepair.Normalize(oldItems, "feeding check");
            Check(InMenu(herb, oldItems[0], true) && Vigor(oldItems[0]) == Vigor(flower) &&
                oldItems[0].Performance.Single(p => p.Id == "food").Nums["energy"] == 987,
                "alimento salvo recupera pet_food completo e preserva stats de comida preparada");
            Check(InMenu(herb, oldItems[1], true), "alimento salvo sem performance recupera o filtro de doma");

            var player = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "food-owner", PlayerName = "food-owner", PlayerLevel = 60 } };
            player.Initialize(Path.Combine(root, "owner.player"));
            player.AppearPlayer.IsAlive = true;
            herb.Stat.EatableTags = new[] { "soft" };
            herb.Stat.Hungry = new Gauge(herb.Stat.Hungry.Max(), 0, new[] { new GaugeNode(Gauge.CurrentTime, 10) });
            player.Pets.Add(new PetSaveData { Pet = herb, LifeMax = herb.Stat.Life.Max(), HungryMax = herb.Stat.Hungry.Max() });
            var wc = new WorldContext { TerrainId = "grass_company_safehouse_01" };
            wc.Initialize(Path.Combine(root, "food.world"));
            var world = new World(wc);
            using var link = new EconomyProtocolCheck.Link(player, world, null, true);
            var entry = Player.PetStore.Find(player.EntityId, herb.EntityId);
            Check(InMenu(entry.Pet, leaves), "animal salvo com dieta antiga recupera menu ao reconectar");
            player.InventoryItems.AddRange(new[] { leaves, meat });
            float before = entry.Pet.Stat.Hungry.Get();
            link.Request<Feeding, OK>(new Feeding { PetId = herb.EntityId, FoodIds = new[] { leaves.Id, leaves.Id } });
            link.PumpUntil(() => link.Messages.OfType<FeedingSuccess>().Any());
            Check(!player.InventoryItems.Any(i => i.Id == leaves.Id) && Math.Abs(entry.Pet.Stat.Hungry.Get() - before - Vigor(leaves)) < 1,
                "alimentacao TCP aumenta saciedade e consome alimento uma vez");
            link.Request<Feeding, Abort>(new Feeding { PetId = herb.EntityId, FoodIds = new[] { meat.Id } });
            Check(player.InventoryItems.Any(i => i.Id == meat.Id), "carne rejeitada por herbivoro permanece na mochila");

            Point2 tile = new Point2(20, 20);
            player.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
                Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
            var grow = new AppearArtifact { EntityId = "feeding-grow", Tile = tile,
                States = new ArtifactState { EntityId = "feeding-grow", Cage = new GrowCage
                    { Size = 100, RemainSize = 90, Pets = new Messages.Pets { Data = new[] { entry.Pet } }, Tasks = new() } } };
            world.ArtifactManager.AddArtifact(grow);
            world.ArtifactManager.SetOwner(grow.EntityId, player.EntityId);
            player.InventoryItems.Add(flower);
            before = entry.Pet.Stat.Hungry.Get();
            link.Request<FeedInCage, OK>(new FeedInCage { EntityId = grow.EntityId, Tile = tile, PetId = herb.EntityId, ItemIds = new[] { flower.Id } });
            Check(!player.InventoryItems.Any(i => i.Id == flower.Id) && entry.Pet.Stat.Hungry.Get() > before,
                "alimentacao TCP de animal domado no abrigo restaura saciedade");

            double now = Gauge.CurrentTime;
            var info = new DomesticationInfo { ItemId = "taming-rein", PetEntityType = herbType, EntityType = 2027,
                Level = 20, EatableTags = new[] { "soft" }, TotalTime = 1000, DomesticateSince = now,
                DomesticateUntil = now + 1000, DomesticateSuccessRate = .2f, DomesticationSuccessMaxRate = 1,
                DomesticationInProgress = true };
            var domestic = new AppearArtifact { EntityId = "feeding-domestic", Tile = tile,
                States = new ArtifactState { EntityId = "feeding-domestic", DomesticCage = new DomesticCage
                    { Size = 100, RemainSize = 90, Reins = new[] { info } } } };
            var savedCages = new Dictionary<string, AppearArtifact> { [grow.EntityId] = grow, [domestic.EntityId] = domestic };
            savedCages = Json.Read<Dictionary<string, AppearArtifact>>(Json.Write(savedCages));
            CageTypes.NormalizeLoaded(savedCages);
            Check(savedCages[grow.EntityId].States.Cage is GrowCage loaded && InMenu(loaded.Pets.Data[0], flower) &&
                savedCages[domestic.EntityId].States.DomesticCage.Value.Reins[0].EatableTags.Contains("flower"),
                "reinicio repara filtros de abrigo e de jaula de domesticação");
            world.ArtifactManager.AddArtifact(savedCages[domestic.EntityId]);
            world.ArtifactManager.SetOwner(domestic.EntityId, player.EntityId);
            Item tamingFood = Cheats.MakeItem("flower", 20).Value;
            player.InventoryItems.Add(tamingFood);
            link.Send(new PutItemsForDomestication { EntityId = domestic.EntityId, Tile = tile, ReinId = info.ItemId,
                ItemIds = new[] { tamingFood.Id, tamingFood.Id, meat.Id } });
            link.PumpUntil(() => !player.InventoryItems.Any(i => i.Id == tamingFood.Id));
            var after = world.ArtifactManager.GetDomesticCage(domestic.EntityId).Value.Reins[0];
            Check(Math.Abs(after.DomesticateUntil - (info.DomesticateUntil - 60)) < .01 &&
                player.InventoryItems.Any(i => i.Id == meat.Id), "doma TCP aplica bonus nativo uma vez e conserva comida incompatível");
            Console.WriteLine($"[pet-feeding-check] PASS {_passed} verificacoes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[pet-feeding-check] FAIL " + ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
