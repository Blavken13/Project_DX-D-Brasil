using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class CombatBagsCheck
{
    private static int _passed;
    private static void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        _passed++; Console.WriteLine("[combat-bags] OK " + text);
    }
    private static object Call(Player player, string name, params object[] args) => typeof(Player)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, args);
    private static T Field<T>(Player player, string name) => (T)typeof(Player)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
    private static void Position(PlayerContext context, WorldPosition position, double at) =>
        context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = position, Time = at } } } };

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-combat-bags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains"); RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "test.world"));
            var world = new World(wc);
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
                PlayerEntityId = "combat-bags-tester", PlayerName = "Combat tester", PlayerLevel = 60 } };
            context.Initialize(Path.Combine(root, "test.player")); context.AppearPlayer.IsAlive = true;
            using var link = new EconomyProtocolCheck.Link(context, world, null);
            var player = link.Player; world.AddPlayer(player);

            var large = Cheats.MakeItem("bag_big", 1).Value;
            context.InventoryItems.Add(large); context.EquippedItems["bag"] = large.Id;
            Check(large.Tags.Single(t => t.Id == "pocket").Level == 20 &&
                player.CurrentInventoryCapacity == Player.InventoryMaxSize + 120, "mochilão nível 1 conserva armazenamento próprio de 120 espaços");
            var legacy = large; legacy.Tags = legacy.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = 1 } : t).ToArray();
            Check(ItemCraftModifications.Initialize(ref legacy) && legacy.Tags.Single(t => t.Id == "pocket").Level == 20,
                "normalização recupera capacidade do mochilão antigo sem alterar o nível");
            var rolled = large; rolled.Tags = rolled.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = 30 } : t).ToArray();
            ItemCraftModifications.Initialize(ref rolled);
            Check(rolled.Tags.Single(t => t.Id == "pocket").Level == 30, "normalização preserva atributo próprio sem evidência do erro antigo");
            var back = Cheats.MakeItem("bag_back", 60).Value;
            Check(back.Tags.Single(t => t.Id == "pocket").Level == 15, "mochila de costas usa seu atributo próprio em vez do nível do item");
            foreach (var (prototype, level) in new[] { ("bag_back", 60), ("bag_fabric", 15),
                ("bag_leaf", 30), ("bag_cross", 34), ("clothes_builder_01", 59), ("clothes_linen_01", 29) })
            {
                var existing = Cheats.MakeItem(prototype, level).Value;
                existing.Tags = existing.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = level } : t).ToArray();
                ItemCraftModifications.Initialize(ref existing);
                Check(existing.Tags.Single(t => t.Id == "pocket").Level == level,
                    prototype + ": preserva armazenamento antigo igual ao nível do item");
                Check(!ItemCraftModifications.Initialize(ref existing) && existing.Tags.Single(t => t.Id == "pocket").Level == level,
                    prototype + ": recarregar não reduz o armazenamento preservado");
            }
            foreach (string recipeId in new[] { "reform_pocket", "reform_pocket_t2" })
            {
                var bag = Cheats.MakeItem("bag_back", 60).Value;
                var cloth = Cheats.MakeItem("thread", 60).Value;
                var materials = new Dictionary<string, Item[]> { ["base"] = new[] { bag }, ["thread"] = new[] { cloth } };
                Check(ItemCraftModifications.Apply(ref bag, recipeId, CraftRecipeStore.Get(recipeId), materials, 0, out _) &&
                    bag.ReformSlots[0].Tags.Single().Level == 60 && bag.Tags.Single(t => t.Id == "reform_pocket").Level == 60,
                    recipeId + ": item e materiais 60 permitem bolso 60");
                context.InventoryItems.Add(bag); context.EquippedItems["bag"] = bag.Id;
                Check(player.CurrentInventoryCapacity == Player.InventoryMaxSize + 90 + 360,
                    recipeId + ": soma armazenamento base e melhoria uma vez");
                Check(!ItemCraftModifications.Apply(ref bag, recipeId, CraftRecipeStore.Get(recipeId), materials, 0, out _),
                    recipeId + ": mesmo espaço não duplica melhoria");
                var low = Cheats.MakeItem("bag_back", 60).Value;
                cloth.Level = 20; materials["thread"] = new[] { cloth };
                Check(ItemCraftModifications.Apply(ref low, recipeId, CraftRecipeStore.Get(recipeId), materials, 0, out _) &&
                    low.ReformSlots[0].Tags.Single().Level == 20, recipeId + ": materiais baixos limitam o bolso");
            }
            context.Save(); SafeSave.FlushPending();
            var loaded = PlayerContext.Load(context.Path);
            Check(Player.InventoryCapacity(loaded) == player.CurrentInventoryCapacity, "capacidade e bolso 60 preservados no save");
            var existingBag = Cheats.MakeItem("bag_back", 60).Value;
            existingBag.Tags = existingBag.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = 60 } : t).ToArray();
            var existingClothes = Cheats.MakeItem("clothes_builder_01", 59).Value;
            existingClothes.Tags = existingClothes.Tags.Select(t => t.Id == "pocket" ? new Tag { Id = t.Id, Level = 59 } : t).ToArray();
            context.InventoryItems.AddRange(new[] { existingBag, existingClothes });
            context.EquippedItems.Clear();
            context.EquippedItems["bag"] = existingBag.Id; context.EquippedItems["body"] = existingClothes.Id;
            context.Save(); SafeSave.FlushPending(); loaded = PlayerContext.Load(context.Path);
            Check(loaded.InventoryItems.Single(i => i.Id == existingBag.Id).Tags.Single(t => t.Id == "pocket").Level == 60 &&
                loaded.InventoryItems.Single(i => i.Id == existingClothes.Id).Tags.Single(t => t.Id == "pocket").Level == 59,
                "save e reconexão preservam armazenamento das bolsas e trajes anteriores ao patch");
            Check(Player.InventoryCapacity(loaded) == Player.InventoryMaxSize + 360 + 354,
                "itens antigos equipados mantêm sua capacidade real após reconexão");
            var existingMaterials = new Dictionary<string, Item[]> { ["base"] = new[] { existingBag },
                ["thread"] = new[] { Cheats.MakeItem("thread", 60).Value } };
            Check(ItemCraftModifications.Apply(ref existingBag, "reform_pocket", CraftRecipeStore.Get("reform_pocket"), existingMaterials, 0, out _) &&
                existingBag.Tags.Single(t => t.Id == "pocket").Level == 60 && existingBag.Tags.Single(t => t.Id == "reform_pocket").Level == 60,
                "melhoria de bolso preserva armazenamento antigo e adiciona seu efeito separado");
            context.InventoryItems[context.InventoryItems.FindIndex(i => i.Id == existingBag.Id)] = existingBag;
            Check(player.CurrentInventoryCapacity == Player.InventoryMaxSize + 360 + 354 + 360,
                "capacidade antiga e melhoria de bolso somadas uma vez");

            var ordinary = new BattleAttackInfo { damage_bonus = 1 };
            Check(CombatDamage.Calculate(100, ordinary, 300) == 25, "defesa 300 reduz ataque 100 para 25 sem piso artificial de 1");
            Check(CombatDamage.Calculate(100, new() { damage_bonus = 2 }, 300) == 50 &&
                CombatDamage.Calculate(100, new() { damage_bonus = 1, armor_penetration = 1 }, 300) == 100,
                "bônus do golpe e penetração funcionam independentemente");
            Check(CombatDamage.Calculate(100, new() { damage_bonus = 1, atk_ratio = new() { ["cut"] = .5f, ["impact"] = .5f } },
                100, new() { ["cut"] = 0, ["impact"] = 2 }) == 50, "resistência pondera todos os tipos do golpe");
            Check(CombatDamage.Calculate(100, new() { damage_bonus = 0 }, 300) == 0, "golpe sem dano não recebe dano básico artificial");
            var actions = Json.ReadFromFile<Dictionary<string, BattleActionData>>("player/player_battle_actions");
            var weaponData = Json.ReadFromFile<JObject>("performance")["weapon"];
            int weaponCount = 0, hitCount = 0;
            foreach (var entry in ((JObject)weaponData).Properties())
            {
                var item = Cheats.MakeItem(entry.Name, 60);
                if (!item.HasValue) continue;
                float power = EquipmentStats.Value(item.Value, "weapon", "attack");
                Check(power > 0, entry.Name + ": atributo de ataque válido no nível 60");
                var proto = PrototypeYaml.GetItemPrototype(entry.Name);
                foreach (var actionId in proto.Tags.Keys.Select(BattleDataStore.ActionsOfTag).Where(a => a != null)
                    .SelectMany(a => a.default_actions.Concat(a.skill_actions)).Distinct())
                    foreach (var hit in actions[actionId].attack_info ?? Array.Empty<BattleAttackInfo>())
                    {
                        Check(CombatDamage.Calculate(40 + power, hit, 300) > 1,
                            entry.Name + "/" + actionId + ": golpe calculado contra defesa de dino nível 60");
                        hitCount++;
                    }
                weaponCount++;
            }
            Check(weaponCount > 30 && hitCount > 100, $"catálogo auditado: {weaponCount} armas e {hitCount} golpes");
            var modified = Cheats.MakeItem("bow_wooden_01", 60).Value;
            float basePower = EquipmentStats.Value(modified, "weapon", "attack");
            modified.Tags = modified.Tags.Append(new Tag { Id = "attack_incr", Level = 10 }).ToArray();
            Check(Math.Abs(EquipmentStats.Value(modified, "weapon", "attack") - basePower - 33) < .01,
                "atributo de ataque da arma contribui no dano real");

            var skills = Field<SkillSave>(player, "_skills");
            skills.Exp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 60 });
            foreach (var category in new[] { Category.MeleeCombat, Category.RangedCombat, Category.Defense })
                ((SkillCategorySave)Call(player, "CategoryState", (int)category)).Level = 60;
            string swordId = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k =>
                PrototypeYaml.GetItemPrototype(k).Tags?.ContainsKey("sword_onehand") == true && PerformanceYaml.GetWeapon(k) != null);
            var sword = Cheats.MakeItem(swordId, 60).Value;
            context.InventoryItems.Add(sword); context.EquippedItems.Clear(); context.EquippedItems["main"] = sword.Id;
            link.Request<LearnSkill, OK>(new LearnSkill { SkillId = "onehanded_flurry", SubId = "__base__", Level = 1 });
            var tile = world.EntryPoint;
            ushort animalType = ushort.Parse(Json.ReadFromFile<JObject>("entity_types/animal").Properties().First(p =>
                ushort.TryParse(p.Name, out var id) && AnimalTypes.Get(id) != null && AnimalTypes.Get(id).Kind != "Sandbag").Name);
            var animal = world.AnimalManager.SpawnAt(animalType, 60, tile);
            if (animal == null) throw new InvalidOperationException("Animal de teste indisponível");
            animal.Life = animal.LifeMax = 100000; animal.Defense = 300;
            Position(context, animal.Position, Gauge.CurrentTime);
            link.Send(new UseBattleAction { ActionId = "onehand_flurry", TargetEntityId = animal.EntityId, StartAt = 1 });
            link.Request<GetActions, Actions>(default);
            var pending = Field<IList>(player, "_pendingBattleHits");
            Check(pending.Count == 3 && animal.Life == animal.LifeMax, "TCP: ativar rajada agenda três impactos sem dano imediato");
            var due = pending.Cast<object>().Select(p => (double)p.GetType().GetProperty("DueAt").GetValue(p)).ToArray();
            Call(player, "UpdatePendingBattleHits", due[0] - .001);
            Check(animal.Life == animal.LifeMax, "não aplica dano antes do fim do primeiro golpe");
            float before = animal.Life;
            Call(player, "UpdatePendingBattleHits", due[0] + .001);
            link.PumpUntil(() => link.Messages.OfType<Damaged>().Any(m => m.VictimId == animal.EntityId));
            var damage = link.Messages.OfType<Damaged>().Last(m => m.VictimId == animal.EntityId);
            Check(before - animal.Life == damage.Damage.Value && damage.Damage.Value > 1 && Math.Abs(damage.EventAt - due[0]) < .01,
                "primeiro golpe sincroniza vida, valor e timestamp de impacto em vez de StartAt do cliente");
            Position(context, new WorldPosition(animal.Position.x + 3000, animal.Position.y), due[1]);
            before = animal.Life; Call(player, "UpdatePendingBattleHits", due[1] + .001);
            Check(animal.Life == before, "segundo golpe não acerta depois de sair do alcance");
            Position(context, animal.Position, due[2]);
            Call(player, "UpdatePendingBattleHits", due[2] + .001);
            Check(animal.Life < before && pending.Count == 0, "terceiro golpe usa seu próprio impacto e alcance");
            before = animal.Life; Call(player, "UpdatePendingBattleHits", due[2] + 10);
            Check(animal.Life == before, "processar novamente não duplica impactos");
            Call(player, "CancelBattleHits");
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_default_a", TargetEntityId = animal.EntityId, StartAt = double.MaxValue });
            Check(pending.Count == 1 && animal.Life == before, "ataque básico também aguarda animação e ignora timestamp forjado");
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_default_b", TargetEntityId = animal.EntityId });
            Check(pending.Count == 1, "repetição durante animação não empilha ataques básicos");
            double basicAt = (double)pending[0].GetType().GetProperty("DueAt").GetValue(pending[0]);
            Call(player, "UpdatePendingBattleHits", basicAt + .001);
            Check(animal.Life < before && pending.Count == 0, "ataque básico aplica dano apenas em seu impacto");
            before = animal.Life;
            Call(player, "CancelBattleHits");
            Call(player, "QueueBattleHits", actions["onehand_flurry"], animal.EntityId, Gauge.CurrentTime);
            context.AppearPlayer.IsAlive = false; Call(player, "UpdatePendingBattleHits", Gauge.CurrentTime + 10);
            Check(pending.Count == 0 && animal.Life == before, "morte cancela todos os impactos pendentes");
            context.AppearPlayer.IsAlive = true;
            Call(player, "QueueBattleHits", actions["onehand_flurry"], animal.EntityId, Gauge.CurrentTime);
            context.EquippedItems.Clear(); Call(player, "UpdatePendingBattleHits", Gauge.CurrentTime + 10);
            Check(pending.Count == 0 && animal.Life == before, "trocar arma cancela combo pendente");
            context.EquippedItems["main"] = sword.Id;
            Call(player, "QueueBattleHits", actions["onehand_flurry"], animal.EntityId, Gauge.CurrentTime);
            context.InventoryItems.RemoveAll(i => i.Id == sword.Id);
            Call(player, "UpdatePendingBattleHits", Gauge.CurrentTime + 10);
            Check(pending.Count == 0 && animal.Life == before, "arma ausente no inventário não produz golpe de skill com ataque de punhos");
            context.InventoryItems.Add(sword);
            Call(player, "QueueBattleHits", actions["onehand_flurry"], animal.EntityId, Gauge.CurrentTime);
            animal.Captured = true; Call(player, "UpdatePendingBattleHits", Gauge.CurrentTime + 10);
            Check(pending.Count == 0 && animal.Life == before, "animal capturado não recebe impactos atrasados");
            animal.Captured = false;
            double testAt = Gauge.CurrentTime + 100;
            Position(context, animal.Position, testAt);
            foreach (var entry in actions.Where(a => a.Value.attack_info?.Length > 0))
            {
                animal.Life = 100000; animal.IsAlive = true;
                Call(player, "QueueBattleHits", entry.Value, animal.EntityId, testAt);
                var times = pending.Cast<object>().Select(p => (double)p.GetType().GetProperty("DueAt").GetValue(p)).ToArray();
                Check(times.Length == entry.Value.attack_info.Length && times.All(t => t > testAt),
                    entry.Key + ": agenda todos os golpes após o começo da animação");
                for (int i = 0; i < times.Length; i++)
                {
                    int count = pending.Count;
                    Call(player, "UpdatePendingBattleHits", times[i] - .0001);
                    Check(pending.Count == count, entry.Key + ": golpe " + (i + 1) + " espera seu tempo");
                    Call(player, "UpdatePendingBattleHits", times[i] + .0001);
                    Check(pending.Count == count - 1, entry.Key + ": golpe " + (i + 1) + " resolve uma vez");
                }
            }
            var charge = actions["twohand_lance_dash"];
            var origin = animal.Position;
            Position(context, origin, testAt);
            animal.Position = new WorldPosition(origin.x, origin.y + 250);
            Call(player, "QueueBattleHits", charge, animal.EntityId, testAt);
            animal.Position = new WorldPosition(origin.x, origin.y + 800);
            before = animal.Life; Call(player, "UpdatePendingBattleHits", testAt + 5);
            Check(animal.Life == before, "investida não usa alcance de ativação como alcance de impacto");
            animal.Position = new WorldPosition(origin.x, origin.y + 250);
            Call(player, "QueueBattleHits", charge, animal.EntityId, testAt);
            animal.Position = new WorldPosition(origin.x + 800, origin.y + 250);
            before = animal.Life; Call(player, "UpdatePendingBattleHits", testAt + 5);
            Check(animal.Life == before, "golpe retangular não acerta animal que saiu lateralmente da trajetória");
            animal.Position = origin;
            Call(player, "QueueBattleHits", actions["onehand_default_a"], animal.EntityId, testAt);
            animal.WalkFrom = origin; animal.WalkTo = new WorldPosition(origin.x + 3000, origin.y);
            animal.WalkStartAt = testAt; animal.WalkEndAt = testAt + 2;
            before = animal.Life; Call(player, "UpdatePendingBattleHits", testAt + 1);
            Check(animal.Life == before, "impacto usa posição interpolada do animal em movimento, mesmo com tile antigo");
            Console.WriteLine($"[combat-bags] PASS {_passed} verificações. Saves temporários: {root}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[combat-bags] FAIL " + error); return 1; }
    }
}
