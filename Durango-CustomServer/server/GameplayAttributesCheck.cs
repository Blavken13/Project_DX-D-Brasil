using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Ability;
using Shared.Estate;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GameplayAttributesCheck
{
    private static int _passed;
    private static void Check(bool pass, string text)
    { if (!pass) throw new InvalidOperationException(text); _passed++; Console.WriteLine("[attributes-check] OK " + text); }
    private static object Call(Player p, string method, params object[] args) => typeof(Player)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(p, args);
    private static T Field<T>(Player p, string name) => (T)typeof(Player)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p);
    private static void Position(PlayerContext c, Point2 tile) => c.AppearPlayer.Move.Movements = new[] {
        new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[] { new Location {
            Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };

    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "LostHorizon-attributes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string previous = AppData.BasePath;
        try
        {
            AppData.BasePath = root; Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains"); RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "ri30td01" }; wc.Initialize(Path.Combine(root, "attributes.world"));
            var world = new World(wc);
            var c = new PlayerContext { RegionId = world.TerrainId, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
                PlayerEntityId = "attributes-tester", PlayerName = "Tester", PlayerLevel = 60 } };
            c.Initialize(Path.Combine(root, "tester.player")); c.AppearPlayer.IsAlive = true; Position(c, world.EntryPoint);
            using var link = new EconomyProtocolCheck.Link(c, world, null, true);
            var p = link.Player; var skills = Field<SkillSave>(p, "_skills");
            foreach (Category cat in Enum.GetValues<Category>()) if (cat != Category.Invalid)
                ((SkillCategorySave)Call(p, "CategoryState", (int)cat)).Level = 60;
            skills.Learned.Clear(); Call(p, "SendFullStatistics");
            Statistics Stats()
            {
                int before = link.Messages.OfType<Statistics>().Count(); link.Send(default(GetStatistics));
                link.PumpUntil(() => link.Messages.OfType<Statistics>().Count() > before);
                return link.Messages.OfType<Statistics>().Last();
            }
            var survival = Field<SurvivalState>(p, "_survival");
            var baseline = Stats(); double now = Gauge.CurrentTime;
            float maxHealth = survival.MaxOf("health", now), maxEnergy = survival.MaxOf("energy", now);
            foreach (string id in new[] { "health", "energy", "life_regen", "base_defense" })
                link.Request<LearnSkill, OK>(new LearnSkill { SkillId = id, SubId = "__base__", Level = 1 });
            var learned = Stats();
            Check(Math.Abs(survival.MaxOf("health", Gauge.CurrentTime) - maxHealth - 25) < .01 &&
                learned.DerivedsAbilities[Derived.MaxHealth] == survival.MaxOf("health", Gauge.CurrentTime), "saúde aprendida aumenta o teto real em 25 e a estatística coincide com a barra");
            Check(Math.Abs(survival.MaxOf("energy", Gauge.CurrentTime) - maxEnergy - 10) < .01, "sobrevivência aumenta energia real em 10");
            Check(learned.DerivedsAbilities[Derived.Defense] == baseline.DerivedsAbilities[Derived.Defense] + 10 &&
                (float)Call(p, "CurrentDerivedDefense") == learned.DerivedsAbilities[Derived.Defense], "defesa aprendida usada no cálculo real de combate");
            survival.Set("life", 50); now = Gauge.CurrentTime; survival.Flush(now);
            Check(Math.Abs(survival.ValueAt("life", now + 10) - 61) < .02, "regeneração aprendida recupera 11 de vida em dez segundos");
            Call(p, "ApplyTimedStatusEffect", "life_up", 5, (double?)180); Call(p, "SendStatusEffects", 0u);
            survival.Set("life", 50); now = Gauge.CurrentTime; survival.Flush(now);
            Check(Math.Abs(survival.ValueAt("life", now + 10) - 91) < .02, "buff nível 5 aplica sua intensidade real de regeneração");
            Call(p, "ClearTimedStatusEffect", "life_up"); Call(p, "SendStatusEffects", 0u);
            float beforeCost = p.EnergyCostScale();
            Call(p, "ApplyTimedStatusEffect", "energetic", 1, (double?)60); Call(p, "SendStatusEffects", 0u);
            Check(Math.Abs(p.EnergyCostScale() / beforeCost - .6f) < .001, "vigor reduz em 40% o custo real de energia");
            Call(p, "ClearTimedStatusEffect", "energetic"); Call(p, "SendStatusEffects", 0u);
            Check(Math.Abs(p.EnergyCostScale() - beforeCost) < .001, "remover buff restaura o custo sem bônus residual");

            Item Product(string recipeId, Item material)
            {
                var recipe = CraftRecipeStore.Get(recipeId);
                var sent = new Dictionary<string, string[]> { [recipe.slots[0].slot_id] = new[] { material.Id } };
                c.InventoryItems.Add(material);
                var estimate = link.Request<EstimateCraft, CraftEstimationInfo>(new EstimateCraft { RecipeId = recipeId, Materials = sent });
                var product = ((Item[])Call(p, "MakeProducts", recipe, sent, new List<Item> { material }, estimate.CraftEstimation.Value.Level)).First();
                Check((material.TagModifications ?? Array.Empty<Tag>()).All(t => product.Tags.Any(u => u.Id == t.Id && u.Level == t.Level)), recipeId + ": conserva propriedades e intensidade do material");
                Check(estimate.CraftEstimation.Value.Tags.All(t => product.Tags.Any(u => u.Id == t.Key && (u.Level == t.Value || u.Id is "weapon" or "armor"))), recipeId + ": prévia anuncia as propriedades entregues");
                Check(product.ColorR == material.ColorR, recipeId + ": herda a cor indicada na receita");
                string saved = Json.Write(product); var reload = Json.Read<Item>(saved);
                Check(reload.TagModifications.Length == product.TagModifications.Length, recipeId + ": propriedades preservadas no save");
                return product;
            }
            var metal = Cheats.MakeItem("metal_iron", 40).Value; metal.ColorR = "123456";
            metal.TagModifications = new[] { new Tag { Id = "hardness_hard", Level = 3 }, new Tag { Id = "attack_incr", Level = 2 }, new Tag { Id = "purity_high", Level = 40 } };
            metal.Tags = metal.Tags.Where(t => !metal.TagModifications.Any(m => m.Id == t.Id)).Concat(metal.TagModifications).ToArray();
            var blade = Product("blade_metal", metal);
            var lowerBlade = ((Item[])Call(p, "MakeProducts", CraftRecipeStore.Get("blade_metal"),
                new Dictionary<string, string[]> { ["base"] = new[] { metal.Id } }, new List<Item> { metal }, 10)).First();
            Check(lowerBlade.Tags.Single(t => t.Id == "purity_high").Level == 10 &&
                lowerBlade.Tags.Single(t => t.Id == "hardness_hard").Level == 3 &&
                lowerBlade.Tags.Single(t => t.Id == "attack_incr").Level == 2,
                "nível nominal limita pureza principal e preserva intensidade das propriedades adicionais");
            Check(Math.Abs(EquipmentStats.Value(blade, "weapon", "attack") - ItemPerformance.Of(blade.Prototype, blade.Level)
                .First(b => b.Id == "weapon").Nums["attack"] - 6.6f) < .01, "bônus herdado aumenta o ataque real da lâmina em 6,6");
            var leaf = Cheats.MakeItem("leaf", 40).Value; leaf.ColorR = "654321";
            leaf.TagModifications = new[] { new Tag { Id = "defense_incr", Level = 2 } }; leaf.Tags = leaf.Tags.Concat(leaf.TagModifications).ToArray();
            var hat = Product("hat_leaf", leaf); c.InventoryItems.Add(hat); c.EquippedItems["head"] = hat.Id;
            var equipped = Stats(); Check(equipped.DerivedsAbilities[Derived.Defense] >= learned.DerivedsAbilities[Derived.Defense] + 4.6f,
                "armadura herdada acrescenta defesa ao personagem equipado");
            c.EquippedItems.Clear();

            var raw = Cheats.MakeItem("meat", 10).Value; var cooked = raw;
            Check(ItemCraftModifications.Apply(ref cooked, "fry", CraftRecipeStore.Get("fry"), new(), null, out _), "fritura tem resultado aplicável");
            Check(cooked.Icon != raw.Icon && cooked.Name != raw.Name && !cooked.Tags.Any(t => t.Id == "raw_food") &&
                cooked.Performance.Single(b => b.Id == "food").Strs?.GetValueOrDefault("effect_on") != "raw_food", "alimento preparado altera aparência, nome e estado cru");
            float Eat(Item food)
            {
                c.InventoryItems.Add(food); survival.Set("energy", 0); survival.Flush(Gauge.CurrentTime);
                link.Request<UseItem, OK>(new UseItem { ItemId = food.Id, Accept = true });
                return survival.ValueAt("energy", Gauge.CurrentTime);
            }
            float rawEnergy = Eat(raw); cooked.Id = Guid.NewGuid().ToString(); float cookedEnergy = Eat(cooked);
            Check(cookedEnergy > rawEnergy + 20, "preparo aumenta energia realmente recuperada ao comer");
            var strengthBefore = Stats().BasicAbilities[Basic.Strength];
            Eat(Cheats.MakeItem("meatball_01", 10).Value);
            Check(Stats().BasicAbilities[Basic.Strength] > strengthBefore && link.Messages.OfType<StatusEffects>().Last()._StatusEffects
                .Any(e => e.EffectId == "food_ability" && e.Effects.Any(d => d.Key == "strength_plus")), "buff do alimento aplica força e envia valor ao cliente");
            var active = Field<System.Collections.IDictionary>(p, "_timedStatusEffects");
            object foodStatus = active["food_ability"];
            foodStatus.GetType().GetField("Until").SetValue(foodStatus, Times.UnixTimeNow() - 1);
            Check((bool)Call(p, "ExpireTimedStatusEffects"), "prazo vencido expira o buff do alimento");
            Call(p, "SendStatusEffects", 0u);
            Check(Stats().BasicAbilities[Basic.Strength] == strengthBefore, "expiração do alimento remove os atributos concedidos");

            Point2 waterTile = default; bool found = false;
            for (int x = 0; x < world.NumTilesX && !found; x++) for (int y = 0; y < world.NumTilesY; y++)
                if (WorldStatusRules.UnmaskBiome(world.BiomeAt(new Point2(x,y))) is Shared.Region.Biome.River or Shared.Region.Biome.Lake)
                { waterTile = new(x,y); found = true; break; }
            Check(found, "ilha de teste contém água doce"); Position(c, waterTile);
            var pot = Cheats.MakeItem("pot_01", 20).Value; c.InventoryItems.Add(pot);
            var timer = link.Request<DrawWater, Messages.Timer>(new DrawWater { ToolItemId = pot.Id });
            Call(p, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + 1);
            link.PumpUntil(() => c.InventoryItems.Any(i => i.Prototype == "water"));
            var water = c.InventoryItems.First(i => i.Prototype == "water");
            var materials = new Dictionary<string, string[]> { ["base"] = new[] { cooked.Id }, ["water"] = new[] { water.Id } };
            c.InventoryItems.Add(cooked);
            var boiled = link.Request<EstimateCraft, CraftEstimationInfo>(new EstimateCraft { RecipeId = "boil", Materials = materials });
            Check(boiled.CraftEstimation != null && water.Tags.Any(t => t.Id == "water" && t.Level >= 1), "água coletada aparece na mochila e é ingrediente válido no preparo");
            float healthCeiling = survival.MaxOf("health", Gauge.CurrentTime);
            survival.Set("health", healthCeiling * .1f); survival.Set("life", 10); survival.Flush(Gauge.CurrentTime);
            var medicine = Cheats.MakeItem("hp_medicine_store", 10).Value; c.InventoryItems.Add(medicine);
            link.Request<UseItem, OK>(new UseItem { ItemId = medicine.Id, Accept = true });
            Check(Math.Abs(survival.ValueAt("health", Gauge.CurrentTime) - healthCeiling * .7f) < .5 &&
                Math.Abs(survival.ValueAt("life", Gauge.CurrentTime) - (10 + healthCeiling * .6f)) < .5,
                "remédio aplica recuperação percentual real de vida e saúde");
            float healthBeforeUntrain = survival.MaxOf("health", Gauge.CurrentTime);
            link.Request<UntrainSkill, OK>(new UntrainSkill { SkillId = "health", SubId = "__base__", Level = 1 });
            Check(Math.Abs(survival.MaxOf("health", Gauge.CurrentTime) - healthBeforeUntrain + 25) < .01,
                "desaprender saúde remove exatamente o bônus concedido");
            Check(SafeSave.FlushPending(), "saves temporários concluídos");
            Console.WriteLine($"[attributes-check] PASS {_passed} verificações"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); AppData.BasePath = previous; Directory.Delete(root, true); }
    }
}
