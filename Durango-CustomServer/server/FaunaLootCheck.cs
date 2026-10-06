using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class FaunaLootCheck
{
    private static int _passed;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[fauna-check] OK " + message); }
    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-fauna-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            SkillDataStore.EnsureLoaded();
            AnimalTuning.Reload();
            var islandReport = new List<string>();
            foreach (string path in Directory.GetFiles(TerrainLoader.TerrainDir, "*.zip"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                var terrain = TerrainLoader.Load(id);
                var template = RegionCatalog.GetTemplate(terrain.Info.region_template);
                var manager = new AnimalManager(terrain, template);
                if (template.Role is Role.Personal or Role.Tutorial)
                    Check(manager.Count == 0, "sem fauna selvagem em " + id);
                else if (template.Role != Role.Safehouse && template.Herds.Count > 0)
                {
                    Check(manager.Count == AnimalTuning.TargetAnimalsPerRegion, $"{id}: {manager.Count} animais");
                    islandReport.Add($"| {id} | {template.Level} | {manager.Count} | {manager.All.Select(a => a.EntityType).Distinct().Count()} |");
                    Check(manager.All.All(a => TerrainEcology.IsLand(terrain, a.Tile)) &&
                        manager.All.Select(a => a.Tile).Distinct().Count() == manager.Count,
                        id + ": todos em terra e sem pontos duplicados");
                    Check(manager.All.All(a => template.Herds.Values.SelectMany(h => h).Any(s => s.EntityType == a.EntityType)),
                        id + ": especies pertencem ao template");
                    var nativeTypes = template.Herds.Values.SelectMany(h => h).Select(s => s.EntityType)
                        .Where(t => AnimalTypes.Get(t) != null).ToHashSet();
                    if (nativeTypes.Count <= manager.Count)
                        Check(nativeTypes.SetEquals(manager.All.Select(a => a.EntityType)), id + ": preserva todas as especies nativas");
                    if (template.Level > 0)
                        Check(manager.All.All(a => a.CombatLevel == template.Level && a.ToMessage().Level == template.Level),
                            id + ": nivel coerente com ilha");
                    if (template.Level == 60)
                    {
                        var animal = manager.All.First();
                        var stats = AnimalTypes.Get(animal.EntityType);
                        Check(Math.Abs(animal.LifeMax - StatFormula.EvalOr(stats.LifeMax,
                            new Dictionary<string, double> { ["combat_level"] = 60, ["unstable_factor"] = 1 }, 1)) < .01,
                            id + ": atributos de combate usam nível 60 além do número exibido");
                        animal.IsAlive = false; animal.Life = 0; animal.DiedAt = 1;
                        AnimalManager.Animal revived = null;
                        manager.Process(1 + AnimalManager.CorpseDisposeDelay, _ => { }, a => revived = a);
                        Check(revived == animal && animal.IsAlive && animal.CombatLevel == 60 && animal.Life == animal.LifeMax,
                            id + ": respawn conserva nível da ilha e restaura vida");
                        var reopened = new AnimalManager(terrain, template);
                        Check(reopened.All.All(a => a.CombatLevel == 60), id + ": recarregar a ilha mantém todos os dinos no nível 60");
                    }
                }
            }
            var level30 = TerrainLoader.Load("ri30td01");
            var originalHerds = level30.Herds;
            try
            {
                level30.Herds = null;
                Check(new AnimalManager(level30, RegionCatalog.GetTemplate(level30.Info.region_template)).Count == 40,
                    "ilha 30 recebe 40 animais mesmo sem herds.yml");
            }
            finally { level30.Herds = originalHerds; }

            var materialRewards = new Dictionary<string, string>();
            var report = new List<string> {
                "# Catálogo de loot por animal", "",
                "Gerado por `--fauna-check`. Materiais das receitas nativas são preservados; as demais partes foram reconstruídas por família, tamanho e anatomia em `AnimalLoot.cs`. O dataset do cliente não contém a tabela original completa de drops do servidor.", "",
                "O nível do item é limitado pelo menor entre o nível do animal e o esquartejamento do jogador. Cada parte exige o nó de habilidade correspondente. As quantidades continuam calculadas por `GatheringTuning` e pelo nível da carcaça.", "",
                "| Tipo | Animal / variante | Materiais coletáveis |", "|---|---|---|" };
            foreach (var property in Json.ReadFromFile<JObject>("entity_types/animal").Properties().OrderBy(p => ushort.Parse(p.Name)))
            {
                ushort type = ushort.Parse(property.Name);
                var info = AnimalTypes.Get(type);
                var expected = AnimalLoot.For(info);
                if (expected.Count == 0) continue;
                var specs = CollectibleTable.AllSpecs(type);
                report.Add($"| {type} | {info.Name} | {string.Join(", ", specs.Select(s => s.PrototypeId))} |");
                Check(specs.Count >= 2 && specs.All(s => PrototypeYaml.GetItemPrototype(s.PrototypeId) != null) &&
                    specs.Select(s => s.Id).Distinct().Count() == specs.Count, info.Name + ": loot variado e ids unicos");
                foreach (var spec in specs.Where(s => s.RequiredCollectibleReward != null))
                {
                    materialRewards[spec.PrototypeId] = spec.RequiredCollectibleReward;
                    if (!SkillDataStore.Skills.Values.SelectMany(b => b.Values).SelectMany(s => s.Values)
                        .SelectMany(nodes => nodes).Any(n => n.Rewards?.Contains(spec.RequiredCollectibleReward) == true))
                        throw new InvalidOperationException(info.Name + ": recompensa impossivel de aprender " + spec.RequiredCollectibleReward);
                }
            }
            Check(CollectibleTable.AllSpecs(2010).Any(s => s.PrototypeId == "leather_raw_armored") &&
                !CollectibleTable.AllSpecs(2010).Any(s => s.PrototypeId == "leather_raw"), "anquilossauro conserva couro blindado");
            Check(CollectibleTable.AllSpecs(2016).Any(s => s.PrototypeId == "feather") &&
                !CollectibleTable.AllSpecs(2015).Any(s => s.PrototypeId == "feather"), "penas somente nas familias correspondentes");
            Check(CollectibleTable.AllSpecs(2008).Any(s => s.PrototypeId == "bone_ivory") &&
                CollectibleTable.AllSpecs(2003).Any(s => s.PrototypeId == "bone_horn"), "marfim e chifres por especie");
            Check(!CollectibleTable.AllSpecs(2063).Any(s => s.PrototypeId == "bone_horn") &&
                !CollectibleTable.AllSpecs(2064).Any(s => s.PrototypeId == "bone_horn") &&
                !CollectibleTable.AllSpecs(2087).Any(s => s.PrototypeId == "leather_raw_fur"),
                "variantes sem galhada e elefantes sem pelagem respeitam anatomia");
            report.AddRange(new[] { "", "## Habilidades necessárias", "",
                "Os níveis abaixo permitem aprender o nó; ele precisa estar aprendido para liberar a parte. O servidor verifica a recompensa exata, pois a ordem numérica das recompensas de osso e carne não acompanha a ordem dos nós.", "",
                "| Material | Recompensa | Nó | Esquartejamento mínimo para aprender |", "|---|---|---|---|" });
            foreach (var pair in materialRewards.OrderBy(p => p.Key))
            foreach (var (bundle, subs) in SkillDataStore.Skills[(int)Shared.Skill.Category.Butchery])
            foreach (var (sub, nodes) in subs)
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Rewards?.Contains(pair.Value) == true)
                    report.Add($"| {pair.Key} | {pair.Value} | {bundle} / {sub} / {i + 1} | {nodes[i].CategoryLevel} |");
            report.AddRange(new[] { "", "## População inicial nas ilhas de caça", "", "| Ilha | Nível | Animais | Espécies |", "|---|---|---|---|" });
            report.AddRange(islandReport);
            string reportPath = Path.Combine(root, "animal-loot.md");
            File.WriteAllLines(reportPath, report);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "fauna-loot-report.md"), report);
            Console.WriteLine("[fauna-check] REPORT " + reportPath);
            Protocol(root);
            Console.WriteLine($"[fauna-check] PASS {_passed} verificacoes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[fauna-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }

    private static void Protocol(string root)
    {
        var wc = new WorldContext { TerrainId = "ri30td01" }; wc.Initialize(Path.Combine(root, "loot.world"));
        var world = new World(wc);
        var context = new PlayerContext { RegionId = world.TerrainId, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = "butcher", PlayerName = "butcher", PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, "butcher.player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        using var link = new EconomyProtocolCheck.Link(context, world, null, true);
        var tile = world.EntryPoint;
        context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
        var animal = world.AnimalManager.SpawnAt(2010, 28, tile);
        animal.IsAlive = false; animal.Life = 0; animal.DiedAt = Gauge.CurrentTime;
        var skill = (SkillCategorySave)Call(link.Player, "CategoryState", (int)Shared.Skill.Category.Butchery);
        skill.Level = 10;
        var save = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
        save.Learned["cutting"] = new() { ["__base__"] = 1 };
        save.Learned["deboning"] = new() { ["__base__"] = 2 };
        save.Learned["skinning"] = new() { ["__base__"] = 1 };
        Collectible Menu() => link.Request<GetCollectible, Collectible>(new GetCollectible { EntityId = animal.EntityId, Tile = tile });
        var beginner = Menu();
        Check(beginner.Generators.Length > 4 && beginner.Generators.All(g => g.Level <= 10),
            "menu TCP mostra todos os materiais e limita nivel pela habilidade");
        Check(!beginner.Generators.Single(g => g.Id == "leather_raw_armored").Enabled &&
            !beginner.Generators.Single(g => g.Id == "fat").Enabled &&
            !beginner.Generators.Single(g => g.Id == "bone_rib").Enabled &&
            beginner.Generators.Single(g => g.Id == "bone_leg_thick").Enabled,
            "couro blindado, gordura e costelas respeitam nos aprendidos, inclusive ordem nao numerica");
        var needed = link.Request<Collect, SkillNeeded>(new Collect { EntityId = animal.EntityId, Tile = tile,
            GeneratorId = "leather_raw_armored", Level = 60 });
        Check(needed.SkillId == "skinning" && needed.Level == 2 &&
            world.HarvestedGenerators(animal.EntityId).Count == 0, "pedido forjado exige habilidade correta sem consumir loot");
        int before = context.InventoryItems.Count;
        int replies = link.Messages.OfType<Collected>().Count();
        var timer = link.Request<Collect, Messages.Timer>(new Collect { EntityId = animal.EntityId, Tile = tile, GeneratorId = "meat", Level = 60 });
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.OfType<Collected>().Count() > replies);
        var collected = link.Messages.OfType<Collected>().Last();
        Check(context.InventoryItems.Skip(before).All(i => i.Prototype == "meat" && i.Level == 10 && i.Tags.All(t => t.Level == 10)) &&
            context.InventoryItems.Count > before && collected.ActionInfo.RelatedCategory == Shared.Skill.Category.Butchery,
            "loot TCP limitado a esquartejamento 10 apesar de jogador e pedido nivel 60");
        Check(!collected.RanOut && !animal.Butchered && Menu().Generators.Any(g => g.Id == "leather_raw_armored"),
            "coletar carne conserva os outros tipos na carcaca");
        skill.Level = 60;
        save.Learned["skinning"]["__base__"] = 2;
        var expert = Menu();
        Check(expert.Generators.Single(g => g.Id == "leather_raw_armored").Enabled && expert.Generators.All(g => g.Level == 28),
            "aprender habilidade libera couro blindado; item nao excede nivel do animal");
        before = context.InventoryItems.Count;
        replies = link.Messages.OfType<Collected>().Count();
        timer = link.Request<Collect, Messages.Timer>(new Collect { EntityId = animal.EntityId, Tile = tile,
            GeneratorId = "leather_raw_armored", Level = 60 });
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.OfType<Collected>().Count() > replies);
        Check(context.InventoryItems.Count > before && context.InventoryItems.Skip(before).All(i =>
            i.Prototype == "leather_raw_armored" && i.Level == 28 && i.Tags.All(t => t.Level == 28)),
            "mesma carcaca entrega outro tipo de loot, com tags e nivel corretos");
        link.Request<Touch, Touched>(new Touch { EntityId = animal.EntityId, EntityType = animal.EntityType, Tile = tile });
        link.Request<Collect, Abort>(new Collect { EntityId = "missing-corpse", Tile = tile, GeneratorId = "leather_raw_armored" });
        Check(context.InventoryItems.Count > before, "id inexistente nao permite tratar carcaca como recurso natural");
        skill.Level = 10;
        Check(Menu().Generators.Single(g => g.Id == "meat").Level == 10, "menu nao vaza nivel de outra consulta pelo cache");
    }
}
